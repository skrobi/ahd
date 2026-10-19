using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Reguły wbudowane – Walidacja EV (RunChecks, reguły RunCheckRules) – okresy zamknięte przed 2026/09; reguły ACTUALS – RunValidationTests.</summary>
public sealed class RunChecksTests
{
    private static readonly (int, int) ClosedBefore = (2026, 9);
    private static readonly (int, int) Today = (2026, 10);

    private static RunBalanceRow Row(string wbs, string ce, int period, decimal amount, bool settlement = false, int rows = 1, string currency = "PLN",
        DateTime? created = null, int year = 2026) =>
        new(wbs, ce, year, period, settlement, currency, rows, amount, created ?? new DateTime(year, Math.Min(period, 12), 28));

    private static List<RunIssue> Of(IReadOnlyList<RunIssue> issues, string check, CheckLevel? level = null) =>
        issues.Where(i => i.Check == check && (level is null || i.Level == level)).ToList();

    /// <summary>Dane z wynikiem każdej reguły katalogu (poprzedni przebieg – previous).</summary>
    private static (RunBalanceRow[] Current, RunBalanceRow[] Previous) AllFindings() =>
    (
        [
            Row("A.1", "51105550", 3, 70m, rows: 3),
            Row("A.1", "91902551", 3, -60m, settlement: true, created: new DateTime(2026, 3, 1)),   // saldo 10, koszt po rozliczeniu
            Row("A.5", "99999999", 10, 7m),                                                         // spoza słownika
            Row("A.2", "92212550", 10, -50m),                                                       // pierwszy raz, ujemny koszt
            Row("A.6", "51105550", 12, 3m, currency: "USD"),                                        // okres przyszły, inna waluta
            Row("", "51105550", 9, 2m),                                                             // bez elementu WBS
            Row("A.7", "51105550", 10, 40m),                                                        // skok przyrostu Materials
        ],
        [Row("A.1", "51105550", 3, 100m, rows: 10)]
    );

