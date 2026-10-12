using System.Globalization;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Wynik kontroli przebiegu.</summary>
public sealed record RunIssue(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount = null);

/// <summary>
/// Kontrole danych ACTUALS przebiegu (docs/pipeline-fazy.md, P2 – kontrole danych przebiegu): dane projektu w chwili
/// przypięcia (current) wobec poprzedniego przebiegu (previous; null – pierwszy przebieg). Kwoty w walucie pola kwoty ACTUALS projektu.
/// Okres zamknięty – rok i okres SAP przed closedBefore (okresy specjalne 13–16 liczone jak 12); today – okres bieżący
/// (późniejsze są przyszłe).
/// </summary>
public static class RunChecks
{
    public const string Volume = "Liczba danych";
    public const string Deviations = "Odchylenia raport do raportu";
    public const string NewCostElements = "Nowe cost elementy";
    public const string NetZero = "Netowanie projektu do 0";
    public const string ClosedPeriods = "Zmiany w zamkniętych okresach";
    public const string Quality = "Jakość danych";

    /// <summary>Tolerancja salda (zaokrąglenia) w walucie obiektu.</summary>
    public const decimal Tolerance = 1m;

    /// <summary>Liczba pozycji w zestawieniach (odchylenia, elementy z saldem).</summary>
    public const int Top = 10;

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Koszt brutto: wiersze bez rozliczenia wychodzącego (jak ACWP struktury).</summary>
    public static decimal GrossCost(IEnumerable<RunBalanceRow> rows) => rows.Where(r => !r.IsSettlement).Sum(r => r.Amount);

    public static IReadOnlyList<RunIssue> Check(IReadOnlyList<RunBalanceRow> current, IReadOnlyList<RunBalanceRow>? previous,
        IReadOnlySet<string> costElementsInDictionary, (int Year, int Period) closedBefore, (int Year, int Period) today, string? currency = "PLN")
    {
        var issues = new List<RunIssue>();
        bool Closed(RunBalanceRow r) => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(closedBefore) < 0;

        // 1. Liczba danych – zrzut jest narastający: mniej wierszy niż poprzednio to brak danych; koszt brutto – ostrzeżenie.
        var rows = current.Sum(r => (long)r.Rows);
        if (previous is not null)
        {
            var before = previous.Sum(r => (long)r.Rows);
            if (rows < before)
                issues.Add(new RunIssue(Volume, CheckLevel.Error, null, $"Mniej wierszy danych niż w poprzednim przebiegu: {rows:#,0} < {before:#,0} – niepełny zrzut ACTUALS albo zmiana zakresu", rows - before));
            var cost = GrossCost(current);
            var costBefore = GrossCost(previous);
            if (cost < costBefore - Tolerance)
                issues.Add(new RunIssue(Volume, CheckLevel.Warning, null, $"Koszt brutto mniejszy niż w poprzednim przebiegu: {Amount(cost)} < {Amount(costBefore)}", cost - costBefore));
        }
        else
            issues.Add(new RunIssue(Volume, CheckLevel.Pass, null, $"Pierwszy przebieg projektu – brak danych do porównania ({rows:#,0} wierszy)"));

        // 2. Największe odchylenia raport do raportu – koszt brutto po elemencie WBS i po cost elemencie.
        if (previous is not null)
        {
            foreach (var (label, key) in new (string, Func<RunBalanceRow, string>)[] { ("element WBS", r => r.WbsElement), ("cost element", r => r.CostElement) })
            {
                var now = current.Where(r => !r.IsSettlement).GroupBy(key).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
                var then = previous.Where(r => !r.IsSettlement).GroupBy(key).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
                foreach (var (element, delta) in now.Keys.Union(then.Keys)
                             .Select(k => (k, now.GetValueOrDefault(k) - then.GetValueOrDefault(k)))
                             .Where(x => Math.Abs(x.Item2) >= Tolerance)
                             .OrderByDescending(x => Math.Abs(x.Item2)).Take(Top))
                    issues.Add(new RunIssue(Deviations, CheckLevel.Pass, element,
                        $"{label} {Show(element)}: zmiana kosztu {Amount(delta)} ({Amount(then.GetValueOrDefault(element))} → {Amount(now.GetValueOrDefault(element))})", delta));
            }
        }

        // 3. Nowe cost elementy – spoza słownika Cost Category (ERROR) i nowe w projekcie względem poprzedniego przebiegu.
        var known = previous?.Select(r => r.CostElement).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in current.Where(r => r.CostElement.Length > 0).GroupBy(r => r.CostElement, StringComparer.OrdinalIgnoreCase))
        {
            if (!costElementsInDictionary.Contains(group.Key))
                issues.Add(new RunIssue(NewCostElements, CheckLevel.Error, group.Key,
                    $"Cost element {group.Key} spoza słownika Cost Category – dodaj go (Słowniki → Cost Category → Uzupełnij z ACTUALS)", group.Sum(r => r.Amount)));
            else if (known is not null && !known.Contains(group.Key))
                issues.Add(new RunIssue(NewCostElements, CheckLevel.Pass, group.Key, $"Cost element {group.Key} pierwszy raz w projekcie", group.Sum(r => r.Amount)));
        }

