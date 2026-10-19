using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Kontrole danych przebiegu (RunChecks, reguły RunCheckRules) – kwoty w walucie obiektu, okresy zamknięte przed 2026/09.</summary>
public sealed class RunChecksTests
{
    private static readonly (int, int) ClosedBefore = (2026, 9);
    private static readonly (int, int) Today = (2026, 10);
    private static readonly HashSet<string> Dictionary = new(StringComparer.OrdinalIgnoreCase) { "51105550", "92212550", "91902551" };

    private static RunBalanceRow Row(string wbs, string ce, int period, decimal amount, bool settlement = false, int rows = 1, string currency = "PLN",
        DateTime? created = null, int year = 2026) =>
        new(wbs, ce, year, period, settlement, currency, rows, amount, created ?? new DateTime(year, Math.Min(period, 12), 28));

    private static List<RunIssue> Of(IReadOnlyList<RunIssue> issues, string check, CheckLevel? level = null) =>
        issues.Where(i => i.Check == check && (level is null || i.Level == level)).ToList();

    [Fact]
    public void Settled_project_nets_to_zero_and_first_run_has_nothing_to_compare()
    {
        RunBalanceRow[] current = [Row("A.1", "51105550", 3, 100m), Row("A.1", "91902551", 3, -100m, settlement: true), Row("A.1", "51105550", 10, 50m)];

        var issues = RunChecks.Check(current, null, Dictionary, ClosedBefore, Today);

        Assert.Equal(CheckLevel.Pass, Assert.Single(Of(issues, RunChecks.NetZero)).Level);   // okres 10 otwarty – poza saldem
        Assert.Contains("Pierwszy przebieg", Assert.Single(Of(issues, RunChecks.Volume)).Message);
        Assert.DoesNotContain(issues, i => i.Level != CheckLevel.Pass);
        Assert.Equal(150m, RunChecks.GrossCost(current));
    }

    [Fact]
    public void Not_netting_project_shows_elements_with_balance_and_reason()
    {
        RunBalanceRow[] current =
        [
            Row("A.1", "51105550", 3, 100m), Row("A.1", "91902551", 3, -60m, settlement: true),   // rozliczenie niepełne
            Row("A.2", "92212550", 4, 30m),                                                        // brak rozliczenia
            Row("A.3", "51105550", 5, 10m), Row("A.3", "91902551", 5, -25m, settlement: true),    // rozliczenie większe niż koszt
            Row("A.4", "51105550", 6, 5m, created: new DateTime(2026, 7, 3)),
            Row("A.4", "91902551", 6, -5m, settlement: true, created: new DateTime(2026, 7, 1)),  // koszt po rozliczeniu
        ];

        var issues = RunChecks.Check(current, null, Dictionary, ClosedBefore, Today);

        var net = Of(issues, RunChecks.NetZero, CheckLevel.Error);
        Assert.Equal(55m, net[0].Amount);   // saldo projektu
        Assert.Equal(["A.1", "A.2", "A.3"], net.Skip(1).Select(i => i.Element));   // od największego salda
        Assert.Contains("rozliczenie niepełne", net[1].Message);
        Assert.Contains("brak rozliczenia", net[2].Message);
        Assert.Contains("rozliczenie większe niż koszt", net[3].Message);
        var late = Assert.Single(Of(issues, RunChecks.NetZero, CheckLevel.Warning));
        Assert.Equal("A.4", late.Element);
        Assert.Contains("po rozliczeniu z 2026-07-01", late.Message);
    }

