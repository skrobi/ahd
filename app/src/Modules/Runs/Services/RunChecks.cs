using System.Globalization;
using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Wynik kontroli przebiegu.</summary>
public sealed record RunIssue(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount = null);

/// <summary>Dane kontroli przebiegu (opis – RunChecks.Check).</summary>
internal sealed record RunCheckContext(IReadOnlyList<RunBalanceRow> Current, IReadOnlyList<RunBalanceRow>? Previous,
    IReadOnlySet<string> CostElementsInDictionary, (int Year, int Period) ClosedBefore, (int Year, int Period) Today, string? Currency,
    IReadOnlyList<RunBalanceRow>? Source, Func<RunCheckRule, RunCheckSetting> Setting)
{
    public bool Closed(RunBalanceRow r) => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(ClosedBefore) < 0;
}

/// <summary>
/// Kontrole danych ACTUALS przebiegu (docs/pipeline-fazy.md, P2 – kontrole danych przebiegu): dane projektu w chwili
/// przypięcia (current) wobec poprzedniego przebiegu (previous; null – pierwszy przebieg). Kwoty w walucie pola kwoty ACTUALS projektu.
/// Okres zamknięty – rok i okres SAP przed closedBefore (okresy specjalne 13–16 liczone jak 12); today – okres bieżący
/// (późniejsze są przyszłe). Każda reguła katalogu (RunCheckRules) ma tu implementację po kodzie; ustawienia reguł – słownik
/// „Kontrole przebiegu” (Settings), brak – ustawienia domyślne.
/// </summary>
public static class RunChecks
{
    public const string Volume = RunCheckRules.Volume;
    public const string Deviations = RunCheckRules.Deviations;
    public const string NewCostElements = RunCheckRules.NewCostElements;
    public const string NetZero = RunCheckRules.NetZero;
    public const string ClosedPeriods = RunCheckRules.ClosedPeriods;
    public const string Quality = RunCheckRules.Quality;

    /// <summary>Tolerancja salda (zaokrąglenia) w walucie obiektu.</summary>
    public const decimal Tolerance = RunCheckRules.Tolerance;

    /// <summary>Liczba pozycji w zestawieniach (odchylenia, elementy z saldem).</summary>
    public const int Top = 10;

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Implementacje reguł katalogu (RunCheckRules.All) po kodzie reguły.</summary>
    internal static IReadOnlyDictionary<string, Func<RunCheckContext, RunCheckSetting, IEnumerable<RunIssue>>> Rules { get; } =
        new Dictionary<string, Func<RunCheckContext, RunCheckSetting, IEnumerable<RunIssue>>>
        {
            [RunCheckRules.RowsDecrease.Code] = RowsDecrease,
            [RunCheckRules.CostDecrease.Code] = CostDecrease,
            [RunCheckRules.TopDeviations.Code] = TopDeviations,
            [RunCheckRules.CostElementOutside.Code] = CostElementOutside,
            [RunCheckRules.CostElementFirst.Code] = CostElementFirst,
            [RunCheckRules.NetZeroBalance.Code] = NetZeroBalance,
            [RunCheckRules.PostedAfterSettlement.Code] = PostedAfterSettlement,
            [RunCheckRules.ClosedPeriodChanges.Code] = ClosedPeriodChanges,
            [RunCheckRules.NegativeCost.Code] = NegativeCost,
            [RunCheckRules.FuturePeriods.Code] = FuturePeriods,
            [RunCheckRules.OtherCurrency.Code] = OtherCurrency,
            [RunCheckRules.NoWbsElement.Code] = NoWbsElement,
        };

    /// <summary>Koszt brutto: wiersze bez rozliczenia wychodzącego (jak ACWP struktury).</summary>
    public static decimal GrossCost(IEnumerable<RunBalanceRow> rows) => rows.Where(r => !r.IsSettlement).Sum(r => r.Amount);

