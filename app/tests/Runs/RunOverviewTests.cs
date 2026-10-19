using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Ekran Przebiegi (RunOverview): macierz kontrola × uruchomienia, definicja przebiegu, zapis ustawień uruchomienia.</summary>
public sealed class RunOverviewTests
{
    private static RunPinRow Pin(long id, string code, int no, int day) =>
        new(id, 1, code, "PAF", no, new DateTimeOffset(2026, 10, day, 8, 0, 0, TimeSpan.Zero), "jan", null, RunStatuses.Ok, null, 10, 100m, 0m, 0, 0, null, null);

    private static RunIssueRow Issue(long pin, string check, CheckLevel level, long id) => new(id, pin, check, level, null, "x", null);

    [Fact]
    public void Matrix_has_runs_from_oldest_checks_in_catalog_order_and_worst_level_per_cell()
    {
        RunPinRow[] pins = [Pin(2, "R-PAF-2026-10-T41", 1, 9), Pin(1, "R-PAF-2026-10-T40", 2, 2)];   // od najnowszego, jak magazyn
        var issues = new Dictionary<long, IReadOnlyList<RunIssueRow>>
        {
            [1] = [Issue(1, RunChecks.NetZero, CheckLevel.Error, 1), Issue(1, RunChecks.NetZero, CheckLevel.Error, 2), Issue(1, "Import", CheckLevel.Warning, 3)],
            [2] = [Issue(2, RunChecks.NetZero, CheckLevel.Pass, 4), Issue(2, "Koszty po narzutach CAS", CheckLevel.Pass, 5), Issue(2, "Przebieg", CheckLevel.Error, 6),
                  Issue(2, RunReconciliations.CheckName, CheckLevel.Warning, 7)],
        };

        var matrix = RunOverview.Matrix(pins, issues);

        Assert.Equal(["2026-10 T40 #2", "2026-10 T41 #1"], matrix.Columns.Select(c => c.Label));
        var checks = matrix.Rows.Select(r => r.CheckName).ToList();
        Assert.Equal("Import", checks[0]);
        Assert.Equal("Koszty po narzutach CAS", checks[1]);
        Assert.Equal("Przebieg", checks[^1]);
        Assert.Equal(RunReconciliations.CheckName, checks[^2]);
        Assert.Equal(RunCheckRules.All.Select(r => r.Check).Distinct(), checks.Skip(2).SkipLast(2));
        var net = matrix.Rows.Single(r => r.CheckName == RunChecks.NetZero).Cells;
        Assert.Equal(("2 ERROR", CheckLevel.Error), (net[0].Text, net[0].Level));
        Assert.Equal(("OK", CheckLevel.Pass), (net[1].Text, net[1].Level));
        var quality = matrix.Rows.Single(r => r.CheckName == RunChecks.Quality).Cells;
        Assert.All(quality, c => Assert.Equal(("–", (CheckLevel?)null), (c.Text, c.Level)));
        Assert.Equal("1 WARNING", matrix.Rows[0].Cells[0].Text);
    }

    [Fact]
    public void Workflow_lists_steps_with_element_settings_reconciliations_and_steps_without_elements()
    {
        var definition = new RunDefinition("CAS", "CAS", null, RunCheckRules.AnyRun,
        [
            new(1, "Przypięcie", RunActions.Pin, RunGates.Info, false, [], "opis"),
            new(2, "Kontrole", RunActions.Checks, RunGates.Confirm, false,
            [
                new(RunCheckRules.NetZeroBalance.Name, Level: CheckLevel.Warning), new(RunCheckRules.NoWbsElement.Name),
                new(RunDefinitions.AllReconciliations), new("Uzgodnienie: Brak"),
            ]),
            new(3, "Zatwierdzenie", RunActions.Approval, RunGates.Confirm, true, []),
        ]);
        RunReconciliationRow[] library = [new(RunCheckRules.AllTypes, "Budżet", RunCheckRules.AnyRun, true, "[Koszt brutto]", "<=", "[BAC]", null, "WARNING")];
        var defaults = RunCheckRules.All.ToDictionary(r => r.Code, RunCheckRules.Default);

        var rows = RunOverview.Workflow(definition, defaults, library, "CAS", isClosing: false);

        Assert.Equal((1, "–", "opis"), (rows[0].StepNo, rows[0].Element, rows[0].Description));
        var net = rows.Single(r => r.Element == RunCheckRules.NetZeroBalance.Name);
        Assert.Equal(("WARNING, próg 1,00", "definicja", "ERROR – stop; WARNING – potwierdzenie"), (net.Setting, net.Source, net.Gate));
        Assert.Equal(("WARNING", "domyślne"), (rows.Single(r => r.Element == RunCheckRules.NoWbsElement.Name).Setting, rows.Single(r => r.Element == RunCheckRules.NoWbsElement.Name).Source));
        Assert.Equal("[Koszt brutto] <= [BAC] – WARNING", rows.Single(r => r.Element == "Uzgodnienie: Budżet").Setting);
        Assert.Equal("brak reguły w słowniku", rows.Single(r => r.Element == "Uzgodnienie: Brak").Setting);
        Assert.Equal("zatwierdzenie ręczne", rows[^1].Gate);
    }

    [Fact]
    public void Program_overview_shows_latest_run_and_step_where_it_stops()
    {
        RunProjectRow[] projects = [new("PAF", "PAF", "CAS"), new("M28", "M28", "SAC")];
        var pin = Pin(5, "R-PAF-2026-10-T41", 1, 9) with { Status = RunStatuses.NeedsAction, Errors = 0, Warnings = 2 };
        var steps = new Dictionary<long, IReadOnlyList<RunStepRow>>
        {
            [5] = [new(5, 1, "Import", RunActions.Import, RunStepStatuses.Done, null, null, null, 0, 0, null),
                   new(5, 2, "Kontrole", RunActions.Checks, RunStepStatuses.NeedsAction, null, null, null, 0, 2, null)],
        };

        var rows = RunOverview.Programs(projects, new Dictionary<string, RunPinRow> { ["PAF"] = pin }, steps);

        Assert.Equal(("R-PAF-2026-10-T41 #1", RunStatuses.NeedsAction, "2. Kontrole – wymaga akcji", 2), (rows[0].Run, rows[0].Status, rows[0].Stop, rows[0].Warnings));
        Assert.Null(rows[1].Run);
    }

    [Fact]
    public void Definition_lists_reconciliation_rules_of_program_type_with_condition()
    {
        RunReconciliationRow[] rules =
        [
            new(RunCheckRules.AllTypes, "Budżet", RunCheckRules.AnyRun, true, "[Koszt brutto]", "<=", "[BAC] * 1,1", 5m, "WARNING"),
            new("CAS", "CAS", RunCheckRules.ClosingRun, false, "[BAC]", ">=", "0", null, "ERROR"),
        ];

        var sac = Assert.Single(RunOverview.Reconciliations(rules, "SAC"));
        var cas = RunOverview.Reconciliations(rules, "CAS");

        Assert.Equal(("Budżet", "[Koszt brutto] <= [BAC] * 1,1 ± 5", "aktywna"), (sac.Name, sac.Condition, sac.State));
        Assert.Equal(["Budżet", "CAS"], cas.Select(r => r.Name));
        Assert.Equal("wyłączona", cas[1].State);
    }
}