    [Fact]
    public void Comparison_with_previous_run_finds_less_data_deviations_new_cost_elements_and_changed_closed_periods()
    {
        RunBalanceRow[] previous = [Row("A.1", "51105550", 3, 100m, rows: 10), Row("A.2", "92212550", 4, 40m, rows: 5)];
        RunBalanceRow[] current =
        [
            Row("A.1", "51105550", 3, 70m, rows: 3),                       // zamknięty okres zmieniony, mniej wierszy
            Row("A.2", "92212550", 4, 40m, rows: 5),
            Row("A.5", "99999999", 10, 7m),                                 // cost element spoza słownika
            Row("A.2", "91902551", 10, -1m, settlement: true),              // pierwszy raz w projekcie
            Row("A.6", "51105550", 12, 3m, currency: "USD"),                // okres przyszły, inna waluta
            Row("", "51105550", 9, 2m),                                     // bez elementu WBS
        ];

        var issues = RunChecks.Check(current, previous, Dictionary, ClosedBefore, Today);

        Assert.StartsWith("Mniej wierszy danych niż w poprzednim przebiegu: 12 < 15", Of(issues, RunChecks.Volume, CheckLevel.Error).Single().Message);
        Assert.Equal(-30m + 7m + 3m + 2m, Of(issues, RunChecks.Volume, CheckLevel.Warning).Single().Amount);   // koszt brutto 122 < 140
        Assert.Equal(["A.1", "A.5", "A.6", ""], Of(issues, RunChecks.Deviations).Where(i => i.Message.StartsWith("element WBS")).Select(i => i.Element));
        Assert.Equal("99999999", Of(issues, RunChecks.NewCostElements, CheckLevel.Error).Single().Element);
        Assert.Equal("91902551", Of(issues, RunChecks.NewCostElements, CheckLevel.Pass).Single().Element);
        var closed = Of(issues, RunChecks.ClosedPeriods).Single();
        Assert.Equal(("A.1", -30m), (closed.Element, closed.Amount));
        var quality = Of(issues, RunChecks.Quality).Select(i => i.Message).ToList();
        Assert.Contains(quality, q => q.StartsWith("Wiersze w okresach przyszłych: 1 (2026/12)"));
        Assert.Contains(quality, q => q.StartsWith("Waluta inna niż PLN: USD"));
        Assert.Contains(quality, q => q.StartsWith("Wiersze bez elementu WBS: 1"));
    }

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
        ],
        [Row("A.1", "51105550", 3, 100m, rows: 10)]
    );

    [Fact]
    public void Every_catalog_rule_has_an_implementation_and_a_finding_with_its_default_level()
    {
        Assert.Equal(RunCheckRules.All.Select(r => r.Code).Order(), RunChecks.Rules.Keys.Order());
        Assert.Equal(RunCheckRules.All.Count, RunCheckRules.All.Select(r => r.Name).Distinct().Count());
        var (current, previous) = AllFindings();
        var context = new RunCheckContext(current, previous, Dictionary, ClosedBefore, Today, "PLN", null, RunCheckRules.Default);

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
        settings[RunCheckRules.CostElementOutside.Code] = new RunCheckSetting(false, CheckLevel.Error, null);    // wyłączona
        settings[RunCheckRules.NoWbsElement.Code] = new RunCheckSetting(true, CheckLevel.Error, null);           // WARNING → ERROR
        settings[RunCheckRules.NetZeroBalance.Code] = new RunCheckSetting(true, CheckLevel.Error, 20m);         // saldo 10 w tolerancji

        var defaults = RunChecks.Check(current, previous, Dictionary, ClosedBefore, Today);
        var issues = RunChecks.Check(current, previous, Dictionary, ClosedBefore, Today, settings: settings);

        Assert.Contains(defaults, i => i.Check == RunChecks.NewCostElements && i.Level == CheckLevel.Error);
        Assert.DoesNotContain(issues, i => i.Check == RunChecks.NewCostElements && i.Level == CheckLevel.Error);
        Assert.Equal(CheckLevel.Error, Assert.Single(issues, i => i.Message.StartsWith("Wiersze bez elementu WBS")).Level);
        Assert.Equal(CheckLevel.Error, Of(defaults, RunChecks.NetZero)[0].Level);
        Assert.Equal(CheckLevel.Pass, Assert.Single(Of(issues, RunChecks.NetZero)).Level);   // netuje się; bez „koszt po rozliczeniu”
        Assert.Contains(Of(defaults, RunChecks.NetZero), i => i.Level == CheckLevel.Warning);
    }

    private static RunCheckRow Setting(string type, RunCheckRule rule, string run, bool active = true, string? level = null, decimal? threshold = null) =>
        new(type, rule.Name, run, active, level, threshold);

    [Fact]
    public void Dictionary_row_of_project_type_and_run_variant_wins_over_general_rows()
    {
        RunCheckRow[] rows =
        [
            Setting(RunCheckRules.AllTypes, RunCheckRules.NetZeroBalance, RunCheckRules.AnyRun, level: "WARNING", threshold: 5m),
            Setting("CAS", RunCheckRules.NetZeroBalance, RunCheckRules.AnyRun, level: "ERROR"),
            Setting("CAS", RunCheckRules.NetZeroBalance, RunCheckRules.ClosingRun, threshold: 0m),
            Setting(RunCheckRules.AllTypes, RunCheckRules.NoWbsElement, RunCheckRules.MidPeriodRun, active: false),
            Setting(RunCheckRules.AllTypes, RunCheckRules.FuturePeriods, RunCheckRules.AnyRun, level: "INFO", threshold: 3m),   // reguła bez progu
            new(RunCheckRules.AllTypes, "nieznana reguła", RunCheckRules.AnyRun, false, null, null),
        ];

        var sac = RunChecks.Settings(rows, "SAC", isClosing: false);
        var casMid = RunChecks.Settings(rows, "CAS", isClosing: false);
        var casClosing = RunChecks.Settings(rows, "CAS", isClosing: true);

        Assert.Equal(new RunCheckSetting(true, CheckLevel.Warning, 5m), sac[RunCheckRules.NetZeroBalance.Code]);
        Assert.Equal(new RunCheckSetting(true, CheckLevel.Error, 1m), casMid[RunCheckRules.NetZeroBalance.Code]);        // próg domyślny
        Assert.Equal(new RunCheckSetting(true, CheckLevel.Error, 0m), casClosing[RunCheckRules.NetZeroBalance.Code]);    // poziom domyślny
        Assert.False(sac[RunCheckRules.NoWbsElement.Code].Active);
        Assert.True(casClosing[RunCheckRules.NoWbsElement.Code].Active);
        Assert.Equal(new RunCheckSetting(true, CheckLevel.Pass, null), sac[RunCheckRules.FuturePeriods.Code]);
        Assert.Equal(RunCheckRules.Default(RunCheckRules.RowsDecrease), sac[RunCheckRules.RowsDecrease.Code]);
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
            [Row(RunCheckRules.NetZeroBalance, "-1"), Row(RunCheckRules.FuturePeriods, "2"), Row(RunCheckRules.CostDecrease, "100")]);

        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message.StartsWith("Próg nie może być ujemny"));
        Assert.Contains(issues, i => i.Level == CheckLevel.Warning && i.Message.Contains(RunCheckRules.FuturePeriods.Name));
        Assert.Equal(2, issues.Count(i => i.Level != CheckLevel.Pass));
    }
}
