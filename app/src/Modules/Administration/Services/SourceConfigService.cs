using System.Text.RegularExpressions;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Administration.Services;

/// <summary>
/// Definicje źródeł i lokalizacje RABIT (docs/zrodla-danych.md, rozdz. 2–3; F08): walidacja, zapis z historią,
/// dziennik. Prefiks unikalny bez rozróżniania wielkości liter – każdy prefiks to osobne źródło.
/// </summary>
public sealed partial class SourceConfigService(ISourceConfigStore store, IJournal journal)
{
    /// <summary>Wersja parsera ACTUALS (zapisywana w definicji i w danych kanonicznych).</summary>
    public const int ActualsParserVersion = 1;

    private const string Area = "Administracja";

    public IReadOnlyList<SourceDefinitionRow> Definitions() => store.Definitions();

    public IReadOnlyList<SourceDefinitionRow> DefinitionHistory(long definitionId) => store.DefinitionHistory(definitionId);

    public IReadOnlyList<SourceLocationRow> Locations() => store.Locations();

    public ConfigSaveResult SaveDefinition(DefinitionInput input)
    {
        input = input with
        {
            Code = input.Code.Trim().ToUpperInvariant(),
            Prefix = input.Prefix.Trim().TrimEnd('*').Trim(),
            Columns = input.Columns.Select(c => c.Trim()).Where(c => c.Length > 0).ToList(),
        };
        var issues = ValidateDefinition(input);
        if (issues.Count > 0)
            return new ConfigSaveResult(false, issues, "Definicja ma błędy – nie zapisano.");

        var signature = input.Columns.Count > 0 ? HeaderSignature.Compute(input.Columns) : "";
        var parserVersion = input.Parser == SourceParsers.Actuals ? ActualsParserVersion : 0;
        var conflict = store.SaveDefinition(input, signature, parserVersion);
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

    private List<Issue> ValidateDefinition(DefinitionInput input)
    {
        var issues = new List<Issue>();
        const string at = "definicja";
        if (!CodePattern().IsMatch(input.Code))
            issues.Add(Issue.Error("Kod: wymagany, wielkie litery, cyfry i _ (np. ACTUALS_PAF)", at));
        if (input.Prefix.Length == 0)
            issues.Add(Issue.Error("Prefiks: pole wymagane (początek nazwy pliku, np. ACTUALS_PAF)", at));
        else if (input.Prefix.IndexOfAny(['*', '?']) >= 0)
            issues.Add(Issue.Error("Prefiks: znaki * i ? dozwolone tylko na końcu – prefiks to początek nazwy pliku (np. ACTUALS_ obejmuje wszystkie ACTUALS_…)", at));
        if (!SourceParsers.All.Contains(input.Parser))
            issues.Add(Issue.Error($"Parser '{input.Parser}' – dozwolone: brak, {SourceParsers.Actuals}", at));

        var others = store.Definitions().Where(d => d.DefinitionId != input.DefinitionId).ToList();
        if (others.Any(d => string.Equals(d.Prefix, input.Prefix, StringComparison.OrdinalIgnoreCase)))
            issues.Add(Issue.Error($"Prefiks {input.Prefix} jest już używany – każdy prefiks to osobne źródło", at));
        if (others.Any(d => d.Code == input.Code))
            issues.Add(Issue.Error($"Kod {input.Code} jest już używany", at));

        if (input.Parser == SourceParsers.Actuals)
        {
            var missing = SourceParsers.ActualsColumns.Where(c => !input.Columns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0)
                issues.Add(Issue.Error($"Parser {SourceParsers.Actuals} wymaga kolumn: {string.Join(", ", missing)}", at));
        }
        return issues;
    }

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex CodePattern();
}
