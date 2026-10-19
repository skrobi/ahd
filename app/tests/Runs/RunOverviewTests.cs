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
    public void Compare_marks_new_resolved_changed_and_same_results_by_check_and_element()
    {
        RunIssueRow Row(long id, long pin, string check, CheckLevel level, string? element, decimal? amount) => new(id, pin, check, level, element, $"opis {id}", amount);
        RunIssueRow[] before =
        [
            Row(1, 1, RunChecks.NetZero, CheckLevel.Error, "P-1", 100m), Row(2, 1, RunChecks.Quality, CheckLevel.Warning, "P-2", null),
            Row(3, 1, RunChecks.Volume, CheckLevel.Warning, null, 50m), Row(4, 1, RunChecks.CostKinds, CheckLevel.Warning, "Hours", 10m),
        ];
        RunIssueRow[] after =
        [
            Row(11, 2, RunChecks.NetZero, CheckLevel.Error, "P-1", 100.004m), Row(12, 2, RunChecks.Volume, CheckLevel.Warning, null, 70m),
            Row(13, 2, RunChecks.CostKinds, CheckLevel.Warning, "Hours", 10m), Row(14, 2, RunChecks.CostKinds, CheckLevel.Warning, "Hours", 5m),
            Row(15, 2, RunChecks.NewCostElements, CheckLevel.Error, "0051100000", null),
        ];

        var rows = RunOverview.Compare(before, after, "PAF");

        string Status(string check, string? element) => rows.Single(r => r.CheckName == check && r.Element == element).Status;
        Assert.Equal(RunCompareRow.Same, Status(RunChecks.NetZero, "P-1"));
        Assert.Equal(RunCompareRow.Resolved, Status(RunChecks.Quality, "P-2"));
        Assert.Equal(RunCompareRow.Changed, Status(RunChecks.Volume, null));
        Assert.Equal(RunCompareRow.Changed, Status(RunChecks.CostKinds, "Hours"));   // dwa wyniki zamiast jednego
        Assert.Equal(RunCompareRow.New, Status(RunChecks.NewCostElements, "0051100000"));
        Assert.Equal((RunChecks.NewCostElements, CheckLevel.Error), (rows[0].CheckName, rows[0].Level!.Value));   // ERROR, nowy przed bez zmian
        var volume = rows.Single(r => r.CheckName == RunChecks.Volume);
        Assert.Equal((50m, 70m, 20m), (volume.AmountBefore!.Value, volume.AmountAfter!.Value, volume.Difference!.Value));
        Assert.Equal("opis 13 (+1)", rows.Single(r => r.CheckName == RunChecks.CostKinds).Message);
        Assert.Equal(("Hours", 2L), (rows.Single(r => r.CheckName == RunChecks.CostKinds).Issue.Changes!.Kind, rows.Single(r => r.CheckName == RunChecks.CostKinds).Issue.Changes!.PinId!.Value));
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
