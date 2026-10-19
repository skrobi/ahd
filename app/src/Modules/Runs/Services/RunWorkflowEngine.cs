using System.Text.Json;
using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Modules.Runs.Services;

/// <summary>
/// Przebieg etapowy (docs/pipeline-fazy.md, rozdz. 1.1–1.2 i 4.1): bramki etapów, kolejny etap do wykonania, potwierdzenie,
/// ponowne wykonanie i stan uruchomienia z etapów; ustawienia reguł kontroli etapu z jego elementów kontrolnych. Bez bazy – stan
/// etapów przekazuje RunService.
/// </summary>
public static class RunWorkflowEngine
{
    /// <summary>
    /// Status etapu po wykonaniu: ERROR – Błąd (przebieg stoi do poprawy i ponownego wykonania); zatwierdzenie ręczne albo WARNING przy
    /// bramce „Potwierdzenie” – Wymaga akcji; inaczej Zakończony.
    /// </summary>
    public static string Gate(RunStepDefinition step, int errors, int warnings) =>
        errors > 0 ? RunStepStatuses.Failed
        : step.NeedsApproval || warnings > 0 && step.WarningGate == RunGates.Confirm ? RunStepStatuses.NeedsAction
        : RunStepStatuses.Done;

    /// <summary>Kolejny etap do wykonania: pierwszy niezakończony, gdy czeka na wykonanie (Do wykonania, Oczekuje, Nieaktualny); null – przebieg stoi albo skończony.</summary>
    public static RunStepRow? Next(IReadOnlyList<RunStepRow> steps) =>
        steps.OrderBy(s => s.StepNo).FirstOrDefault(s => s.Status != RunStepStatuses.Done) is { } step
        && step.Status is RunStepStatuses.Ready or RunStepStatuses.Waiting or RunStepStatuses.Stale
            ? step : null;

    /// <summary>Potwierdzenie możliwe dla etapu, który wymaga akcji (WARNING przy bramce albo zatwierdzenie ręczne).</summary>
    public static bool CanConfirm(IReadOnlyList<RunStepRow> steps, int stepNo) =>
        steps.FirstOrDefault(s => s.StepNo == stepNo)?.Status == RunStepStatuses.NeedsAction;

    /// <summary>Ponowne wykonanie etapu: wykonany albo zatrzymany (nie w toku), wszystkie wcześniejsze zakończone.</summary>
    public static bool CanRerun(IReadOnlyList<RunStepRow> steps, int stepNo) =>
        steps.FirstOrDefault(s => s.StepNo == stepNo) is { Status: not (RunStepStatuses.Running or RunStepStatuses.Waiting) }
        && steps.Where(s => s.StepNo < stepNo).All(s => s.Status == RunStepStatuses.Done)
        && steps.All(s => s.Status != RunStepStatuses.Running);

    /// <summary>Statusy po ponownym wykonaniu etapu: etap – Do wykonania, późniejsze już wykonane albo zatrzymane – Nieaktualny.</summary>
    public static IReadOnlyList<(int StepNo, string Status)> Rerun(IReadOnlyList<RunStepRow> steps, int stepNo) =>
        steps.Where(s => s.StepNo >= stepNo)
            .Select(s => (s.StepNo, s.StepNo == stepNo ? RunStepStatuses.Ready : s.Status == RunStepStatuses.Waiting ? RunStepStatuses.Waiting : RunStepStatuses.Stale))
            .ToList();

    /// <summary>
    /// Stan uruchomienia z etapów: etap w toku – w toku; etap z błędem – zatrzymany; etap wymagający akcji – wymaga akcji; wszystkie
    /// zakończone – zakończony (z ostrzeżeniami, gdy były); inaczej (przerwany przed końcem) – zatrzymany.
    /// </summary>
    public static string PinStatus(IReadOnlyList<RunStepRow> steps) =>
        steps.Any(s => s.Status == RunStepStatuses.Running) ? RunStatuses.Running
        : steps.Any(s => s.Status == RunStepStatuses.Failed) ? RunStatuses.Blocked
        : steps.Any(s => s.Status == RunStepStatuses.NeedsAction) ? RunStatuses.NeedsAction
        : steps.Count > 0 && steps.All(s => s.Status == RunStepStatuses.Done) ? steps.Any(s => s.Warnings > 0) ? RunStatuses.Warnings : RunStatuses.Ok
        : RunStatuses.Blocked;

