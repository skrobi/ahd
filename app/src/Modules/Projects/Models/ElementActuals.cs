namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Koszty rzeczywiste po elemencie CES z ostatniego importu ACTUALS (REP_ProjectCostsByElement): Amounts – ACWP (pole kwoty
/// projektu), Hours – Actual DLH (Total Quantity), Labor / Material – ACWP według rodzaju kosztu (słownik Cost Category;
/// reszta ACWP – Other).
/// </summary>
public sealed record ElementActuals(
    IReadOnlyDictionary<string, decimal> Amounts,
    IReadOnlyDictionary<string, decimal> Hours,
    IReadOnlyDictionary<string, decimal> Labor,
    IReadOnlyDictionary<string, decimal> Material)
{
    public static ElementActuals Empty { get; } = new(new Dictionary<string, decimal>(), new Dictionary<string, decimal>(),
        new Dictionary<string, decimal>(), new Dictionary<string, decimal>());
}