    /// <summary>
    /// Ustawienia reguł dla typu programu i wariantu przebiegu ze słownika „Kontrole przebiegu”: wiersz typu programu przed
    /// „Wszystkie”, wariant przebiegu (zamykający / w środku okresu) przed „Każdy”; reguła bez wiersza – ustawienie domyślne;
    /// pusty poziom albo próg wiersza – domyślny reguły. Wiersz nieznanej reguły jest pomijany.
    /// </summary>
    public static IReadOnlyDictionary<string, RunCheckSetting> Settings(IEnumerable<RunCheckRow> rows, string projectType, bool isClosing)
    {
        var kind = isClosing ? RunCheckRules.ClosingRun : RunCheckRules.MidPeriodRun;
        var matching = rows
            .Where(r => Is(r.ProjectType, projectType) || Is(r.ProjectType, RunCheckRules.AllTypes))
            .Where(r => Is(r.RunKind, kind) || Is(r.RunKind, RunCheckRules.AnyRun))
            .Select(r => (Rule: RunCheckRules.ByName(r.RuleName), Row: r))
            .Where(x => x.Rule is not null)
            .ToList();
        return RunCheckRules.All.ToDictionary(rule => rule.Code, rule =>
            matching.Where(x => x.Rule == rule)
                .OrderByDescending(x => Is(x.Row.ProjectType, projectType))
                .ThenByDescending(x => Is(x.Row.RunKind, kind))
                .Select(x => new RunCheckSetting(x.Row.IsActive, RunCheckRules.LevelOf(x.Row.Level) ?? rule.Level,
                    rule.Threshold is null ? null : x.Row.Threshold ?? rule.Threshold))
                .FirstOrDefault() ?? RunCheckRules.Default(rule));

        static bool Is(string? value, string expected) => string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Kontrole z ustawieniami reguł (settings – kod reguły → ustawienie; brak – domyślne); wyniki w kolejności katalogu.</summary>
    public static IReadOnlyList<RunIssue> Check(IReadOnlyList<RunBalanceRow> current, IReadOnlyList<RunBalanceRow>? previous,
        IReadOnlySet<string> costElementsInDictionary, (int Year, int Period) closedBefore, (int Year, int Period) today, string? currency = "PLN",
        IReadOnlyList<RunBalanceRow>? source = null, IReadOnlyDictionary<string, RunCheckSetting>? settings = null)
    {
        RunCheckSetting Setting(RunCheckRule rule) => settings?.GetValueOrDefault(rule.Code) ?? RunCheckRules.Default(rule);
        var context = new RunCheckContext(current, previous, costElementsInDictionary, closedBefore, today, currency, source, Setting);
        return RunCheckRules.All.Where(rule => Setting(rule).Active).SelectMany(rule => Rules[rule.Code](context, Setting(rule))).ToList();
    }

    // Liczba danych – zrzut jest narastający: mniej wierszy niż poprzednio to brak danych.
    private static IEnumerable<RunIssue> RowsDecrease(RunCheckContext c, RunCheckSetting s)
    {
        var rows = c.Current.Sum(r => (long)r.Rows);
        if (c.Previous is null)
        {
            yield return new RunIssue(Volume, CheckLevel.Pass, null, $"Pierwszy przebieg programu – brak danych do porównania ({rows:#,0} wierszy)");
            yield break;
        }
        var before = c.Previous.Sum(r => (long)r.Rows);
        if (rows < before)
            yield return new RunIssue(Volume, s.Level, null, $"Mniej wierszy danych niż w poprzednim przebiegu: {rows:#,0} < {before:#,0} – niepełny zrzut ACTUALS albo zmiana zakresu", rows - before);
    }

    // Liczba danych – koszt brutto mniejszy niż poprzednio.
    private static IEnumerable<RunIssue> CostDecrease(RunCheckContext c, RunCheckSetting s)
    {
        if (c.Previous is null)
            yield break;
        var cost = GrossCost(c.Current);
        var costBefore = GrossCost(c.Previous);
        if (cost < costBefore - s.Threshold!.Value)
            yield return new RunIssue(Volume, s.Level, null, $"Koszt brutto mniejszy niż w poprzednim przebiegu: {Amount(cost)} < {Amount(costBefore)}", cost - costBefore);
    }

    // Największe odchylenia raport do raportu – koszt brutto po elemencie WBS i po cost elemencie.
    private static IEnumerable<RunIssue> TopDeviations(RunCheckContext c, RunCheckSetting s)
    {
        if (c.Previous is null)
            yield break;
        foreach (var (label, key) in new (string, Func<RunBalanceRow, string>)[] { ("element WBS", r => r.WbsElement), ("cost element", r => r.CostElement) })
        {
            var now = c.Current.Where(r => !r.IsSettlement).GroupBy(key).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            var then = c.Previous.Where(r => !r.IsSettlement).GroupBy(key).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            foreach (var (element, delta) in now.Keys.Union(then.Keys)
                         .Select(k => (k, now.GetValueOrDefault(k) - then.GetValueOrDefault(k)))
                         .Where(x => Math.Abs(x.Item2) >= s.Threshold!.Value)
                         .OrderByDescending(x => Math.Abs(x.Item2)).Take(Top))
                yield return new RunIssue(Deviations, s.Level, element,
                    $"{label} {Show(element)}: zmiana kosztu {Amount(delta)} ({Amount(then.GetValueOrDefault(element))} → {Amount(now.GetValueOrDefault(element))})", delta);
        }
    }

    private static IEnumerable<IGrouping<string, RunBalanceRow>> CostElementGroups(RunCheckContext c) =>
        c.Current.Where(r => r.CostElement.Length > 0).GroupBy(r => r.CostElement, StringComparer.OrdinalIgnoreCase);

    // Nowe cost elementy – spoza słownika Cost Category.
    private static IEnumerable<RunIssue> CostElementOutside(RunCheckContext c, RunCheckSetting s) =>
        CostElementGroups(c).Where(g => !c.CostElementsInDictionary.Contains(g.Key))
            .Select(g => new RunIssue(NewCostElements, s.Level, g.Key,
                $"Cost element {g.Key} spoza słownika Cost Category – dodaj go (Słowniki → Cost Category → Uzupełnij z ACTUALS)", g.Sum(r => r.Amount)));

    // Nowe cost elementy – nowe w projekcie względem poprzedniego przebiegu (ze słownika; spoza słownika – reguła wyżej).
    private static IEnumerable<RunIssue> CostElementFirst(RunCheckContext c, RunCheckSetting s)
    {
        if (c.Previous is null)
            return [];
        var known = c.Previous.Select(r => r.CostElement).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return CostElementGroups(c).Where(g => c.CostElementsInDictionary.Contains(g.Key) && !known.Contains(g.Key))
            .Select(g => new RunIssue(NewCostElements, s.Level, g.Key, $"Cost element {g.Key} pierwszy raz w programie", g.Sum(r => r.Amount)));
    }

    // Dane źródłowe ACTUALS w okresach zamkniętych (projekt CAS – koszty bez narzutów z rozliczeniem), nie koszty po narzutach.
    private static List<RunBalanceRow> ClosedSource(RunCheckContext c) => (c.Source ?? c.Current).Where(c.Closed).ToList();

    // Projekt netuje się do 0 w zamkniętych okresach; inaczej – gdzie: elementy WBS z saldem narastającym ≠ 0.
    private static IEnumerable<RunIssue> NetZeroBalance(RunCheckContext c, RunCheckSetting s)
    {
        var closed = ClosedSource(c);
        var balance = closed.Sum(r => r.Amount);
        var tolerance = s.Threshold!.Value;
        if (Math.Abs(balance) <= tolerance)
        {
            yield return new RunIssue(NetZero, CheckLevel.Pass, null, $"Program netuje się do 0 w okresach zamkniętych (do {c.ClosedBefore.Year}/{c.ClosedBefore.Period:00} bez niego): saldo {Amount(balance)}", balance);
            yield break;
        }
        yield return new RunIssue(NetZero, s.Level, null, $"Program nie netuje się do 0 w okresach zamkniętych: saldo {Amount(balance)}", balance);
        foreach (var wbs in closed.GroupBy(r => r.WbsElement)
                     .Select(g => (Element: g.Key, Balance: g.Sum(r => r.Amount), Cost: g.Where(r => !r.IsSettlement).Sum(r => r.Amount),
                         Settled: g.Where(r => r.IsSettlement).Sum(r => r.Amount), Rows: g.ToList()))
                     .Where(x => Math.Abs(x.Balance) > tolerance)
                     .OrderByDescending(x => Math.Abs(x.Balance)).Take(Top))
        {
            var reason = wbs.Settled == 0 ? "brak rozliczenia (reguła rozliczenia, element statystyczny albo brak cost elementu „Rozliczeniowy” w słowniku)"
                : Math.Abs(wbs.Settled) > Math.Abs(wbs.Cost) ? "rozliczenie większe niż koszt (storno po rozliczeniu?)"
                : "rozliczenie niepełne";
            var last = wbs.Rows.Where(r => r.Amount != 0).MaxBy(r => (r.FiscalYear, r.Period));
            var lines = wbs.Rows.Where(r => !r.IsSettlement).GroupBy(r => r.CostElement)
                .Select(g => (g.Key, Sum: g.Sum(r => r.Amount))).Where(x => x.Sum != 0).OrderByDescending(x => Math.Abs(x.Sum)).Take(3)
                .Select(x => $"{x.Key} {Amount(x.Sum)}");
            yield return new RunIssue(NetZero, s.Level, wbs.Element,
                $"{Show(wbs.Element)}: saldo {Amount(wbs.Balance)} (koszt {Amount(wbs.Cost)}, rozliczenie {Amount(wbs.Settled)}) – {reason}; " +
                $"ostatni okres z ruchem {last?.FiscalYear}/{last?.Period:00}; główne cost elementy: {string.Join(", ", lines)}", wbs.Balance);
        }
    }

    // Księgowanie po rozliczeniu (gdy program nie netuje się do 0 – tolerancja reguły netowania): koszt utworzony później niż
    // ostatnie rozliczenie elementu w tym okresie.
    private static IEnumerable<RunIssue> PostedAfterSettlement(RunCheckContext c, RunCheckSetting s)
    {
        var closed = ClosedSource(c);
        var tolerance = c.Setting(RunCheckRules.NetZeroBalance).Threshold ?? Tolerance;
        if (Math.Abs(closed.Sum(r => r.Amount)) <= tolerance)
            return [];
        return closed.GroupBy(r => (r.WbsElement, r.FiscalYear, r.Period))
            .Select(g => (g.Key, Cost: g.Where(r => !r.IsSettlement).Max(r => r.LastCreatedOn), Settled: g.Where(r => r.IsSettlement).Max(r => r.LastCreatedOn)))
            .Where(x => x.Cost is not null && x.Settled is not null && x.Cost > x.Settled).Take(Top)
            .Select(late => new RunIssue(NetZero, s.Level, late.Key.WbsElement,
                $"{Show(late.Key.WbsElement)} {late.Key.FiscalYear}/{late.Key.Period:00}: koszt utworzony {late.Cost:yyyy-MM-dd}, po rozliczeniu z {late.Settled:yyyy-MM-dd} – wymaga ponownego rozliczenia"));
    }

    // Zmiany w zamkniętych okresach względem poprzedniego przebiegu (element WBS × rok × okres).
    private static IEnumerable<RunIssue> ClosedPeriodChanges(RunCheckContext c, RunCheckSetting s)
    {
        if (c.Previous is null)
            return [];
        var then = c.Previous.Where(c.Closed).GroupBy(r => (r.WbsElement, r.FiscalYear, r.Period)).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var now = c.Current.Where(c.Closed).GroupBy(r => (r.WbsElement, r.FiscalYear, r.Period)).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        return now.Keys.Union(then.Keys)
            .Select(k => (k, Delta: now.GetValueOrDefault(k) - then.GetValueOrDefault(k)))
            .Where(x => Math.Abs(x.Delta) > s.Threshold!.Value).OrderByDescending(x => Math.Abs(x.Delta)).Take(Top)
            .Select(change => new RunIssue(ClosedPeriods, s.Level, change.k.WbsElement,
                $"{Show(change.k.WbsElement)} {change.k.FiscalYear}/{change.k.Period:00}: saldo okresu zamkniętego zmieniło się o {Amount(change.Delta)} od poprzedniego przebiegu", change.Delta));
    }

    // Jakość danych: ujemny koszt elementu narastająco.
    private static IEnumerable<RunIssue> NegativeCost(RunCheckContext c, RunCheckSetting s) =>
        c.Current.Where(r => !r.IsSettlement).GroupBy(r => r.WbsElement)
            .Select(g => (g.Key, Sum: g.Sum(r => r.Amount))).Where(x => x.Sum < -s.Threshold!.Value).OrderBy(x => x.Sum).Take(Top)
            .Select(negative => new RunIssue(Quality, s.Level, negative.Key, $"{Show(negative.Key)}: ujemny koszt narastająco {Amount(negative.Sum)} (storno po rozliczeniu?)", negative.Sum));

    // Jakość danych: okresy przyszłe.
    private static IEnumerable<RunIssue> FuturePeriods(RunCheckContext c, RunCheckSetting s)
    {
        var future = c.Current.Where(r => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(c.Today) > 0).ToList();
        if (future.Count > 0)
            yield return new RunIssue(Quality, s.Level, null,
                $"Wiersze w okresach przyszłych: {future.Sum(r => r.Rows)} ({string.Join(", ", future.Select(r => $"{r.FiscalYear}/{r.Period:00}").Distinct().Take(5))})", future.Sum(r => r.Amount));
    }

    // Jakość danych: waluta pola kwoty – podana (PLN – waluta obiektu) albo przeważająca w danych (inne pola kwoty).
    private static IEnumerable<RunIssue> OtherCurrency(RunCheckContext c, RunCheckSetting s)
    {
        var expected = c.Currency ?? c.Current.Where(r => r.Currency.Length > 0).GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(r => r.Rows)).Select(g => g.Key).FirstOrDefault();
        var currencies = c.Current.Where(r => r.Currency.Length > 0 && !string.Equals(r.Currency, expected, StringComparison.OrdinalIgnoreCase)).ToList();
        if (currencies.Count > 0)
            yield return new RunIssue(Quality, s.Level, null,
                $"Waluta inna niż {expected}: {string.Join(", ", currencies.Select(r => r.Currency).Distinct())} ({currencies.Sum(r => r.Rows)} wierszy) – saldo w różnych walutach");
    }

    // Jakość danych: wiersze bez elementu WBS.
    private static IEnumerable<RunIssue> NoWbsElement(RunCheckContext c, RunCheckSetting s)
    {
        var noWbs = c.Current.Where(r => r.WbsElement.Length == 0).ToList();
        if (noWbs.Count > 0)
            yield return new RunIssue(Quality, s.Level, null, $"Wiersze bez elementu WBS: {noWbs.Sum(r => r.Rows)} (sieci, zlecenia przypisane do programu)", noWbs.Sum(r => r.Amount));
    }

    private static string Show(string element) => element.Length == 0 ? "(bez elementu WBS)" : element;

    private static string Amount(decimal value) => PolishNumber.ToDisplay(Math.Round(value, 2));
}