        // 4. Projekt netuje się do 0 w zamkniętych okresach; inaczej – gdzie: elementy WBS z saldem narastającym ≠ 0.
        var closed = current.Where(Closed).ToList();
        var balance = closed.Sum(r => r.Amount);
        if (Math.Abs(balance) <= Tolerance)
            issues.Add(new RunIssue(NetZero, CheckLevel.Pass, null, $"Projekt netuje się do 0 w okresach zamkniętych (do {closedBefore.Year}/{closedBefore.Period:00} bez niego): saldo {Amount(balance)}", balance));
        else
        {
            issues.Add(new RunIssue(NetZero, CheckLevel.Error, null, $"Projekt nie netuje się do 0 w okresach zamkniętych: saldo {Amount(balance)}", balance));
            foreach (var wbs in closed.GroupBy(r => r.WbsElement)
                         .Select(g => (Element: g.Key, Balance: g.Sum(r => r.Amount), Cost: g.Where(r => !r.IsSettlement).Sum(r => r.Amount),
                             Settled: g.Where(r => r.IsSettlement).Sum(r => r.Amount), Rows: g.ToList()))
                         .Where(x => Math.Abs(x.Balance) > Tolerance)
                         .OrderByDescending(x => Math.Abs(x.Balance)).Take(Top))
            {
                var reason = wbs.Settled == 0 ? "brak rozliczenia (reguła rozliczenia, element statystyczny albo brak cost elementu „Rozliczeniowy” w słowniku)"
                    : Math.Abs(wbs.Settled) > Math.Abs(wbs.Cost) ? "rozliczenie większe niż koszt (storno po rozliczeniu?)"
                    : "rozliczenie niepełne";
                var last = wbs.Rows.Where(r => r.Amount != 0).MaxBy(r => (r.FiscalYear, r.Period));
                var lines = wbs.Rows.Where(r => !r.IsSettlement).GroupBy(r => r.CostElement)
                    .Select(g => (g.Key, Sum: g.Sum(r => r.Amount))).Where(x => x.Sum != 0).OrderByDescending(x => Math.Abs(x.Sum)).Take(3)
                    .Select(x => $"{x.Key} {Amount(x.Sum)}");
                issues.Add(new RunIssue(NetZero, CheckLevel.Error, wbs.Element,
                    $"{Show(wbs.Element)}: saldo {Amount(wbs.Balance)} (koszt {Amount(wbs.Cost)}, rozliczenie {Amount(wbs.Settled)}) – {reason}; " +
                    $"ostatni okres z ruchem {last?.FiscalYear}/{last?.Period:00}; główne cost elementy: {string.Join(", ", lines)}", wbs.Balance));
            }
            // Księgowanie po rozliczeniu: koszt utworzony później niż ostatnie rozliczenie elementu w tym okresie.
            foreach (var late in closed.GroupBy(r => (r.WbsElement, r.FiscalYear, r.Period))
                         .Select(g => (g.Key, Cost: g.Where(r => !r.IsSettlement).Max(r => r.LastCreatedOn), Settled: g.Where(r => r.IsSettlement).Max(r => r.LastCreatedOn)))
                         .Where(x => x.Cost is not null && x.Settled is not null && x.Cost > x.Settled).Take(Top))
                issues.Add(new RunIssue(NetZero, CheckLevel.Warning, late.Key.WbsElement,
                    $"{Show(late.Key.WbsElement)} {late.Key.FiscalYear}/{late.Key.Period:00}: koszt utworzony {late.Cost:yyyy-MM-dd}, po rozliczeniu z {late.Settled:yyyy-MM-dd} – wymaga ponownego rozliczenia"));
        }

