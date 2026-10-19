using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Szablon przebiegu na ekranie Przebiegi: definicja, aktywny, programy, które go wybrały (Parametry programu → „Przebieg”).</summary>
public sealed record RunTemplate(RunDefinition Definition, bool IsActive, IReadOnlyList<string> Programs);

/// <summary>
/// Szablony przebiegów (ekran Przebiegi – docs/pipeline-fazy.md, rozdz. 4.1): odczyt i zapis szablonu jako wierszy słowników
/// „Definicje przebiegów”, „Etapy przebiegów”, „Elementy kontrolne etapów” (historia, walidacja – DictionaryService), usunięcie,
/// szablony startowe (Wewnętrzny, CAS).
/// </summary>
public sealed class RunTemplateService(DictionaryService dictionaries, IRunStore store)
{
    private static DictionarySpec HeaderSpec => GlobalDictionaries.Get(GlobalDictionaries.RunDefinitionHeaders);
    private static DictionarySpec StepSpec => GlobalDictionaries.Get(GlobalDictionaries.RunSteps);
    private static DictionarySpec ElementSpec => GlobalDictionaries.Get(GlobalDictionaries.RunStepElements);

    /// <summary>Szablony (także nieaktywne) z etapami i programami, które je wybrały.</summary>
    public IReadOnlyList<RunTemplate> Load()
    {
        var headers = RunDefinitionRows.Headers(dictionaries.Load(HeaderSpec));
        var steps = RunDefinitionRows.Steps(dictionaries.Load(StepSpec));
        var elements = RunDefinitionRows.Elements(dictionaries.Load(ElementSpec));
        var assignments = store.DefinitionAssignments();
        return headers.OrderBy(h => h.Code, StringComparer.CurrentCulture)
            .Select(h => new RunTemplate(RunDefinitions.Build(h, steps, elements), h.IsActive,
                assignments.Where(a => string.Equals(a.Value, h.Code, StringComparison.OrdinalIgnoreCase)).Select(a => a.Key).Order().ToList()))
            .ToList();
    }

    /// <summary>
    /// Zapis szablonu (nowy albo zmieniony – nazwa bez zmian): kontrola struktury (RunDefinitions.Validate), potem szablon, etapy i
    /// elementy kontrolne – wiersze o tym samym kluczu zachowują historię. Ostrzeżenia słowników nie blokują zapisu (w wyniku).
    /// </summary>
    public SaveOutcome Save(RunDefinition definition, bool isActive)
    {
        var numbered = definition with { Code = definition.Code.Trim(), Steps = [.. definition.Steps.OrderBy(s => s.No).Select((s, i) => s with { No = i + 1, Name = s.Name.Trim() })] };
        var structure = RunDefinitions.Validate(numbered);
        if (structure.Any(i => i.Level == CheckLevel.Error))
            return new SaveOutcome(SaveStatus.Rejected, structure, "Szablon ma błędy – nic nie zapisano.");
        return SaveRows(numbered.Code, [RunDefinitionRows.Header(numbered, isActive)], RunDefinitionRows.StepValues(numbered),
            RunDefinitionRows.ElementValues(numbered), $"Szablon „{numbered.Code}” zapisany ({numbered.Steps.Count} etapów).");
    }

    /// <summary>Usunięcie szablonu (historia zostaje); szablon wybrany w programach – odmowa.</summary>
    public SaveOutcome Delete(string code)
    {
        var programs = store.DefinitionAssignments().Where(a => string.Equals(a.Value, code, StringComparison.OrdinalIgnoreCase)).Select(a => a.Key).Order().ToList();
        if (programs.Count > 0)
            return new SaveOutcome(SaveStatus.Rejected, [Issue.Error($"Szablon wybrany w programach: {string.Join(", ", programs)}")],
                "Szablon jest używany – najpierw zmień przebieg w tych programach (Konfiguracja → Parametry programu) albo wyłącz szablon.");
        return SaveRows(code, [], [], [], $"Szablon „{code}” usunięty.");
    }

