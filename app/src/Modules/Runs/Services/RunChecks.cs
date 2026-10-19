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
    (int Year, int Period) ClosedBefore, (int Year, int Period) Today, Func<RunCheckRule, RunCheckSetting> Setting, RunDictionaryData? Dictionaries = null)
{
    public bool Closed(RunBalanceRow r) => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(ClosedBefore) < 0;
}

/// <summary>
/// Kontrole wbudowane przebiegu – grupa „Walidacja EV” (docs/pipeline-fazy.md, rozdz. 4.1): dane programu jak w strukturze (current –
/// bez cost elementów rozliczeniowych, po Wykluczeniach, CAS po narzutach) wobec poprzedniego przebiegu (previous; null – pierwszy
/// przebieg) i słowniki programu. Okres zamknięty – rok i okres SAP przed closedBefore (okresy specjalne 13–16 liczone jak 12);
/// today – okres bieżący. Każda reguła katalogu (RunCheckRules) ma tu implementację po kodzie; ustawienia reguł – słownik „Kontrole
/// przebiegu” (Settings), brak – ustawienia domyślne. Reguły ACTUALS – formuły (RunValidationLanguage).
/// </summary>
public static class RunChecks
{
    public const string Volume = RunCheckRules.Volume;
    public const string Deviations = RunCheckRules.Deviations;
    public const string NewCostElements = RunCheckRules.NewCostElements;
    public const string NetZero = RunCheckRules.NetZero;
    public const string ClosedPeriods = RunCheckRules.ClosedPeriods;
    public const string Quality = RunCheckRules.Quality;
    public const string Dictionaries = RunCheckRules.Dictionaries;
    public const string CostKinds = RunCheckRules.CostKinds;

    /// <summary>Tolerancja salda (zaokrąglenia) w walucie obiektu.</summary>
    public const decimal Tolerance = RunCheckRules.Tolerance;