    /// <summary>Słowniki P2: 92212550 bez kategorii, MPK PZW30 bez stawki na 2026 (WP-2), WP-2 bez budżetu, WP-3 bez MPK.</summary>
    private static RunDictionaryData Dictionaries() => new(
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["51105550"] = "Materials", ["92212550"] = null, ["91902551"] = null },
        [new("WP-1", "PZW20", true), new("WP-2", "PZW30", false), new("WP-3", null, true)],
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PZW20" }, 2026,
        CostKinds: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["51105550"] = "Materials", ["92212550"] = "Hours" });

    [Fact]
    public void Dictionary_consistency_rules_find_cost_element_without_category_cost_center_without_rate_and_wp_without_budget()
    {
        RunBalanceRow[] current =
        [
            Row("A.1", "51105550", 3, 100m), Row("A.2", "92212550", 4, 30m), Row("A.2", "92212550", 5, 12m),
            Row("A.1", "91902551", 3, -100m, settlement: true),   // rozliczeniowy bez kategorii – bez wyniku
            Row("A.3", "99999999", 4, 5m),                         // spoza słownika – reguła „spoza słownika”, nie „bez kategorii”
        ];

        var issues = Of(RunChecks.Check(current, null, ClosedBefore, Today, dictionaries: Dictionaries()), RunChecks.Dictionaries);
        var without = RunChecks.Check(current, null, ClosedBefore, Today);

        var category = Assert.Single(issues, i => i.Message.StartsWith("Cost element"));
        Assert.Equal(("92212550", CheckLevel.Warning, 42m), (category.Element, category.Level, category.Amount));
        var rate = Assert.Single(issues, i => i.Message.StartsWith("MPK"));
        Assert.Equal(("PZW30", CheckLevel.Error), (rate.Element, rate.Level));
        Assert.Contains("na rok 2026", rate.Message);
        Assert.EndsWith("WP: WP-2", rate.Message);
        var budget = Assert.Single(issues, i => i.Message.Contains("bez budżetu"));
        Assert.Equal(("WP-2", CheckLevel.Warning), (budget.Element, budget.Level));
        var noMpk = Assert.Single(issues, i => i.Message.Contains("bez MPK"));
        Assert.Equal(("WP-3", CheckLevel.Warning), (noMpk.Element, noMpk.Level));
        Assert.Empty(Of(without, RunChecks.Dictionaries));   // bez słowników – bez wyników
    }

    [Fact]
    public void Cost_kind_rules_find_new_kind_jump_over_average_decrease_and_closed_period_change()
    {
        // Materials: 6 okresów przed 2026/10 po 10 (średnio 10), w 2026/10 – 31 > 3 × 10; Other – nowy; Hours – spadek i zmiana w zamkniętym 2026/05.
        RunBalanceRow[] previous = [.. Enumerable.Range(4, 6).Select(p => Row("A.1", "51105550", p, 10m)), Row("A.2", "92212550", 5, 100m)];
        RunBalanceRow[] current = [.. previous.Where(r => r.CostElement == "51105550"), Row("A.1", "51105550", 10, 31m), Row("A.2", "92212550", 5, 60m),
            Row("A.3", "99999999", 10, 5m), Row("A.1", "91902551", 10, -500m, settlement: true)];

        var issues = Of(RunChecks.Check(current, previous, ClosedBefore, Today, dictionaries: Dictionaries()), RunChecks.CostKinds);
        var jumpAt30 = RunChecks.Check(current, previous, ClosedBefore, Today, dictionaries: Dictionaries(),
            settings: new Dictionary<string, RunCheckSetting> { [RunCheckRules.CostKindJump.Code] = new(true, CheckLevel.Warning, 3.2m) });

        var jump = Assert.Single(issues, i => i.Message.StartsWith("Skok"));
        Assert.Equal(("Materials", 21m), (jump.Element, jump.Amount));
        Assert.Contains("2026/10: 31 – 3,1 × średni przyrost 10 z 6 poprzednich okresów", jump.Message.Replace('\u00A0', ' '));
        Assert.DoesNotContain(jumpAt30, i => i.Message.StartsWith("Skok"));
        Assert.Equal(("Other", 5m), (Assert.Single(issues, i => i.Message.StartsWith("Nowy")).Element, Assert.Single(issues, i => i.Message.StartsWith("Nowy")).Amount));
        Assert.Equal(("Hours", -40m), (Assert.Single(issues, i => i.Message.StartsWith("Koszt Hours narastająco")).Element,
            Assert.Single(issues, i => i.Message.StartsWith("Koszt Hours narastająco")).Amount));
        var closed = Assert.Single(issues, i => i.Message.Contains("w okresach zamkniętych"));
        Assert.Equal(("Hours", -40m), (closed.Element, closed.Amount));
        Assert.EndsWith("okresy: 2026/05 -40", closed.Message);
        Assert.DoesNotContain(issues, i => i.Element == "Materials" && !i.Message.StartsWith("Skok"));   // rozliczenie poza rodzajami
    }

    [Fact]
    public void Every_catalog_rule_has_an_implementation_and_a_finding_with_its_default_level()
    {
        Assert.Equal(RunCheckRules.All.Select(r => r.Code).Order(), RunChecks.Rules.Keys.Order());
        Assert.Equal(RunCheckRules.All.Count, RunCheckRules.All.Select(r => r.Name).Distinct().Count());
        var (current, previous) = AllFindings();
        var context = new RunCheckContext(current, previous, ClosedBefore, Today, RunCheckRules.Default, Dictionaries());

        foreach (var rule in RunCheckRules.All)
        {
            var issues = RunChecks.Rules[rule.Code](context, RunCheckRules.Default(rule)).ToList();
            Assert.True(issues.Count > 0, rule.Code);
            Assert.All(issues, i => Assert.Equal(rule.Check, i.Check));
            Assert.Contains(issues, i => i.Level == rule.Level);
        }
    }

    [Fact]
    public void Settings_switch_rules_off_change_level_and_threshold()
    {
        var (current, previous) = AllFindings();
        var settings = RunCheckRules.All.ToDictionary(r => r.Code, RunCheckRules.Default);
        settings[RunCheckRules.CostElementWithoutCategory.Code] = new RunCheckSetting(false, CheckLevel.Warning, null);   // wyłączona
        settings[RunCheckRules.WpWithoutBudget.Code] = new RunCheckSetting(true, CheckLevel.Error, null);                // WARNING → ERROR
        settings[RunCheckRules.CostKindJump.Code] = new RunCheckSetting(true, CheckLevel.Warning, 1000m);                // skok w progu

        var defaults = RunChecks.Check(current, previous, ClosedBefore, Today, dictionaries: Dictionaries());
        var issues = RunChecks.Check(current, previous, ClosedBefore, Today, settings: settings, dictionaries: Dictionaries());

        Assert.Contains(defaults, i => i.Message.StartsWith("Cost element 92212550 bez Cost Category"));
        Assert.DoesNotContain(issues, i => i.Message.StartsWith("Cost element 92212550 bez Cost Category"));
        Assert.Equal(CheckLevel.Error, Assert.Single(issues, i => i.Message.Contains("bez budżetu")).Level);
        Assert.Contains(defaults, i => i.Message.StartsWith("Skok"));
        Assert.DoesNotContain(issues, i => i.Message.StartsWith("Skok"));
    }

    private static RunCheckRow Setting(string type, RunCheckRule rule, string run, bool active = true, string? level = null, decimal? threshold = null) =>
        new(type, rule.Name, run, active, level, threshold);

    [Fact]
    public void Dictionary_row_of_project_type_and_run_variant_wins_over_general_rows()
    {
        RunCheckRow[] rows =
        [
            Setting(RunCheckRules.AllTypes, RunCheckRules.CostKindDecrease, RunCheckRules.AnyRun, level: "WARNING", threshold: 5m),
            Setting("CAS", RunCheckRules.CostKindDecrease, RunCheckRules.AnyRun, level: "ERROR"),
            Setting("CAS", RunCheckRules.CostKindDecrease, RunCheckRules.ClosingRun, threshold: 0m),
            Setting(RunCheckRules.AllTypes, RunCheckRules.WpWithoutBudget, RunCheckRules.MidPeriodRun, active: false),
            Setting(RunCheckRules.AllTypes, RunCheckRules.WpWithoutCostCenter, RunCheckRules.AnyRun, level: "INFO", threshold: 3m),   // reguła bez progu
            new(RunCheckRules.AllTypes, "nieznana reguła", RunCheckRules.AnyRun, false, null, null),
        ];

        var sac = RunChecks.Settings(rows, "SAC", isClosing: false);
        var casMid = RunChecks.Settings(rows, "CAS", isClosing: false);
        var casClosing = RunChecks.Settings(rows, "CAS", isClosing: true);

        Assert.Equal(new RunCheckSetting(true, CheckLevel.Warning, 5m), sac[RunCheckRules.CostKindDecrease.Code]);
        Assert.Equal(new RunCheckSetting(true, CheckLevel.Error, 1m), casMid[RunCheckRules.CostKindDecrease.Code]);        // próg domyślny
        Assert.Equal(new RunCheckSetting(true, CheckLevel.Warning, 0m), casClosing[RunCheckRules.CostKindDecrease.Code]);  // poziom domyślny
        Assert.False(sac[RunCheckRules.WpWithoutBudget.Code].Active);
        Assert.True(casClosing[RunCheckRules.WpWithoutBudget.Code].Active);
        Assert.Equal(new RunCheckSetting(true, CheckLevel.Pass, null), sac[RunCheckRules.WpWithoutCostCenter.Code]);
        Assert.Equal(RunCheckRules.Default(RunCheckRules.CostCenterWithoutRate), sac[RunCheckRules.CostCenterWithoutRate.Code]);
        Assert.Equal(RunCheckRules.All.Count, RunChecks.Settings([], "SAC", false).Count);
    }

    [Fact]
    public void Dictionary_validation_rejects_negative_threshold_and_warns_about_threshold_of_rule_without_one()
    {
        var spec = PzlEv.Shared.Utils.Dictionaries.GlobalDictionaries.Get(PzlEv.Shared.Utils.Dictionaries.GlobalDictionaries.RunChecks);
        DictRow Row(RunCheckRule rule, string threshold) => new(null, null, new Dictionary<string, string?>
        {
            ["Typ programu"] = RunCheckRules.AllTypes, ["Reguła"] = rule.Name, ["Przebieg"] = RunCheckRules.AnyRun, ["Aktywna"] = "tak",
            ["Poziom"] = null, ["Próg"] = threshold, ["Opis"] = null,
        });

        var issues = PzlEv.Shared.Utils.Dictionaries.DictionaryValidator.Validate(spec,
            [Row(RunCheckRules.CostKindDecrease, "-1"), Row(RunCheckRules.WpWithoutCostCenter, "2"), Row(RunCheckRules.CostKindJump, "100")]);

        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message.StartsWith("Próg nie może być ujemny"));
        Assert.Contains(issues, i => i.Level == CheckLevel.Warning && i.Message.Contains(RunCheckRules.WpWithoutCostCenter.Name));
        Assert.Equal(2, issues.Count(i => i.Level != CheckLevel.Pass));
    }
}
