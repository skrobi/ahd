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

/// <summary>Przyrost rodzaju w okresie: koszt zaksięgowany w okresie, średni przyrost z poprzednich okresów, skok (koszt &gt; próg × średnia).</summary>
public sealed record AcwpIncrement(int Year, int Period, decimal Amount, decimal Average, bool IsJump)
{
    public string Label => $"{Year}/{Period:00}";
}

/// <summary>Seria przyrostów rodzaju kosztu (wykres w oknie „Zmiany ACWP”).</summary>
public sealed record AcwpIncrementSeries(string Kind, IReadOnlyList<AcwpIncrement> Increments);

/// <summary>Wiersz eksportu drzewa zmian: ścieżka liścia i kwoty.</summary>
public sealed record AcwpChangeExportRow(string Kind, string CostElement, string WbsElement, string Period, decimal Before, decimal After)
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
        IReadOnlyDictionary<string, string>? names, string? kind, IReadOnlySet<string>? wbsElements, bool onlyChanges, int top = DefaultTop, string? costElement = null)
    {
        var cells = Cells(before, after, kinds, kind, wbsElements, costElement, r => true, r => true, r => (r.FiscalYear, r.Period));
        return new(Totals(cells, kind), Tree(cells, names, onlyChanges, top, withPeriods: true, periodBefore: null, periodAfter: null));
    }

    /// <summary>Okres do okresu w jednym uruchomieniu: koszt zaksięgowany w okresie before i w okresie after (okresy 13–16 jak 12).</summary>
    public static AcwpComparison Periods(IReadOnlyList<RunBalanceRow> rows, (int Year, int Period) before, (int Year, int Period) after,
        IReadOnlyDictionary<string, string>? kinds, IReadOnlyDictionary<string, string>? names, string? kind, IReadOnlySet<string>? wbsElements, bool onlyChanges,
        int top = DefaultTop, string? costElement = null)
    {
        bool In(RunBalanceRow r, (int, int) period) => (r.FiscalYear, Math.Min(r.Period, 12)) == period;
        var cells = Cells(rows, rows, kinds, kind, wbsElements, costElement, r => In(r, before), r => In(r, after), _ => (0, 0));
        return new(Totals(cells, kind), Tree(cells, names, onlyChanges, top, withPeriods: false, periodBefore: before, periodAfter: after));
    }

    /// <summary>Okresy danych uruchomienia (od najnowszego; 13–16 jak 12) – wybór w wariancie okres do okresu.</summary>
    public static IReadOnlyList<(int Year, int Period)> PeriodsOf(IEnumerable<RunBalanceRow> rows) =>
        rows.Where(r => !r.IsSettlement).Select(r => (r.FiscalYear, Math.Min(r.Period, 12))).Distinct().OrderByDescending(p => p).ToList();

    /// <summary>
    /// Przyrosty miesięczne rodzajów (koszt brutto zaksięgowany w okresie) w count okresach do ostatniego okresu danych; średnia –
    /// z averagePeriods okresów przed okresem (bez kosztu – 0), skok – koszt &gt; factor × średnia przy średniej &gt; 0 (reguła „Skok przyrostu
    /// kosztu”). Filtry jak w porównaniu.
    /// </summary>
    public static IReadOnlyList<AcwpIncrementSeries> Increments(IReadOnlyList<RunBalanceRow> rows, IReadOnlyDictionary<string, string>? kinds, string? kind,
        IReadOnlySet<string>? wbsElements, string? costElement, decimal factor, int count = 12, int averagePeriods = RunCheckRules.IncrementPeriods)
    {
        var data = rows.Where(r => !r.IsSettlement && (wbsElements is null || wbsElements.Contains(r.WbsElement))
                                   && (costElement is null || string.Equals(r.CostElement, costElement, StringComparison.OrdinalIgnoreCase))).ToList();
        if (data.Count == 0)
            return [];
        var last = data.Max(r => (r.FiscalYear, Math.Min(r.Period, 12)));
        // count okresów kończących się ostatnim okresem danych, od najstarszego.
        var next = last.Item2 == 12 ? (last.Item1 + 1, 1) : (last.Item1, last.Item2 + 1);
        var periods = RunCostKinds.Before(next, count).Reverse().ToList();
        return RunMeasures.Kinds.Where(k => kind is null || k == kind).Select(k =>
        {
            var byPeriod = data.Where(r => RunCostKinds.Of(r, kinds) == k).GroupBy(r => (r.FiscalYear, Math.Min(r.Period, 12)))
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            return new AcwpIncrementSeries(k, periods.Select(p =>
            {
                var amount = byPeriod.GetValueOrDefault(p);
                var average = RunCostKinds.Before(p, averagePeriods).Sum(b => byPeriod.GetValueOrDefault(b)) / averagePeriods;
                return new AcwpIncrement(p.Year, p.Period, amount, average, average > 0 && amount > factor * average);
            }).ToList());
        }).ToList();
    }

    /// <summary>Liście drzewa (bez węzłów „pozostałe”) ze ścieżką – eksport do Excela; drzewo budowane bez limitu pozycji.</summary>
    public static IReadOnlyList<AcwpChangeExportRow> Flatten(IReadOnlyList<AcwpChangeNode> tree)
    {
        var rows = new List<AcwpChangeExportRow>();
        void Walk(AcwpChangeNode node, string kind, string ce, string wbs)
        {
            if (node.Remainder)
                return;
            var (k, c, w) = node.Level switch
            {
                KindLevel => (node.Label, ce, wbs),
                CostElementLevel => (kind, node.Label, wbs),
                WbsLevel => (kind, ce, node.Label),
                _ => (kind, ce, wbs),
            };
            if (node.Children.Count == 0 && node.Level is WbsLevel or PeriodLevel)
                rows.Add(new AcwpChangeExportRow(k, c, w, node.Level == PeriodLevel ? node.Label : "", node.Before, node.After));
            foreach (var child in node.Children)
                Walk(child, k, c, w);
        }
        foreach (var node in tree)
            Walk(node, "", "", "");
        return rows;
    }

    private static List<Cell> Cells(IReadOnlyList<RunBalanceRow> before, IReadOnlyList<RunBalanceRow> after, IReadOnlyDictionary<string, string>? kinds,
        string? kind, IReadOnlySet<string>? wbsElements, string? costElement, Func<RunBalanceRow, bool> inBefore, Func<RunBalanceRow, bool> inAfter,
        Func<RunBalanceRow, (int, int)> period)
    {
        bool Keep(RunBalanceRow r) => !r.IsSettlement && (wbsElements is null || wbsElements.Contains(r.WbsElement))
                                      && (costElement is null || string.Equals(r.CostElement, costElement, StringComparison.OrdinalIgnoreCase))
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
