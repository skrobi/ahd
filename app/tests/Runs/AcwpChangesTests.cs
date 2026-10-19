using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Okno „Zmiany ACWP” – porównanie (AcwpChanges): dwa uruchomienia, okres do okresu, filtry, pierwsze N i „pozostałe”.</summary>
public sealed class AcwpChangesTests
{
    private static RunBalanceRow Row(string wbs, string ce, int period, decimal amount, bool settlement = false) =>
        new(wbs, ce, 2026, period, settlement, "PLN", 1, amount, null);

    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase) { ["51105550"] = "Materials", ["92212550"] = "Hours" };
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase) { ["51105550"] = "Materiały" };

    [Fact]
    public void Two_runs_show_kind_totals_and_tree_from_largest_change_down_to_period_with_line_key()
    {
        RunBalanceRow[] before = [Row("A.1", "51105550", 8, 100m), Row("A.2", "92212550", 8, 50m), Row("A.1", "91902551", 8, -100m, settlement: true)];
        RunBalanceRow[] after = [Row("A.1", "51105550", 8, 100m), Row("A.1", "51105550", 9, 30m), Row("A.3", "51105550", 9, 70m), Row("A.2", "92212550", 8, 40m),
            Row("A.4", "99999999", 9, 5m)];

        var result = AcwpChanges.Runs(before, after, Kinds, Names, kind: null, wbsElements: null, onlyChanges: true);

        Assert.Equal([("Hours", 50m, 40m), ("Materials", 100m, 200m), ("Other", 0m, 5m)], result.Totals.Select(t => (t.Kind, t.Before, t.After)));
        Assert.Equal(100m, result.Totals[1].Percent);
        Assert.Null(result.Totals[2].Percent);
        Assert.Equal(["Materials", "Hours", "Other"], result.Tree.Select(n => n.Label));   // od największej zmiany
        var ce = Assert.Single(result.Tree[0].Children);
        Assert.Equal(("51105550 – Materiały", 100m), (ce.Label, ce.Delta));
        Assert.Equal(["A.3", "A.1"], ce.Children.Select(n => n.Label));                     // A.1 okres 08 bez zmiany – pominięty
        var period = Assert.Single(ce.Children[1].Children);
        Assert.Equal(("2026/09", 30m), (period.Label, period.Delta));
        Assert.Equal(new AcwpLineKey("A.1", "51105550", (2026, 9), (2026, 9)), period.Line);
        Assert.Equal(2, AcwpChanges.Runs(before, after, Kinds, Names, null, null, onlyChanges: false).Tree[0].Children[0].Children[1].Children.Count);
    }

    [Fact]
    public void Kind_and_wbs_filter_and_top_with_remainder()
    {
        RunBalanceRow[] after = [.. Enumerable.Range(1, 5).Select(i => Row($"B.{i}", "51105550", 9, i * 10m)), Row("C.1", "92212550", 9, 7m)];

        var materials = AcwpChanges.Runs([], after, Kinds, null, "Materials", null, onlyChanges: true, top: 2);
        var element = AcwpChanges.Runs([], after, Kinds, null, null, new HashSet<string> { "B.2", "C.1" }, onlyChanges: true);

        Assert.Equal(["Materials"], materials.Totals.Select(t => t.Kind));
        var wbs = materials.Tree.Single().Children.Single().Children;
        Assert.Equal(["B.5", "B.4", "pozostałe: 3 pozycji"], wbs.Select(n => n.Label));
        Assert.True(wbs[2].Remainder);
        Assert.Equal(60m, wbs[2].After);   // 10 + 20 + 30
        Assert.Equal([("Hours", 7m), ("Materials", 20m), ("Other", 0m)], element.Totals.Select(t => (t.Kind, t.After)));
    }

    [Fact]
    public void Period_to_period_compares_costs_booked_in_two_periods_with_wbs_leaf_and_both_periods_in_line_key()
    {
        RunBalanceRow[] rows = [Row("A.1", "51105550", 8, 100m), Row("A.1", "51105550", 9, 160m), Row("A.2", "51105550", 9, 20m), Row("A.1", "92212550", 7, 5m),
            new("A.1", "51105550", 2025, 14, false, "PLN", 1, 1m, null)];

        var result = AcwpChanges.Periods(rows, (2026, 8), (2026, 9), Kinds, null, null, null, onlyChanges: true);

        Assert.Equal((100m, 180m), (result.Totals.Single(t => t.Kind == "Materials").Before, result.Totals.Single(t => t.Kind == "Materials").After));
        Assert.Equal(0m, result.Totals.Single(t => t.Kind == "Hours").After);
        var leaves = result.Tree.Single().Children.Single().Children;
        Assert.Equal(["A.1", "A.2"], leaves.Select(n => n.Label));
        Assert.Empty(leaves[0].Children);
        Assert.Equal(new AcwpLineKey("A.1", "51105550", (2026, 8), (2026, 9)), leaves[0].Line);
        Assert.Equal([(2026, 9), (2026, 8), (2026, 7), (2025, 12)], AcwpChanges.PeriodsOf(rows));   // okres 14 jak 12
    }

    [Fact]
    public void Increments_cover_12_periods_up_to_last_data_period_and_mark_jumps_over_factor_times_average()
    {
        // Materials: 10 w okresach 2026/01–06, 31 w 2026/07 (> 3 × 10), 25 w 2026/08 (średnia z 02–07 = 13,5 – bez skoku).
        RunBalanceRow[] rows = [.. Enumerable.Range(1, 6).Select(p => Row("A.1", "51105550", p, 10m)), Row("A.1", "51105550", 7, 31m), Row("A.1", "51105550", 8, 25m),
            Row("A.1", "91902551", 8, -500m, settlement: true)];

        var series = AcwpChanges.Increments(rows, Kinds, "Materials", null, null, factor: 3m);

        var materials = Assert.Single(series).Increments;
        Assert.Equal(12, materials.Count);
        Assert.Equal(("2025/09", "2026/08"), (materials[0].Label, materials[^1].Label));
        // 2026/02 – drugi okres kosztu: średnia z 6 okresów przed nim 10 / 6, więc 10 to skok (jak reguła przebiegu przy rozruchu).
        Assert.Equal(["2026/02", "2026/07"], materials.Where(i => i.IsJump).Select(i => i.Label));
        Assert.Equal((31m, 10m), (materials[^2].Amount, materials[^2].Average));
        Assert.Equal(13.5m, materials[^1].Average);
        Assert.Empty(AcwpChanges.Increments(rows, Kinds, null, new HashSet<string> { "X" }, null, 3m));
    }

    [Fact]
    public void Cost_element_filter_and_flatten_for_excel_skip_remainder()
    {
        RunBalanceRow[] after = [Row("A.1", "51105550", 9, 30m), Row("A.2", "51105550", 9, 20m), Row("A.1", "92212550", 9, 5m)];

        var one = AcwpChanges.Runs([], after, Kinds, null, null, null, onlyChanges: true, costElement: "92212550");
        var flat = AcwpChanges.Flatten(AcwpChanges.Runs([], after, Kinds, Names, null, null, onlyChanges: true, top: 1).Tree);

        Assert.Equal([("Hours", 5m), ("Materials", 0m), ("Other", 0m)], one.Totals.Select(t => (t.Kind, t.After)));
        var row = Assert.Single(flat.Where(r => r.Kind == "Materials"));   // A.2 w „pozostałe” – pominięty
        Assert.Equal(("51105550 – Materiały", "A.1", "2026/09", 30m), (row.CostElement, row.WbsElement, row.Period, row.Delta));
        Assert.Equal("", Assert.Single(AcwpChanges.Flatten(AcwpChanges.Periods(after, (2026, 8), (2026, 9), Kinds, null, null, null, true, costElement: "92212550").Tree)).Period);
    }

    [Fact]
    public void Run_issue_opens_changes_for_its_kind_cost_element_or_wbs_element_and_its_run()
    {
        RunIssueRow Issue(string check, string? element, string message = "x") => new(1, 77, check, CheckLevel.Warning, element, message, null);

        Assert.Equal(new PzlEv.Shared.Utils.Data.AcwpChangesRequest("PAF", Kind: "Other", PinId: 77, Scope: "Rodzaje kosztu: Other"),
            RunOverview.ChangesFor(Issue(RunChecks.CostKinds, "Other"), "PAF"));
        Assert.Equal("99999999", RunOverview.ChangesFor(Issue(RunChecks.NewCostElements, "99999999"), "PAF")!.CostElement);
        Assert.Equal("51105550", RunOverview.ChangesFor(Issue(RunChecks.Deviations, "51105550", "cost element 51105550: zmiana"), "PAF")!.CostElement);
        Assert.Equal(["A.1"], RunOverview.ChangesFor(Issue(RunChecks.Deviations, "A.1", "element WBS A.1: zmiana"), "PAF")!.WbsElements);
        Assert.Equal(["A.1"], RunOverview.ChangesFor(Issue(RunChecks.NetZero, "A.1"), "PAF")!.WbsElements);
        var program = RunOverview.ChangesFor(Issue(RunChecks.Volume, null), "PAF")!;
        Assert.Equal((null, null, (long?)77), (program.Kind, program.WbsElements, program.PinId));
        Assert.Null(RunOverview.ChangesFor(Issue("Import", null), "PAF"));
        Assert.Null(RunOverview.ChangesFor(Issue(RunChecks.Dictionaries, "WP-1"), "PAF"));
    }
}
