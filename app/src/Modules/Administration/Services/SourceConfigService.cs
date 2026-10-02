using System.Text.RegularExpressions;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Administration.Services;

/// <summary>
/// Definicje źródeł, parsery i lokalizacje RABIT (docs/zrodla-danych.md, rozdz. 2–3; F08): walidacja, zapis z historią,
/// dziennik. Prefiks unikalny bez rozróżniania wielkości liter – każdy prefiks to osobne źródło. Parser to tabela danych
/// kanonicznych i jej pola z kolumnami pliku, typami i polami wymaganymi – pilnuje układu pliku; definicja wskazuje parser.
/// </summary>
public sealed partial class SourceConfigService(ISourceConfigStore store, IJournal journal)
{
    private const string Area = "Administracja";

    public IReadOnlyList<SourceDefinitionRow> Definitions() => store.Definitions();

    public IReadOnlyList<SourceDefinitionRow> DefinitionHistory(long definitionId) => store.DefinitionHistory(definitionId);

    public IReadOnlyList<SourceLocationRow> Locations() => store.Locations();

    /// <summary>Nazwa pola w bazie z nazwy kolumny pliku: słowa wielką literą, bez znaków spoza A–Z i cyfr („Invoice Number” → InvoiceNumber).</summary>
    public static string FieldName(string column)
    {
        var latin = string.Concat(column.Select(c => Polish.TryGetValue(c, out var l) ? l : c));
        var name = string.Concat(NonAlphanumeric().Split(latin).Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        if (name.Length == 0)
            name = "Pole";
        if (!char.IsAsciiLetter(name[0]))
            name = "F" + name;
        return name.Length > 64 ? name[..64] : name;
    }

    /// <summary>
    /// Parser a wiersz nagłówków pliku: kolumny pliku, których nie czyta żadne pole (propozycje nowych pól – tekst,
    /// nazwa w bazie z nazwy kolumny) i pola, których kolumny nie ma w pliku.
    /// </summary>
    public static (IReadOnlyList<ParserField> NewFields, IReadOnlyList<ParserField> MissingInFile) CompareWithFile(
        IReadOnlyList<ParserField> fields, IReadOnlyList<string> headers)
    {
        var columns = headers.Select(h => h.Trim()).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var names = fields.Select(f => f.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newFields = new List<ParserField>();
        foreach (var column in columns.Where(c => fields.All(f => !string.Equals(f.Column, c, StringComparison.OrdinalIgnoreCase))))
        {
            var name = FieldName(column);
            var unique = name;
            for (var i = 2; names.Contains(unique); i++)
                unique = $"{name}{i}";
            names.Add(unique);
            newFields.Add(new ParserField(unique, column, FieldTypes.Text, FieldTypes.DefaultTextLength));
        }
        var missing = fields.Where(f => f.Column.Length > 0 && !columns.Contains(f.Column, StringComparer.OrdinalIgnoreCase)).ToList();
        return (newFields, missing);
    }

    public IReadOnlyList<ParserRow> Parsers() => store.Parsers();

    public IReadOnlyList<ParserRow> ParserHistory(long parserId) => store.ParserHistory(parserId);

    /// <summary>
    /// Zapisuje parser (nowa wersja) i zakłada albo rozszerza jego tabelę danych kanonicznych. Pola usunięte z parsera
    /// zostają w tabeli (dane zapisane), ale import ich już nie wypełnia.
    /// </summary>
    public ConfigSaveResult SaveParser(ParserInput input)
    {
        var current = input.ParserId is { } id ? store.Parsers().FirstOrDefault(p => p.ParserId == id) : null;
        input = input with
        {
            Code = current?.Code ?? input.Code.Trim().ToUpperInvariant(),
            Name = input.Name.Trim(),
            Fields = input.Fields
                .Where(f => f.Field.Trim().Length > 0 || f.Column.Trim().Length > 0)
                .Select(f => f with
                {
                    Field = f.Field.Trim(),
                    Column = f.Column.Trim(),
                    Length = f.Type == FieldTypes.Text ? f.Length ?? FieldTypes.DefaultTextLength : null,
                    PadDigits = f.Type == FieldTypes.Text ? f.PadDigits : null,
                })
                .ToList(),
        };
        var table = current?.Table ?? input.Code;
        var issues = ValidateParser(input, current);
        if (issues.Count > 0)
            return new ConfigSaveResult(false, issues, "Parser ma błędy – nie zapisano.");

        var outcome = store.SaveParser(input, table);
        if (outcome.Conflict is not null)
            return new ConfigSaveResult(false, [], outcome.Conflict);

        var changes = outcome.TableChanges.Count == 0 ? "tabela bez zmian" : string.Join("; ", outcome.TableChanges);
        journal.Add(Area, $"Parser {input.Code} {(current is null ? "dodany" : "zmieniony")} ({input.Fields.Count} pól; {changes})");
        return new ConfigSaveResult(true, [], $"Zapisano parser {input.Code} – {changes}.");
    }

    public ConfigSaveResult SaveDefinition(DefinitionInput input)
    {
        input = input with
        {
            Code = input.Code.Trim().ToUpperInvariant(),
            Prefix = input.Prefix.Trim().TrimEnd('*').Trim(),
        };
        var issues = ValidateDefinition(input, ActiveParser(input.Parser));
        if (issues.Count > 0)
            return new ConfigSaveResult(false, issues, "Definicja ma błędy – nie zapisano.");

        var conflict = store.SaveDefinition(input);
        if (conflict is not null)
            return new ConfigSaveResult(false, [], conflict);

        journal.Add(Area, $"Definicja źródła {input.Code} (prefiks {input.Prefix}, parser {(input.Parser.Length == 0 ? "brak" : input.Parser)}) " +
                          $"{(input.DefinitionId is null ? "dodana" : "zmieniona")}{(input.Active ? "" : " – nieaktywna")}");
        return new ConfigSaveResult(true, [], $"Zapisano definicję {input.Code}.");
    }

    /// <summary>
    /// Usuwa definicję: bieżąca wersja zostaje zamknięta (historia i zaimportowane dane zostają); pliki o tym
    /// prefiksie przy kolejnym imporcie są nierozpoznane, prefiks i kod można użyć ponownie.
    /// </summary>
    public ConfigSaveResult DeleteDefinition(long definitionId, int version)
    {
        var definition = store.Definitions().FirstOrDefault(d => d.DefinitionId == definitionId);
        var conflict = store.DeleteDefinition(definitionId, version);
        if (conflict is not null)
            return new ConfigSaveResult(false, [], conflict);
        journal.Add(Area, $"Definicja źródła {definition?.Code} (prefiks {definition?.Prefix}) usunięta");
        return new ConfigSaveResult(true, [], $"Usunięto definicję {definition?.Code}.");
    }

    public ConfigSaveResult SaveLocation(LocationInput input)
    {
        // Link do folderu SharePoint skopiowany z przeglądarki zamieniany na ścieżkę WebDAV (UNC).
        input = input with { Name = input.Name.Trim(), Path = WebDavPath.ToUnc(input.Path) };
        var issues = new List<Issue>();
        if (input.Name.Length == 0)
            issues.Add(Issue.Error("Nazwa: pole wymagane", "lokalizacja"));
        if (input.Path.Length == 0)
            issues.Add(Issue.Error("Ścieżka: pole wymagane", "lokalizacja"));
        if (store.Locations().Any(l => l.LocationId != input.LocationId && string.Equals(l.Name, input.Name, StringComparison.OrdinalIgnoreCase)))
            issues.Add(Issue.Error($"Lokalizacja o nazwie {input.Name} już istnieje", "lokalizacja"));
        if (issues.Count > 0)
            return new ConfigSaveResult(false, issues, "Lokalizacja ma błędy – nie zapisano.");

        var conflict = store.SaveLocation(input);
        if (conflict is not null)
            return new ConfigSaveResult(false, [], conflict);

        journal.Add(Area, $"Lokalizacja RABIT {input.Name} ({input.Path}) {(input.LocationId is null ? "dodana" : "zmieniona")}{(input.Active ? "" : " – nieaktywna")}");
        return new ConfigSaveResult(true, [], $"Zapisano lokalizację {input.Name}.");
    }

    private ParserRow? ActiveParser(string code) =>
        code == SourceParsers.None ? null : store.Parsers().FirstOrDefault(p => p.Code == code && p.Active);

    private List<Issue> ValidateParser(ParserInput input, ParserRow? current)
    {
        var issues = new List<Issue>();
        var at = $"parser {input.Code}";
        if (!CodePattern().IsMatch(input.Code) || input.Code.Length > 30 || !SqlCanonical.TableName().IsMatch(input.Code))
            issues.Add(Issue.Error("Kod: wymagany, wielkie litery, cyfry i _ (do 30 znaków), np. FORECAST", at));
        if (input.Name.Length == 0)
            issues.Add(Issue.Error("Nazwa: pole wymagane", at));
        if (current is null && store.Parsers().Any(p => p.Code == input.Code))
            issues.Add(Issue.Error($"Parser {input.Code} już istnieje", at));
        if (input.Fields.All(f => f.Column.Length == 0))
            issues.Add(Issue.Error("Pola: co najmniej jedno pole z kolumną w pliku", at));

        foreach (var field in input.Fields)
        {
            var name = field.Field.Length > 0 ? field.Field : field.Column;
            if (!SqlCanonical.FieldName().IsMatch(field.Field) || SqlCanonical.FixedColumns.Contains(field.Field, StringComparer.OrdinalIgnoreCase))
                issues.Add(Issue.Error($"Pole w bazie „{name}”: litera, potem litery, cyfry i _ (bez spacji), inne niż {string.Join(", ", SqlCanonical.FixedColumns)}", at));
            if (!FieldTypes.All.Contains(field.Type))
                issues.Add(Issue.Error($"Pole {name}: typ wymagany ({string.Join(", ", FieldTypes.All.Select(FieldTypes.Label))})", at));
            if (field.Type == FieldTypes.Text && field.Length is < 1 or > FieldTypes.MaxTextLength)
                issues.Add(Issue.Error($"Pole {name}: długość tekstu od 1 do {FieldTypes.MaxTextLength}", at));
            if (field.PadDigits is { } pad && (pad < 1 || pad > (field.Length ?? FieldTypes.DefaultTextLength)))
                issues.Add(Issue.Error($"Pole {name}: dopełnianie zerami od 1 do długości pola", at));
            if (field.Required && field.Column.Length == 0)
                issues.Add(Issue.Error($"Pole {name}: wymagane, ale bez kolumny w pliku", at));
        }
        foreach (var duplicate in input.Fields.GroupBy(f => f.Field, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1 && g.Key.Length > 0))
            issues.Add(Issue.Error($"Pole {duplicate.Key} występuje kilka razy", at));

        foreach (var duplicate in input.Fields.Where(f => f.Column.Length > 0).GroupBy(f => f.Column, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(Issue.Error($"Kolumna w pliku {duplicate.Key} przypisana kilku polom ({string.Join(", ", duplicate.Select(f => f.Field))})", at));
        return issues;
    }

    private List<Issue> ValidateDefinition(DefinitionInput input, ParserRow? parser)
    {
        var issues = new List<Issue>();
        const string at = "definicja";
        if (!CodePattern().IsMatch(input.Code))
            issues.Add(Issue.Error("Kod: wymagany, wielkie litery, cyfry i _ (np. ACTUALS_PAF)", at));
        if (input.Prefix.Length == 0)
            issues.Add(Issue.Error("Prefiks: pole wymagane (początek nazwy pliku, np. ACTUALS_PAF)", at));
        else if (input.Prefix.IndexOfAny(['*', '?']) >= 0)
            issues.Add(Issue.Error("Prefiks: znaki * i ? dozwolone tylko na końcu – prefiks to początek nazwy pliku (np. ACTUALS_ obejmuje wszystkie ACTUALS_…)", at));
        if (input.Parser != SourceParsers.None && parser is null)
            issues.Add(Issue.Error($"Parser {input.Parser} nie istnieje albo jest nieaktywny (Administracja → Parsery)", at));

        var others = store.Definitions().Where(d => d.DefinitionId != input.DefinitionId).ToList();
        if (others.Any(d => string.Equals(d.Prefix, input.Prefix, StringComparison.OrdinalIgnoreCase)))
            issues.Add(Issue.Error($"Prefiks {input.Prefix} jest już używany – każdy prefiks to osobne źródło", at));
        if (others.Any(d => d.Code == input.Code))
            issues.Add(Issue.Error($"Kod {input.Code} jest już używany", at));

        return issues;
    }

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    private static readonly Dictionary<char, char> Polish = new()
    {
        ['ą'] = 'a', ['ć'] = 'c', ['ę'] = 'e', ['ł'] = 'l', ['ń'] = 'n', ['ó'] = 'o', ['ś'] = 's', ['ź'] = 'z', ['ż'] = 'z',
        ['Ą'] = 'A', ['Ć'] = 'C', ['Ę'] = 'E', ['Ł'] = 'L', ['Ń'] = 'N', ['Ó'] = 'O', ['Ś'] = 'S', ['Ź'] = 'Z', ['Ż'] = 'Z',
    };
}
