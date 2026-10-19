using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Kafelek rodzaju kosztu: było, jest, zmiana, zmiana %.</summary>
public sealed record AcwpKindTotal(string Kind, decimal Before, decimal After)
{
    public decimal Delta => After - Before;

    public decimal? Percent => Before == 0 ? null : Math.Round(Delta / Math.Abs(Before) * 100, 1);
}

/// <summary>Liść drzewa zmian – linie źródłowe: element WBS, cost element, okres strony „było” i „jest” (null – strona bez okresu).</summary>
public sealed record AcwpLineKey(string WbsElement, string CostElement, (int Year, int Period)? Before, (int Year, int Period)? After);

/// <summary>
/// Węzeł drzewa zmian ACWP: poziom (Rodzaj, Cost element, Element WBS, Okres), etykieta, było / jest; Remainder – zbiorczy węzeł
/// „pozostałe” (pozycje poza pierwszymi N); Line – liść z liniami źródłowymi.
/// </summary>
public sealed record AcwpChangeNode(string Level, string Label, decimal Before, decimal After, IReadOnlyList<AcwpChangeNode> Children,
    bool Remainder = false, AcwpLineKey? Line = null)
{
    public decimal Delta => After - Before;
}

/// <summary>Wynik porównania: kafelki rodzajów i drzewo zmian (rodzaj → cost element → element WBS [→ okres]).</summary>
public sealed record AcwpComparison(IReadOnlyList<AcwpKindTotal> Totals, IReadOnlyList<AcwpChangeNode> Tree);

/// <summary>
/// Zmiany ACWP (docs/funkcjonalnosc.md, F06 – okno „Zmiany ACWP”): porównanie kosztu brutto (bez rozliczenia wychodzącego) po
/// rodzaju kosztu (RunCostKinds) w dwóch wariantach – dwa uruchomienia przebiegu (stan narastająco A i B; liść – okres) albo
/// okres do okresu w jednym uruchomieniu (koszt zaksięgowany w okresie A i B; liść – element WBS). Filtr: rodzaj, elementy WBS
/// (wiersz struktury z poddrzewem). Na każdym poziomie pierwsze top pozycji wg |zmiany| i węzeł „pozostałe”.
/// </summary>
public static class AcwpChanges
{
    public const string KindLevel = "Rodzaj";
    public const string CostElementLevel = "Cost element";
    public const string WbsLevel = "Element WBS";
    public const string PeriodLevel = "Okres";

    /// <summary>Domyślna liczba pozycji na poziomie drzewa.</summary>
    public const int DefaultTop = 50;

    /// <summary>Zmiana uznana za brak zmiany (zaokrąglenia).</summary>
    private const decimal Zero = 0.005m;

    private sealed record Cell(string Kind, string CostElement, string WbsElement, (int Year, int Period) Period, decimal Before, decimal After);

    /// <summary>Dwa uruchomienia: before / after – dane przypięcia (koszty w walucie przebiegu).</summary>
    public static AcwpComparison Runs(IReadOnlyList<RunBalanceRow> before, IReadOnlyList<RunBalanceRow> after, IReadOnlyDictionary<string, string>? kinds,
        IReadOnlyDictionary<string, string>? names, string? kind, IReadOnlySet<string>? wbsElements, bool onlyChanges, int top = DefaultTop)
    {
        var cells = Cells(before, after, kinds, kind, wbsElements, r => true, r => true, r => (r.FiscalYear, r.Period));
        return new(Totals(cells, kind), Tree(cells, names, onlyChanges, top, withPeriods: true, periodBefore: null, periodAfter: null));
    }

    /// <summary>Okres do okresu w jednym uruchomieniu: koszt zaksięgowany w okresie before i w okresie after (okresy 13–16 jak 12).</summary>
    public static AcwpComparison Periods(IReadOnlyList<RunBalanceRow> rows, (int Year, int Period) before, (int Year, int Period) after,
        IReadOnlyDictionary<string, string>? kinds, IReadOnlyDictionary<string, string>? names, string? kind, IReadOnlySet<string>? wbsElements, bool onlyChanges,
        int top = DefaultTop)
    {
        bool In(RunBalanceRow r, (int, int) period) => (r.FiscalYear, Math.Min(r.Period, 12)) == period;
        var cells = Cells(rows, rows, kinds, kind, wbsElements, r => In(r, before), r => In(r, after), _ => (0, 0));
        return new(Totals(cells, kind), Tree(cells, names, onlyChanges, top, withPeriods: false, periodBefore: before, periodAfter: after));
    }

