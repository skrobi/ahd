using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Kafelek rodzaju kosztu: było, jest, zmiana (Trend – znak zmiany: kolor kafelka), zmiana % (eksport).</summary>
public sealed record AcwpKindTotal(string Kind, decimal Before, decimal After)
{
    public decimal Delta => After - Before;

    public int Trend => Math.Abs(Delta) < 0.005m ? 0 : Math.Sign(Delta);

    public decimal? Percent => Before == 0 ? null : Math.Round(Delta / Math.Abs(Before) * 100, 1);
}

/// <summary>Komórka drzewa zmian – linie źródłowe: element WBS, cost element, okres strony „było” i „jest” (null – strona bez okresu).</summary>
public sealed record AcwpLineKey(string WbsElement, string CostElement, (int Year, int Period)? Before, (int Year, int Period)? After);

/// <summary>
/// Węzeł drzewa zmian ACWP: poziom (Rodzaj, Cost element, Element WBS, Okres), etykieta, było / jest; Remainder – zbiorczy węzeł
/// „pozostałe” (pozycje poza pierwszymi N); Cells – wszystkie komórki węzła (także ukryte w „pozostałe” i bez zmian) – linie źródłowe węzła.
/// </summary>
public sealed record AcwpChangeNode(string Level, string Label, decimal Before, decimal After, IReadOnlyList<AcwpChangeNode> Children,
    IReadOnlyList<AcwpLineKey> Cells, bool Remainder = false)
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

/// <summary>Wykres przyrostów (osobne okno): przyrosty rodzajów w okresach i skumulowany koszt rodzajów wykresu na koniec każdego okresu.</summary>
public sealed record AcwpChart(IReadOnlyList<AcwpIncrementSeries> Series, IReadOnlyList<decimal> Cumulative);

/// <summary>Stan linii źródłowej w porównaniu „było” / „jest”.</summary>
public static class AcwpLineStates
{
    public const string Unchanged = "bez zmian";
    public const string Changed = "zmieniona";
    public const string New = "nowa";
    public const string Removed = "usunięta";
}

/// <summary>
/// Zapytanie linii ACTUALS jednej strony węzła: elementy WBS i cost elementy komórek (null – więcej niż lista parametrów, zawężenie po
/// wczytaniu), rok i okres wspólne dla komórek (null – różne; okres 12 obejmuje 13–16); Cells – komórki strony do zawężenia linii.
/// </summary>
public sealed record AcwpLineScope(IReadOnlyList<string>? WbsElements, IReadOnlyList<string>? CostElements, int? Year, int? Period,
    IReadOnlySet<(string Wbs, string CostElement, int Year, int Period)> Cells, bool FoldClosing);

/// <summary>Linia źródłowa w porównaniu: stan (AcwpLineStates), linia strony „było” i „jest” (null – brak linii po tej stronie).</summary>
public sealed record AcwpLineChange(string State, RunActualsLine? Before, RunActualsLine? After)
{
    /// <summary>Linia do opisu (pola klucza są równe po obu stronach): „jest”, a dla usuniętej – „było”.</summary>
    public RunActualsLine Line => After ?? Before!;

    public string Period => Line.FiscalYear is { } year ? $"{year}/{Line.Period:00}" : "";

