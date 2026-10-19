using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
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
        var structure = RunDefinitions.Validate(numbered, ValidationRules().Rules.Select(r => r.Name).ToList());
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

    private static DictionarySpec ValidationSpec => GlobalDictionaries.Get(GlobalDictionaries.RunValidations);

    /// <summary>Reguły walidacji ACTUALS ze słownika; pusty słownik – reguły startowe (Starters = true – jeszcze niezapisane).</summary>
    public (IReadOnlyList<RunValidationRule> Rules, bool Starters) ValidationRules()
    {
        var rules = RunValidationRows.Rules(dictionaries.Load(ValidationSpec));
        return rules.Count == 0 ? (RunValidationRules.Starters, true) : (rules, false);
    }

    /// <summary>
    /// Zapis reguł walidacji (cały słownik): reguła o tej samej nazwie zachowuje historię, reguły spoza listy – usunięte. Komunikat
    /// wymienia reguły użyte w szablonach, których już nie ma (w przebiegu – ostrzeżenie „niesprawdzona”).
    /// </summary>
    public SaveOutcome SaveValidations(IReadOnlyList<RunValidationRule> rules)
    {
        var current = dictionaries.Load(ValidationSpec);
        var byName = current.Where(r => r["Nazwa"] is not null).GroupBy(r => r["Nazwa"]!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var kept = new HashSet<long>();
        var working = rules.Select(rule => byName.TryGetValue(rule.Name.Trim(), out var row) && row.RowId is { } id && kept.Add(id)
            ? row with { Values = RunValidationRows.Values(rule) } : new DictRow(null, null, RunValidationRows.Values(rule))).ToList();
        var outcome = dictionaries.Save(ValidationSpec, working, current.Where(r => r.RowId is not { } id || !kept.Contains(id)).ToList(), confirmWarnings: true);
        if (outcome.Status != SaveStatus.Saved)
            return outcome;
        var names = rules.Select(r => r.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = RunDefinitionRows.Elements(dictionaries.Load(ElementSpec)).Select(e => e.Element.Trim())
            .Where(e => !RunDefinitions.IsReconciliation(e) && RunCheckRules.ByName(e) is null && !names.Contains(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return outcome with
        {
            Message = $"Reguły walidacji zapisane ({rules.Count})." + (orphans.Count == 0 ? ""
                : $" Szablony przebiegów używają reguł, których już nie ma: {string.Join(", ", orphans)} – usuń je z kafelków albo przywróć regułę."),
        };
    }

    /// <summary>
    /// Test reguły na ostatnim uruchomieniu programu z przypiętymi danymi (i poprzednim jak w przebiegu) – bez zapisu wyników;
    /// okresy jak w chwili uruchomienia.
    /// </summary>
    public (IReadOnlyList<RunValidationResult> Results, string Note, string? Error) TestValidation(RunValidationRule rule, string project)
    {
        var pin = store.Pins(project, 20).FirstOrDefault(p => p.Rows is not null && p.Status != RunStatuses.Running);
        if (pin is null)
            return ([], $"Program {project} nie ma uruchomienia z przypiętymi danymi – uruchom przebieg na ekranie programu.", null);
        var amountField = pin.AmountField ?? ProjectSettings.DefaultActualsAmount;
        var previousPin = store.Previous(project, pin.RunId);
        var previous = previousPin is null || (previousPin.AmountField ?? ProjectSettings.DefaultActualsAmount) != amountField ? null : store.Balance(previousPin.PinId);
        var (closedBefore, today) = RunService.Periods(store.RunWeek(pin.RunId).Period, pin.StartedAt.LocalDateTime.Date);
        var current = store.Balance(pin.PinId);
        var data = new RunValidationData(current, previous, store.CostElements(project), closedBefore, today,
            amountField == ProjectSettings.DefaultActualsAmount ? "PLN" : null);
        var (results, error) = RunValidationLanguage.Evaluate(rule, data);
        var note = $"Uruchomienie {pin.Code} #{pin.PinNo} ({current.Count:#,0} pozycji, {current.Sum(r => (long)r.Rows):#,0} wierszy ACTUALS)"
            + (previous is null ? ", bez poprzedniego przebiegu" : $", porównanie z {previousPin!.Code} #{previousPin.PinNo}")
            + $"; bieżący okres {today.Year}/{today.Period:00}, zamknięte przed {closedBefore.Year}/{closedBefore.Period:00}; wyników: {results.Count}.";
        return (results, note, error);
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