    /// <summary>Okresy danych uruchomienia (od najnowszego; 13–16 jak 12) – wybór w wariancie okres do okresu.</summary>
    public static IReadOnlyList<(int Year, int Period)> PeriodsOf(IEnumerable<RunBalanceRow> rows) =>
        rows.Where(r => !r.IsSettlement).Select(r => (r.FiscalYear, Math.Min(r.Period, 12))).Distinct().OrderByDescending(p => p).ToList();

    private static List<Cell> Cells(IReadOnlyList<RunBalanceRow> before, IReadOnlyList<RunBalanceRow> after, IReadOnlyDictionary<string, string>? kinds,
        string? kind, IReadOnlySet<string>? wbsElements, Func<RunBalanceRow, bool> inBefore, Func<RunBalanceRow, bool> inAfter,
        Func<RunBalanceRow, (int, int)> period)
    {
        bool Keep(RunBalanceRow r) => !r.IsSettlement && (wbsElements is null || wbsElements.Contains(r.WbsElement))
                                      && (kind is null || RunCostKinds.Of(r, kinds) == kind);
        var map = new Dictionary<(string Kind, string Ce, string Wbs, (int, int) Period), (decimal Before, decimal After)>();
        foreach (var r in before.Where(r => Keep(r) && inBefore(r)))
        {
            var key = (RunCostKinds.Of(r, kinds), r.CostElement, r.WbsElement, period(r));
            map[key] = (map.GetValueOrDefault(key).Before + r.Amount, map.GetValueOrDefault(key).After);
        }
        foreach (var r in after.Where(r => Keep(r) && inAfter(r)))
        {
            var key = (RunCostKinds.Of(r, kinds), r.CostElement, r.WbsElement, period(r));
            map[key] = (map.GetValueOrDefault(key).Before, map.GetValueOrDefault(key).After + r.Amount);
        }
        return map.Select(x => new Cell(x.Key.Kind, x.Key.Ce, x.Key.Wbs, x.Key.Period, x.Value.Before, x.Value.After)).ToList();
    }

    private static IReadOnlyList<AcwpKindTotal> Totals(List<Cell> cells, string? kind) =>
        RunMeasures.Kinds.Where(k => kind is null || k == kind)
            .Select(k => new AcwpKindTotal(k, cells.Where(c => c.Kind == k).Sum(c => c.Before), cells.Where(c => c.Kind == k).Sum(c => c.After))).ToList();

    private static IReadOnlyList<AcwpChangeNode> Tree(List<Cell> cells, IReadOnlyDictionary<string, string>? names, bool onlyChanges, int top, bool withPeriods,
        (int, int)? periodBefore, (int, int)? periodAfter)
    {
        bool Changed(decimal before, decimal after) => !onlyChanges || Math.Abs(after - before) >= Zero;

        IReadOnlyList<AcwpChangeNode> Limit(IEnumerable<AcwpChangeNode> nodes, string level)
        {
            var ordered = nodes.Where(n => Changed(n.Before, n.After)).OrderByDescending(n => Math.Abs(n.Delta)).ThenBy(n => n.Label).ToList();
            if (ordered.Count <= top)
                return ordered;
            var rest = ordered.Skip(top).ToList();
            return [.. ordered.Take(top), new AcwpChangeNode(level, $"pozostałe: {rest.Count} pozycji", rest.Sum(n => n.Before), rest.Sum(n => n.After), [], Remainder: true)];
        }

        AcwpChangeNode Wbs(IGrouping<string, Cell> g) => withPeriods
            ? new(WbsLevel, Show(g.Key), g.Sum(c => c.Before), g.Sum(c => c.After),
                Limit(g.Select(c => new AcwpChangeNode(PeriodLevel, $"{c.Period.Year}/{c.Period.Period:00}", c.Before, c.After, [],
                    Line: new AcwpLineKey(c.WbsElement, c.CostElement, c.Period, c.Period))), PeriodLevel))
            : new(WbsLevel, Show(g.Key), g.Sum(c => c.Before), g.Sum(c => c.After), [],
                Line: new AcwpLineKey(g.Key, g.First().CostElement, periodBefore, periodAfter));

        AcwpChangeNode CostElement(IGrouping<string, Cell> g) =>
            new(CostElementLevel, names?.GetValueOrDefault(g.Key) is { Length: > 0 } name ? $"{g.Key} – {name}" : g.Key, g.Sum(c => c.Before), g.Sum(c => c.After),
                Limit(g.GroupBy(c => c.WbsElement).Select(Wbs), WbsLevel));

        return Limit(cells.GroupBy(c => c.Kind).Select(g => new AcwpChangeNode(KindLevel, g.Key, g.Sum(c => c.Before), g.Sum(c => c.After),
            Limit(g.GroupBy(c => c.CostElement).Select(CostElement), CostElementLevel))), KindLevel);
    }

    private static string Show(string wbs) => wbs.Length == 0 ? "(bez elementu WBS)" : wbs;
}
