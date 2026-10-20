using System.Text.Json;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Modules.Runs.Services;

/// <summary>
/// Przebieg etapowy (docs/pipeline-fazy.md, rozdz. 1.1–1.2 i 4.1): bramki etapów, kolejny etap do wykonania, potwierdzenie,
/// ponowne wykonanie i stan uruchomienia z etapów; reguły walidacji etapu z jego elementów kontrolnych. Bez bazy – stan
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
    /// Reguły walidacji etapu: element etapu z nazwą reguły (słownik „Reguły walidacji”) – reguła, gdy element i reguła są aktywne;
    /// Missing – nazwy spoza słownika (w przebiegu – ostrzeżenie „niesprawdzona”).
    /// </summary>
    public static (IReadOnlyList<RunRule> Rules, IReadOnlyList<string> Missing) Rules(RunStepDefinition step, IReadOnlyList<RunRule> library)
    {
        var rules = new List<RunRule>();
        var missing = new List<string>();
        foreach (var element in step.Elements)
        {
            var rule = library.FirstOrDefault(r => string.Equals(r.Name, element.Element.Trim(), StringComparison.OrdinalIgnoreCase));
            if (rule is null)
                missing.Add(element.Element.Trim());
            else if (element.Active && rule.IsActive)
                rules.Add(rule);
        }
        return (rules, missing);
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