    /// <summary>Szablony startowe, których jeszcze nie ma (Wewnętrzny – domyślny dla wszystkich typów, CAS – dla CAS).</summary>
    public string CreateStarters()
    {
        var existing = RunDefinitionRows.Headers(dictionaries.Load(HeaderSpec)).Select(h => h.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var made = new List<string>();
        foreach (var starter in RunDefinitions.Starters.Where(s => !existing.Contains(s.Code)))
        {
            var outcome = Save(starter, isActive: true);
            if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
                return $"Szablon „{starter.Code}” nie zapisany: {outcome.Message} {string.Join("; ", outcome.Issues.Where(i => i.Level == CheckLevel.Error).Take(3).Select(i => i.Message))}";
            made.Add(starter.Code);
        }
        return made.Count == 0 ? "Szablony startowe już są." : $"Utworzono szablony: {string.Join(", ", made)}.";
    }

    /// <summary>Ustawienia domyślne reguł katalogu dla wszystkich programów (słownik „Kontrole przebiegu”: „Wszystkie”, „Każdy”; brak wiersza – katalog).</summary>
    public IReadOnlyDictionary<string, RunCheckSetting> CheckDefaults() =>
        RunChecks.Settings(store.CheckSettings().Where(r => Is(r.ProjectType, RunCheckRules.AllTypes) && Is(r.RunKind, RunCheckRules.AnyRun)),
            RunCheckRules.AllTypes, isClosing: false);

    /// <summary>
    /// Zapis ustawień domyślnych reguł („Wszystkie”, „Każdy”): reguła z ustawieniem katalogu – bez wiersza, inna – wiersz (aktywna,
    /// poziom, próg); wiersze typów programów i wariantów zostają.
    /// </summary>
    public SaveOutcome SaveCheckDefaults(IReadOnlyDictionary<string, RunCheckSetting> settings)
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.RunChecks);
        var current = dictionaries.Load(spec);
        bool General(DictRow r) => Is(r["Typ programu"], RunCheckRules.AllTypes) && Is(r["Przebieg"], RunCheckRules.AnyRun);
        var working = current.Where(r => !General(r)).ToList();
        var general = current.Where(General).ToList();
        var kept = new HashSet<long>();
        foreach (var rule in RunCheckRules.All)
        {
            if (!settings.TryGetValue(rule.Code, out var setting) || setting == RunCheckRules.Default(rule))
                continue;
            var values = new Dictionary<string, string?>
            {
                ["Typ programu"] = RunCheckRules.AllTypes, ["Reguła"] = rule.Name, ["Przebieg"] = RunCheckRules.AnyRun, ["Aktywna"] = setting.Active ? "tak" : "nie",
                ["Poziom"] = RunCheckRules.Levels[setting.Level switch { CheckLevel.Error => 0, CheckLevel.Warning => 1, _ => 2 }],
                ["Próg"] = rule.Threshold is null || setting.Threshold is not { } threshold ? null : PzlEv.Shared.Utils.Files.PolishNumber.ToCanonical(threshold),
                ["Opis"] = null,
            };
            var existing = general.FirstOrDefault(r => Is(r["Reguła"], rule.Name));
            working.Add(existing is { RowId: { } id } && kept.Add(id) ? existing with { Values = values } : new DictRow(null, null, values));
        }
        var outcome = dictionaries.Save(spec, working, general.Where(r => r.RowId is not { } id || !kept.Contains(id)).ToList(), confirmWarnings: true);
        return outcome.Status == SaveStatus.Saved ? outcome with { Message = "Ustawienia kontroli zapisane." } : outcome;
    }

    private static bool Is(string? value, string expected) => string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    // Kolejność: szablon (dodanie przed etapami), etapy, elementy; przy usuwaniu – odwrotnie (najpierw elementy, na końcu szablon).
    private SaveOutcome SaveRows(string code, IReadOnlyList<IReadOnlyDictionary<string, string?>> header, IReadOnlyList<IReadOnlyDictionary<string, string?>> steps,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> elements, string message)
    {
        (DictionarySpec Spec, IReadOnlyList<IReadOnlyDictionary<string, string?>> Values)[] parts =
            [(HeaderSpec, header), (StepSpec, steps), (ElementSpec, elements)];
        if (header.Count == 0)
            Array.Reverse(parts);
        var warnings = new List<Issue>();
        var saved = false;
        foreach (var (spec, values) in parts)
        {
            var (working, removed) = RunDefinitionRows.Replace(spec, dictionaries.Load(spec), code, values);
            var outcome = dictionaries.Save(spec, working, removed, confirmWarnings: true);
            if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
                return outcome with { Message = $"{spec.Name}: {outcome.Message}" + (saved ? " Część zmian zapisana – popraw i zapisz ponownie." : "") };
            saved |= outcome.Status == SaveStatus.Saved;
            warnings.AddRange(outcome.Issues.Where(i => i.Level == CheckLevel.Warning));
        }
        return new SaveOutcome(saved ? SaveStatus.Saved : SaveStatus.NoChanges, warnings, saved ? message : "Bez zmian.");
    }
}
