namespace PzlEv.Shared.Models.Pipeline;

/// <summary>Wynik bramki albo akcji etapu: nowy status i kontrole.</summary>
public sealed record StageResult(StageStatus Status, IReadOnlyList<CheckResult> Checks);