    /// <summary>Liczba pozycji w zestawieniach (odchylenia, elementy z saldem).</summary>
    public const int Top = 10;

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Implementacje reguł katalogu (RunCheckRules.All) po kodzie reguły.</summary>
    internal static IReadOnlyDictionary<string, Func<RunCheckContext, RunCheckSetting, IEnumerable<RunIssue>>> Rules { get; } =
        new Dictionary<string, Func<RunCheckContext, RunCheckSetting, IEnumerable<RunIssue>>>
        {
            [RunCheckRules.CostElementWithoutCategory.Code] = CostElementWithoutCategory,
            [RunCheckRules.CostCenterWithoutRate.Code] = CostCenterWithoutRate,
            [RunCheckRules.WpWithoutCostCenter.Code] = WpWithoutCostCenter,
            [RunCheckRules.WpWithoutBudget.Code] = WpWithoutBudget,
            [RunCheckRules.NewCostKind.Code] = NewCostKind,
            [RunCheckRules.CostKindJump.Code] = CostKindJump,
            [RunCheckRules.CostKindDecrease.Code] = CostKindDecrease,
            [RunCheckRules.CostKindClosedChanges.Code] = CostKindClosedChanges,
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

    /// <summary>
    /// Kontrole z ustawieniami reguł (settings – kod reguły → ustawienie; brak – domyślne); wyniki w kolejności katalogu.
    /// dictionaries – słowniki programu; null – reguły spójności ze słownikami bez wyników.
    /// </summary>
    public static IReadOnlyList<RunIssue> Check(IReadOnlyList<RunBalanceRow> current, IReadOnlyList<RunBalanceRow>? previous,
        (int Year, int Period) closedBefore, (int Year, int Period) today, IReadOnlyDictionary<string, RunCheckSetting>? settings = null,
        RunDictionaryData? dictionaries = null)
    {
        RunCheckSetting Setting(RunCheckRule rule) => settings?.GetValueOrDefault(rule.Code) ?? RunCheckRules.Default(rule);
        var context = new RunCheckContext(current, previous, closedBefore, today, Setting, dictionaries);
        return RunCheckRules.All.Where(rule => Setting(rule).Active).SelectMany(rule => Rules[rule.Code](context, Setting(rule))).ToList();
    }

    private static IEnumerable<IGrouping<string, RunBalanceRow>> CostElementGroups(RunCheckContext c) =>
        c.Current.Where(r => r.CostElement.Length > 0).GroupBy(r => r.CostElement, StringComparer.OrdinalIgnoreCase);

    // Spójność ze słownikami: cost element kosztu (bez rozliczeniowych) z pustą Cost Category w słowniku efektywnym programu.
    private static IEnumerable<RunIssue> CostElementWithoutCategory(RunCheckContext c, RunCheckSetting s) =>
        c.Dictionaries is not { } d ? []
            : CostElementGroups(c).Where(g => g.Any(r => !r.IsSettlement) && d.CostCategories.TryGetValue(g.Key, out var category) && category is null)
                .Select(g => new RunIssue(Dictionaries, s.Level, g.Key,
                    $"Cost element {g.Key} bez Cost Category w słowniku Cost Category – koszt poza raportem po kategoriach (Słowniki → Cost Category albo zmiana programu)",
                    g.Where(r => !r.IsSettlement).Sum(r => r.Amount)));

    // Spójność ze słownikami: MPK WP („WP i CAM”) bez stawki w „Stawki wydziałów” na rok – ETC Cost bez stawki.
    private static IEnumerable<RunIssue> CostCenterWithoutRate(RunCheckContext c, RunCheckSetting s) =>
        c.Dictionaries is not { } d ? []
            : d.Wps.Where(w => w.Mpk is not null && !d.RatedCostCenters.Contains(w.Mpk))
                .GroupBy(w => w.Mpk!, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new RunIssue(Dictionaries, s.Level, g.Key,
                    $"MPK {g.Key} bez stawki w „Stawki wydziałów” na rok {d.RateYear} – ETC Cost bez stawki; WP: " +
                    string.Join(", ", g.Select(w => w.Wp).Distinct().Take(Top)) + (g.Select(w => w.Wp).Distinct().Count() > Top ? "…" : "")));

    // Spójność ze słownikami: WP bez MPK w „WP i CAM” (żaden wiersz WP nie ma MPK) – ETC Cost bez stawki.
    private static IEnumerable<RunIssue> WpWithoutCostCenter(RunCheckContext c, RunCheckSetting s) =>
        c.Dictionaries is not { } d ? []
            : d.Wps.GroupBy(w => w.Wp, StringComparer.OrdinalIgnoreCase).Where(g => g.All(w => w.Mpk is null))
                .Select(g => new RunIssue(Dictionaries, s.Level, g.Key, $"WP {g.Key} bez MPK w „WP i CAM” – ETC Cost bez stawki wydziału"));

    // Spójność ze słownikami: WP bez budżetu w „Harmonogram i budżet”.
    private static IEnumerable<RunIssue> WpWithoutBudget(RunCheckContext c, RunCheckSetting s) =>
        c.Dictionaries is not { } d ? []
            : d.Wps.GroupBy(w => w.Wp, StringComparer.OrdinalIgnoreCase).Where(g => !g.Any(w => w.HasBudget))
                .Select(g => new RunIssue(Dictionaries, s.Level, g.Key,
                    $"WP {g.Key} bez budżetu (BAC, BAC HOURS, BAC MATERIAL) w „Harmonogram i budżet” – WP bez podstawy BCWS i BCWP"));

    // Rodzaje kosztu: rodzaj z kosztem, którego w poprzednim przebiegu nie było.
    private static IEnumerable<RunIssue> NewCostKind(RunCheckContext c, RunCheckSetting s)
    {
        if (c.Previous is null)
            return [];
        var kinds = c.Dictionaries?.CostKinds;
        var now = RunCostKinds.Totals(c.Current, kinds);
        var then = RunCostKinds.Totals(c.Previous, kinds);
        return RunMeasures.Kinds.Where(k => Math.Abs(now[k]) > s.Threshold!.Value && Math.Abs(then[k]) <= s.Threshold!.Value)
            .Select(k => new RunIssue(CostKinds, s.Level, k,
                $"Nowy rodzaj kosztu {k}: {Amount(now[k])} (w poprzednim przebiegu {Amount(then[k])}) – sprawdź cost elementy rodzaju {k}", now[k]));
    }

    // Rodzaje kosztu: przyrost bieżącego okresu większy niż K × średni przyrost z N poprzednich okresów.
    private static IEnumerable<RunIssue> CostKindJump(RunCheckContext c, RunCheckSetting s)
    {
        var kinds = c.Dictionaries?.CostKinds;
        foreach (var kind in RunMeasures.Kinds)
        {
            var increment = RunCostKinds.InPeriod(c.Current, kinds, kind, c.Today);
            var average = RunCostKinds.AverageBefore(c.Current, kinds, kind, c.Today, RunCheckRules.IncrementPeriods);
            if (average > 0 && increment > s.Threshold!.Value * average)
                yield return new RunIssue(CostKinds, s.Level, kind,
                    $"Skok przyrostu {kind} w okresie {c.Today.Year}/{c.Today.Period:00}: {Amount(increment)} – {Amount(increment / average)} × średni przyrost " +
                    $"{Amount(average)} z {RunCheckRules.IncrementPeriods} poprzednich okresów (próg {Amount(s.Threshold!.Value)} ×)", increment - average);
        }
    }

    // Rodzaje kosztu: koszt rodzaju narastająco mniejszy niż w poprzednim przebiegu.
    private static IEnumerable<RunIssue> CostKindDecrease(RunCheckContext c, RunCheckSetting s)
    {
        if (c.Previous is null)
            return [];
        var kinds = c.Dictionaries?.CostKinds;
        var now = RunCostKinds.Totals(c.Current, kinds);
        var then = RunCostKinds.Totals(c.Previous, kinds);
        return RunMeasures.Kinds.Where(k => now[k] < then[k] - s.Threshold!.Value)
            .Select(k => new RunIssue(CostKinds, s.Level, k,
                $"Koszt {k} narastająco mniejszy niż w poprzednim przebiegu: {Amount(now[k])} < {Amount(then[k])} (storno, przeksięgowanie, zmiana rodzaju w Cost Category?)",
                now[k] - then[k]));
    }

    // Rodzaje kosztu: koszt rodzaju w okresach zamkniętych zmieniony względem poprzedniego przebiegu; 3 okresy z największą zmianą.
    private static IEnumerable<RunIssue> CostKindClosedChanges(RunCheckContext c, RunCheckSetting s)
    {
        if (c.Previous is null)
            yield break;
        var kinds = c.Dictionaries?.CostKinds;
        foreach (var kind in RunMeasures.Kinds)
        {
            Dictionary<(int, int), decimal> ByPeriod(IEnumerable<RunBalanceRow> rows) => rows
                .Where(r => !r.IsSettlement && c.Closed(r) && RunCostKinds.Of(r, kinds) == kind)
                .GroupBy(r => (r.FiscalYear, Math.Min(r.Period, 12))).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            var now = ByPeriod(c.Current);
            var then = ByPeriod(c.Previous);
            var delta = now.Values.Sum() - then.Values.Sum();
            if (Math.Abs(delta) <= s.Threshold!.Value)
                continue;
            var periods = now.Keys.Union(then.Keys).Select(p => (Period: p, Delta: now.GetValueOrDefault(p) - then.GetValueOrDefault(p)))
                .Where(x => x.Delta != 0).OrderByDescending(x => Math.Abs(x.Delta)).Take(3)
                .Select(x => $"{x.Period.Item1}/{x.Period.Item2:00} {Amount(x.Delta)}");
            yield return new RunIssue(CostKinds, s.Level, kind,
                $"Koszt {kind} w okresach zamkniętych zmienił się o {Amount(delta)} od poprzedniego przebiegu; okresy: {string.Join(", ", periods)}", delta);
        }
    }

    private static string Amount(decimal value) => PolishNumber.ToDisplay(Math.Round(value, 2));
}