        // 5. Zmiany w zamkniętych okresach względem poprzedniego przebiegu (element WBS × rok × okres).
        if (previous is not null)
        {
            var then = previous.Where(Closed).GroupBy(r => (r.WbsElement, r.FiscalYear, r.Period)).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            var now = closed.GroupBy(r => (r.WbsElement, r.FiscalYear, r.Period)).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            foreach (var change in now.Keys.Union(then.Keys)
                         .Select(k => (k, Delta: now.GetValueOrDefault(k) - then.GetValueOrDefault(k)))
                         .Where(x => Math.Abs(x.Delta) > Tolerance).OrderByDescending(x => Math.Abs(x.Delta)).Take(Top))
                issues.Add(new RunIssue(ClosedPeriods, CheckLevel.Warning, change.k.WbsElement,
                    $"{Show(change.k.WbsElement)} {change.k.FiscalYear}/{change.k.Period:00}: saldo okresu zamkniętego zmieniło się o {Amount(change.Delta)} od poprzedniego przebiegu", change.Delta));
        }

        // 6. Jakość danych: ujemny koszt elementu, okresy przyszłe, waluta obiektu, wiersze bez elementu WBS.
        foreach (var negative in current.Where(r => !r.IsSettlement).GroupBy(r => r.WbsElement)
                     .Select(g => (g.Key, Sum: g.Sum(r => r.Amount))).Where(x => x.Sum < -Tolerance).OrderBy(x => x.Sum).Take(Top))
            issues.Add(new RunIssue(Quality, CheckLevel.Warning, negative.Key, $"{Show(negative.Key)}: ujemny koszt narastająco {Amount(negative.Sum)} (storno po rozliczeniu?)", negative.Sum));
        var future = current.Where(r => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(today) > 0).ToList();
        if (future.Count > 0)
            issues.Add(new RunIssue(Quality, CheckLevel.Warning, null,
                $"Wiersze w okresach przyszłych: {future.Sum(r => r.Rows)} ({string.Join(", ", future.Select(r => $"{r.FiscalYear}/{r.Period:00}").Distinct().Take(5))})", future.Sum(r => r.Amount)));
        // waluta pola kwoty: podana (PLN – waluta obiektu) albo przeważająca w danych (inne pola kwoty)
        var expected = currency ?? current.Where(r => r.Currency.Length > 0).GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(r => r.Rows)).Select(g => g.Key).FirstOrDefault();
        var currencies = current.Where(r => r.Currency.Length > 0 && !string.Equals(r.Currency, expected, StringComparison.OrdinalIgnoreCase)).ToList();
        if (currencies.Count > 0)
            issues.Add(new RunIssue(Quality, CheckLevel.Warning, null,
                $"Waluta inna niż {expected}: {string.Join(", ", currencies.Select(r => r.Currency).Distinct())} ({currencies.Sum(r => r.Rows)} wierszy) – saldo w różnych walutach"));
        var noWbs = current.Where(r => r.WbsElement.Length == 0).ToList();
        if (noWbs.Count > 0)
            issues.Add(new RunIssue(Quality, CheckLevel.Warning, null, $"Wiersze bez elementu WBS: {noWbs.Sum(r => r.Rows)} (sieci, zlecenia przypisane do projektu)", noWbs.Sum(r => r.Amount)));
        return issues;
    }

    private static string Show(string element) => element.Length == 0 ? "(bez elementu WBS)" : element;

    private static string Amount(decimal value) => PolishNumber.ToDisplay(Math.Round(value, 2));
}