    public decimal Delta => (After?.Amount ?? 0) - (Before?.Amount ?? 0);
}

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

    /// <summary>Liczba okresów przed okresem, z których liczony jest średni przyrost (jak reguła startowa „Skok przyrostu kosztu”).</summary>
    public const int IncrementPeriods = 6;

    /// <summary>Skok przyrostu: koszt okresu &gt; JumpFactor × średni przyrost (jak reguła startowa „Skok przyrostu kosztu”).</summary>
    public const decimal JumpFactor = 3m;

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
        IReadOnlySet<string>? wbsElements, string? costElement, decimal factor, int count = 12, int averagePeriods = IncrementPeriods)
    {
        var data = rows.Where(r => !r.IsSettlement && (wbsElements is null || wbsElements.Contains(r.WbsElement))
                                   && (costElement is null || string.Equals(r.CostElement, costElement, StringComparison.OrdinalIgnoreCase))).ToList();
        if (data.Count == 0)
            return [];
        var last = data.Max(r => (r.FiscalYear, Math.Min(r.Period, 12)));
        // count okresów kończących się ostatnim okresem danych, od najstarszego.
        var next = last.Item2 == 12 ? (last.Item1 + 1, 1) : (last.Item1, last.Item2 + 1);
        var periods = RunCostKinds.Before(next, count).Reverse().ToList();
        return RunCostKinds.All.Where(k => kind is null || k == kind).Select(k =>
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

    /// <summary>
    /// Wykres przyrostów: przyrosty rodzajów (Increments) i skumulowany koszt brutto rodzajów wykresu (filtr rodzaju; bez filtra – wszystkie
    /// rodzaje razem) zaksięgowany do końca każdego okresu wykresu, łącznie z kosztem sprzed pierwszego okresu wykresu.
    /// </summary>
    public static AcwpChart Chart(IReadOnlyList<RunBalanceRow> rows, IReadOnlyDictionary<string, string>? kinds, string? kind, IReadOnlySet<string>? wbsElements,
        string? costElement, decimal factor)
    {
        var series = Increments(rows, kinds, kind, wbsElements, costElement, factor);
        if (series.Count == 0)
            return new([], []);
        var byPeriod = rows.Where(r => !r.IsSettlement && (wbsElements is null || wbsElements.Contains(r.WbsElement))
                                       && (costElement is null || string.Equals(r.CostElement, costElement, StringComparison.OrdinalIgnoreCase))
                                       && (kind is null || RunCostKinds.Of(r, kinds) == kind))
            .GroupBy(r => (r.FiscalYear, Math.Min(r.Period, 12))).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        return new(series, series[0].Increments.Select(i => byPeriod.Where(p => p.Key.CompareTo((i.Year, i.Period)) <= 0).Sum(p => p.Value)).ToList());
    }

    /// <summary>
    /// Zapytanie linii strony węzła (before – strona „było”): komórki z okresem tej strony; foldClosing – okresy 13–16 jak 12 (okres do okresu),
    /// inaczej okres linii jak okres komórki. Null – brak komórek strony. Listy do listLimit pozycji (limit parametrów zapytania).
    /// </summary>
    public static AcwpLineScope? LineScope(IReadOnlyList<AcwpLineKey> cells, bool before, bool foldClosing, int listLimit = 900)
    {
        var side = cells.Select(c => (c.WbsElement, c.CostElement, Period: before ? c.Before : c.After)).Where(c => c.Period is not null)
            .Select(c => (c.WbsElement, c.CostElement, c.Period!.Value.Year, c.Period.Value.Period)).Distinct().ToList();
        if (side.Count == 0)
            return null;
        var wbs = side.Select(c => c.WbsElement).Distinct().ToList();
        var costElements = side.Select(c => c.CostElement).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var periods = side.Select(c => (c.Year, c.Period)).Distinct().ToList();
        int? year = periods.Select(p => p.Year).Distinct().Count() == 1 ? periods[0].Year : null;
        // Okres w zapytaniu obejmuje 13–16 przy 12 – bez łączenia okresów tylko okresy 1–11.
        int? period = periods.Count == 1 && (foldClosing || periods[0].Period < 12) ? periods[0].Period : null;
        return new(wbs.Count <= listLimit ? wbs : null, costElements.Count <= listLimit ? costElements : null, year, period,
            side.Select(c => (c.WbsElement.ToUpperInvariant(), c.CostElement.ToUpperInvariant(), c.Year, c.Period)).ToHashSet(), foldClosing);
    }

    /// <summary>Linie zapytania należące do komórek strony (element WBS × cost element × okres).</summary>
    public static IReadOnlyList<RunActualsLine> InScope(IEnumerable<RunActualsLine> lines, AcwpLineScope scope) =>
        lines.Where(l => l.FiscalYear is { } year && l.Period is { } period
                         && scope.Cells.Contains(((l.WbsElement ?? "").ToUpperInvariant(), (l.CostElement ?? "").ToUpperInvariant(), year, scope.FoldClosing ? Math.Min(period, 12) : period)))
            .ToList();

    /// <summary>
    /// Linie źródłowe węzła w jednym wierszu na linię: linia „było” i „jest” to ta sama linia, gdy są równe pola klucza (WBS Element,
    /// Cost Element, CO object name, Transaction Currency, Value TranCurr, Partner object, Original material, Purchase order number,
    /// Created on); powtórzenia klucza łączone po kolei (plik, wiersz). Stan: bez zmian, zmieniona (kwota pola kwoty uruchomienia
    /// albo ilość), nowa (tylko „jest”), usunięta (tylko „było”). Kolejność: zmiany od największej |zmiany kwoty|, potem bez zmian;
    /// onlyChanges – bez linii bez zmian.
    /// </summary>
    public static IReadOnlyList<AcwpLineChange> Lines(IReadOnlyList<RunActualsLine> before, IReadOnlyList<RunActualsLine> after, bool onlyChanges)
    {
        var pending = before.OrderBy(l => l.FileName, StringComparer.Ordinal).ThenBy(l => l.RowNumber).GroupBy(LineKey)
            .ToDictionary(g => g.Key, g => new Queue<RunActualsLine>(g));
        var result = new List<AcwpLineChange>();
        foreach (var line in after.OrderBy(l => l.FileName, StringComparer.Ordinal).ThenBy(l => l.RowNumber))
        {
            if (pending.TryGetValue(LineKey(line), out var queue) && queue.TryDequeue(out var old))
                result.Add(new(Same(old.Amount, line.Amount) && Same(old.Quantity, line.Quantity) ? AcwpLineStates.Unchanged : AcwpLineStates.Changed, old, line));
            else
                result.Add(new(AcwpLineStates.New, null, line));
        }
        result.AddRange(pending.Values.SelectMany(q => q).Select(l => new AcwpLineChange(AcwpLineStates.Removed, l, null)));
        return result.Where(c => !onlyChanges || c.State != AcwpLineStates.Unchanged)
            .OrderBy(c => c.State == AcwpLineStates.Unchanged).ThenByDescending(c => Math.Abs(c.Delta)).ThenByDescending(c => Math.Abs(c.Line.Amount ?? 0))
            .ThenBy(c => c.Line.FileName, StringComparer.Ordinal).ThenBy(c => c.Line.RowNumber).ToList();
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
        RunCostKinds.All.Where(k => kind is null || k == kind)
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
            return [.. ordered.Take(top), new AcwpChangeNode(level, $"pozostałe: {rest.Count} pozycji", rest.Sum(n => n.Before), rest.Sum(n => n.After), [],
                rest.SelectMany(n => n.Cells).ToList(), Remainder: true)];
        }

        // Komórka – linie źródłowe: w wariancie dwa uruchomienia okres komórki po obu stronach, w okres do okresu – okresy porównania.
        AcwpLineKey Key(Cell c) => withPeriods ? new(c.WbsElement, c.CostElement, c.Period, c.Period) : new(c.WbsElement, c.CostElement, periodBefore, periodAfter);
        IReadOnlyList<AcwpLineKey> Keys(IEnumerable<Cell> group) => group.Select(Key).ToList();

        AcwpChangeNode Wbs(IGrouping<string, Cell> g) => withPeriods
            ? new(WbsLevel, Show(g.Key), g.Sum(c => c.Before), g.Sum(c => c.After),
                Limit(g.Select(c => new AcwpChangeNode(PeriodLevel, $"{c.Period.Year}/{c.Period.Period:00}", c.Before, c.After, [], [Key(c)])), PeriodLevel), Keys(g))
            : new(WbsLevel, Show(g.Key), g.Sum(c => c.Before), g.Sum(c => c.After), [], Keys(g));

        AcwpChangeNode CostElement(IGrouping<string, Cell> g) =>
            new(CostElementLevel, names?.GetValueOrDefault(g.Key) is { Length: > 0 } name ? $"{g.Key} – {name}" : g.Key, g.Sum(c => c.Before), g.Sum(c => c.After),
                Limit(g.GroupBy(c => c.WbsElement).Select(Wbs), WbsLevel), Keys(g));

        return Limit(cells.GroupBy(c => c.Kind).Select(g => new AcwpChangeNode(KindLevel, g.Key, g.Sum(c => c.Before), g.Sum(c => c.After),
            Limit(g.GroupBy(c => c.CostElement).Select(CostElement), CostElementLevel), Keys(g))), KindLevel);
    }

    private static (string?, string?, string?, string?, decimal?, string?, string?, string?, string?) LineKey(RunActualsLine l) =>
        (Norm(l.WbsElement), Norm(l.CostElement), Norm(l.CoObjectName), Norm(l.TransactionCurrency), l.ValueTranCurr, Norm(l.PartnerObject),
            Norm(l.OriginalMaterial), Norm(l.PurchaseOrderNumber), Norm(l.CreatedOn));

    private static string? Norm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static bool Same(decimal? a, decimal? b) => Math.Abs((a ?? 0) - (b ?? 0)) < Zero;

    private static string Show(string wbs) => wbs.Length == 0 ? "(bez elementu WBS)" : wbs;
}