    /// <summary>Podsumowanie uruchomienia: stan, etap, na którym stoi (albo „wszystkie etapy”), liczby wyników.</summary>
    public static string Summary(IReadOnlyList<RunStepRow> steps)
    {
        var status = PinStatus(steps);
        var stop = steps.OrderBy(s => s.StepNo).FirstOrDefault(s => s.Status != RunStepStatuses.Done);
        var where = stop is null ? $"wszystkie etapy ({steps.Count}) zakończone" : $"etap {stop.StepNo} „{stop.Name}” – {stop.Status.ToLowerInvariant()}";
        return $"{status}: {where}; błędy {steps.Sum(s => s.Errors)}, ostrzeżenia {steps.Sum(s => s.Warnings)}";
    }

    /// <summary>
    /// Ustawienia reguł katalogu dla etapu kontroli: reguły spoza elementów etapu – wyłączone; element – aktywny (w definicji
    /// wbudowanej także wg słownika „Kontrole przebiegu”), poziom i próg elementu, a puste – ze słownika „Kontrole przebiegu” (defaults).
    /// </summary>
    public static IReadOnlyDictionary<string, RunCheckSetting> Settings(RunStepDefinition step, IReadOnlyDictionary<string, RunCheckSetting> defaults, bool builtIn) =>
        RunCheckRules.All.ToDictionary(rule => rule.Code, rule =>
        {
            var basic = defaults.GetValueOrDefault(rule.Code) ?? RunCheckRules.Default(rule);
            return step.Elements.FirstOrDefault(e => RunCheckRules.ByName(e.Element) == rule) is { } element
                ? new RunCheckSetting(element.Active && (!builtIn || basic.Active), element.Level ?? basic.Level,
                    rule.Threshold is null ? null : element.Threshold ?? basic.Threshold)
                : basic with { Active = false };
        });

    /// <summary>
    /// Reguły uzgodnień etapu: „Uzgodnienie: *” – aktywne reguły typu programu i wariantu; „Uzgodnienie: nazwa” – reguła o tej nazwie
    /// (wiersz typu i wariantu przed innymi) z poziomem i tolerancją elementu; Missing – nazwy reguł, których nie ma w słowniku.
    /// </summary>
    public static (IReadOnlyList<RunReconciliationRow> Rules, IReadOnlyList<string> Missing) Reconciliations(RunStepDefinition step,
        IReadOnlyList<RunReconciliationRow> library, string projectType, bool isClosing)
    {
        var rules = new List<RunReconciliationRow>();
        var missing = new List<string>();
        var applicable = RunReconciliations.Applicable(library, projectType, isClosing);
        foreach (var element in step.Elements.Where(e => e.Active && RunDefinitions.IsReconciliation(e.Element)))
        {
            if (element.Element.Trim() == RunDefinitions.AllReconciliations)
            {
                rules.AddRange(applicable.Where(r => r.IsActive));
                continue;
            }
            var name = RunDefinitions.ReconciliationName(element.Element);
            var found = applicable.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? library.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (found is null)
                missing.Add(name);
            else
                rules.Add(found with
                {
                    IsActive = true,
                    Level = element.Level is { } level ? RunCheckRules.Levels[level switch { CheckLevel.Error => 0, CheckLevel.Warning => 1, _ => 2 }] : found.Level,
                    Tolerance = element.Threshold ?? found.Tolerance,
                });
        }
        return (rules.DistinctBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(), missing);
    }

    public static string Serialize(RunDefinition definition) => JsonSerializer.Serialize(definition);

    /// <summary>Definicja zapisana w uruchomieniu; null – brak zapisu albo zapis nieczytelny.</summary>
    public static RunDefinition? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<RunDefinition>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
