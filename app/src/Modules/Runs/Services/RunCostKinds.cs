using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Runs.Services;

/// <summary>
/// Rodzaje kosztu w danych przebiegu (docs/pipeline-fazy.md, rozdz. 4.1): rodzaj cost elementu z Cost Category efektywnego
/// (Hours / Materials, inny albo brak – Other, jak ACWP Hours / Materials / Other struktury); koszt brutto rodzaju narastająco
/// i po okresach (okresy specjalne 13–16 jak 12).
/// </summary>
public static class RunCostKinds
{
    /// <summary>Rodzaje kosztu w kolejności (jak ACWP Hours / Materials / Other struktury).</summary>
    public static IReadOnlyList<string> All { get; } = [GlobalDictionaries.LaborKind, GlobalDictionaries.MaterialKind, GlobalDictionaries.OtherKind];

    public static string Of(RunBalanceRow row, IReadOnlyDictionary<string, string>? kinds) =>
        kinds?.GetValueOrDefault(row.CostElement) is { } kind && (kind == GlobalDictionaries.LaborKind || kind == GlobalDictionaries.MaterialKind)
            ? kind : GlobalDictionaries.OtherKind;

    /// <summary>Koszt brutto (bez rozliczenia wychodzącego) każdego rodzaju narastająco.</summary>
    public static IReadOnlyDictionary<string, decimal> Totals(IEnumerable<RunBalanceRow> rows, IReadOnlyDictionary<string, string>? kinds)
    {
        var totals = All.ToDictionary(k => k, _ => 0m);
        foreach (var row in rows.Where(r => !r.IsSettlement))
            totals[Of(row, kinds)] += row.Amount;
        return totals;
    }

    /// <summary>count okresów przed today, od najbliższego.</summary>
    public static IEnumerable<(int Year, int Period)> Before((int Year, int Period) today, int count)
    {
        var (year, period) = today;
        for (var i = 0; i < count; i++)
        {
            (year, period) = period == 1 ? (year - 1, 12) : (year, period - 1);
            yield return (year, period);
        }
    }
}
