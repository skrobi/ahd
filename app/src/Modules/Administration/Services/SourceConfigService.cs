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
/// kanonicznych i jej pola; definicja mapuje na nie kolumny pliku i wskazuje pola wymagane.
/// </summary>
public sealed partial class SourceConfigService(ISourceConfigStore store, IJournal journal)
{
    private const string Area = "Administracja";

    public IReadOnlyList<SourceDefinitionRow> Definitions() => store.Definitions();

    public IReadOnlyList<SourceDefinitionRow> DefinitionHistory(long definitionId) => store.DefinitionHistory(definitionId);

    public IReadOnlyList<SourceLocationRow> Locations() => store.Locations();

    /// <summary>
    /// Kolumny z pola „Oczekiwane kolumny”: jedna w wierszu albo rozdzielone tabulatorem (wiersz nagłówków skopiowany
    /// z Excela); puste pomijane.
    /// </summary>
    public static IReadOnlyList<string> ParseColumns(string text) =>
        text.Split(['\r', '\n', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Sygnatura układu kolumn definicji – porównywana przy imporcie z sygnaturą nagłówków pliku.</summary>
    public static string Signature(IReadOnlyList<string> columns) => columns.Count > 0 ? HeaderSignature.Compute(columns) : "";

    /// <summary>„Mapuj po nazwach”: kolumna → pole parsera o tej samej nazwie w pliku (Label) albo w bazie (Field); bez wymaganych.</summary>
    public static IReadOnlyList<ColumnMapping> MapByName(IReadOnlyList<string> columns, IReadOnlyList<ParserField> fields)
    {
        var mapping = new List<ColumnMapping>();
        foreach (var column in columns)
        {
            var field = fields.FirstOrDefault(f => string.Equals(f.Label, column, StringComparison.OrdinalIgnoreCase))
                        ?? fields.FirstOrDefault(f => string.Equals(f.Field, column, StringComparison.OrdinalIgnoreCase));
            if (field is not null && mapping.All(m => m.Field != field.Field))
                mapping.Add(new ColumnMapping(column, field.Field, false));
        }
        return mapping;
    }

    public IReadOnlyList<ParserRow> Parsers() => store.Parsers();

    public IReadOnlyList<ParserRow> ParserHistory(long parserId) => store.ParserHistory(parserId);

    /// <summary>
    /// Zapisuje parser (nowa wersja) i zakłada albo rozszerza jego tabelę danych kanonicznych. Pola usunięte z parsera
    /// zostają w tabeli (dane zapisane), ale nie mogą być używane w mapowaniu definicji.
    /// </summary>
    public ConfigSaveResult SaveParser(ParserInput input)
    {
        var current = input.ParserId is { } id ? store.Parsers().FirstOrDefault(p => p.ParserId == id) : null;
        input = input with
        {
            Code = current?.Code ?? input.Code.Trim().ToUpperInvariant(),
            Name = input.Name.Trim(),
            Fields = input.Fields
                .Where(f => f.Field.Trim().Length > 0 || f.Label.Trim().Length > 0)
                .Select(f => f with
                {
                    Field = f.Field.Trim(),
                    Label = f.Label.Trim().Length > 0 ? f.Label.Trim() : f.Field.Trim(),
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
            Columns = ParseColumns(string.Join("\n", input.Columns)),
        };
        var parser = ActiveParser(input.Parser);
        input = input with { Mapping = parser is null ? [] : NormalizeMapping(input) };
        var issues = ValidateDefinition(input, parser);
        if (issues.Count > 0)
            return new ConfigSaveResult(false, issues, "Definicja ma błędy – nie zapisano.");

        var signature = Signature(input.Columns);
        var conflict = store.SaveDefinition(input, signature, parser?.Version ?? 0);
        if (conflict is not null)
            return new ConfigSaveResult(false, [], conflict);

        journal.Add(Area, $"Definicja źródła {input.Code} (prefiks {input.Prefix}) {(input.DefinitionId is null ? "dodana" : "zmieniona")}{(input.Active ? "" : " – nieaktywna")}");
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

    /// <summary>Mapowanie z wpisami „kolumna → pole” (bez pustych), kolumny w pisowni z listy oczekiwanych kolumn.</summary>
    private static List<ColumnMapping> NormalizeMapping(DefinitionInput input) =>
        (input.Mapping ?? [])
            .Where(m => m.Field.Trim().Length > 0)
            .Select(m => m with
            {
                Column = input.Columns.FirstOrDefault(c => string.Equals(c, m.Column.Trim(), StringComparison.OrdinalIgnoreCase)) ?? m.Column.Trim(),
                Field = m.Field.Trim(),
            })
            .ToList();

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
        if (input.Fields.Count == 0)
            issues.Add(Issue.Error("Pola: co najmniej jedno pole", at));

        foreach (var field in input.Fields)
        {
            var name = field.Field.Length > 0 ? field.Field : field.Label;
            if (!SqlCanonical.FieldName().IsMatch(field.Field) || SqlCanonical.FixedColumns.Contains(field.Field, StringComparer.OrdinalIgnoreCase))
                issues.Add(Issue.Error($"Pole w bazie „{name}”: litera, potem litery, cyfry i _ (bez spacji), inne niż {string.Join(", ", SqlCanonical.FixedColumns)}", at));
            if (!FieldTypes.All.Contains(field.Type))
                issues.Add(Issue.Error($"Pole {name}: typ wymagany ({string.Join(", ", FieldTypes.All.Select(FieldTypes.Label))})", at));
            if (field.Type == FieldTypes.Text && field.Length is < 1 or > FieldTypes.MaxTextLength)
                issues.Add(Issue.Error($"Pole {name}: długość tekstu od 1 do {FieldTypes.MaxTextLength}", at));
            if (field.PadDigits is { } pad && (pad < 1 || pad > (field.Length ?? FieldTypes.DefaultTextLength)))
                issues.Add(Issue.Error($"Pole {name}: dopełnianie zerami od 1 do długości pola", at));
        }
        foreach (var duplicate in input.Fields.GroupBy(f => f.Field, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1 && g.Key.Length > 0))
            issues.Add(Issue.Error($"Pole {duplicate.Key} występuje kilka razy", at));

        if (current is not null)
        {
            // Pole usunięte z parsera nie może zostać w mapowaniu definicji (import by go nie znalazł).
            var removed = current.Fields.Select(f => f.Field).Except(input.Fields.Select(f => f.Field), StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var definition in store.Definitions().Where(d => d.Parser == current.Code))
            foreach (var used in definition.Mapping.Where(m => removed.Contains(m.Field, StringComparer.OrdinalIgnoreCase)))
                issues.Add(Issue.Error($"Pole {used.Field} jest zmapowane w definicji {definition.Code} (kolumna {used.Column}) – najpierw zmień mapowanie", at));
        }
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

        if (parser is null)
            return issues;
        var mapping = input.Mapping ?? [];
        if (mapping.Count == 0)
            issues.Add(Issue.Error($"Mapowanie: przypisz kolumnom pliku pola parsera {parser.Code} (albo wybierz parser „brak” – tylko wiersze surowe)", at));
        foreach (var line in mapping)
        {
            if (!input.Columns.Contains(line.Column, StringComparer.OrdinalIgnoreCase))
                issues.Add(Issue.Error($"Mapowanie: kolumny {line.Column} nie ma w oczekiwanych kolumnach", at));
            if (parser.Fields.All(f => !string.Equals(f.Field, line.Field, StringComparison.OrdinalIgnoreCase)))
                issues.Add(Issue.Error($"Mapowanie: pola {line.Field} nie ma w parserze {parser.Code}", at));
        }
        foreach (var duplicate in mapping.GroupBy(m => m.Field, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(Issue.Error($"Mapowanie: pole {duplicate.Key} przypisane kilku kolumnom ({string.Join(", ", duplicate.Select(m => m.Column))})", at));
        foreach (var duplicate in mapping.GroupBy(m => m.Column, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(Issue.Error($"Mapowanie: kolumna {duplicate.Key} przypisana kilku polom", at));
        return issues;
    }

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex CodePattern();
}
