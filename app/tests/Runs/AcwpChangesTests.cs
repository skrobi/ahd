using PzlEv.Modules.Runs.Data;
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

    private static RunLineRow Line(int row, decimal amount, decimal tranCurr, string po = "PO1", string createdOn = "2026-10-18", decimal? quantity = null,
        string file = "ACTUALS.xlsx", string? coObject = "Certyfikacja") =>
        new(file, row, createdOn, amount, quantity, "PZL Des Eng", coObject, "PETE22/41055", po, null, null, "PLN", tranCurr, null);

    [Fact]
    public void Lines_pair_before_and_after_by_key_fields_into_one_row_with_state()
    {
        // Ta sama linia w nowym imporcie ma inny numer wiersza – łączona po polach klucza, nie po pliku i wierszu.
        RunLineRow[] before = [Line(10, 100m, 100m), Line(11, 50m, 50m, po: "PO2"), Line(12, 30m, 30m, po: "PO3"), Line(13, 7m, 7m, po: "PO4", quantity: 1m)];
        RunLineRow[] after = [Line(20, 100m, 100m, file: "ACTUALS_NEW.xlsx"), Line(21, 55m, 50m, po: "po2 "), Line(22, 40m, 40m, po: "PO3"), Line(23, 9m, 9m, po: "PO5"),
            Line(24, 7m, 7m, po: "PO4", quantity: 2m)];

        var all = AcwpChanges.Lines(before, after, onlyChanges: false);
        var changes = AcwpChanges.Lines(before, after, onlyChanges: true);

        // PO3: inna Value TranCurr – inna linia (usunięta i nowa); PO2: ten sam klucz (wielkość liter, spacje), inna kwota – zmieniona.
        Assert.Equal([(AcwpLineStates.New, 40m), (AcwpLineStates.Removed, -30m), (AcwpLineStates.New, 9m), (AcwpLineStates.Changed, 5m),
            (AcwpLineStates.Changed, 0m), (AcwpLineStates.Unchanged, 0m)], all.Select(l => (l.State, l.Delta)));
        Assert.Equal((10, 20), (all[^1].Before!.RowNumber, all[^1].After!.RowNumber));
        Assert.Equal((11, 21), (all[3].Before!.RowNumber, all[3].After!.RowNumber));
        Assert.Equal(12, all[1].Line.RowNumber);   // usunięta – opis z linii „było”
        Assert.Null(all[1].After);
        Assert.Equal((2m, 1m), (all[4].After!.Quantity, all[4].Before!.Quantity));   // PO4: zmiana tylko ilości
        Assert.Equal(5, changes.Count);
        Assert.DoesNotContain(changes, l => l.State == AcwpLineStates.Unchanged);
    }

    [Fact]
    public void Lines_with_repeated_key_pair_in_file_and_row_order_and_extra_ones_are_new_or_removed()
    {
        RunLineRow[] before = [Line(2, 10m, 10m), Line(1, 10m, 10m), Line(3, 5m, 5m, createdOn: "2026-10-01", coObject: null)];
        RunLineRow[] after = [Line(7, 10m, 10m), Line(5, 10m, 10m), Line(6, 10m, 10m), Line(8, 5m, 5m, createdOn: "2026-10-01", coObject: " ")];

        var lines = AcwpChanges.Lines(before, after, onlyChanges: false);

        Assert.Equal([(1, 5), (2, 6), (3, 8)], lines.Where(l => l.State == AcwpLineStates.Unchanged).Select(l => (l.Before!.RowNumber, l.After!.RowNumber)).OrderBy(x => x));
        var added = Assert.Single(lines, l => l.State == AcwpLineStates.New);
        Assert.Equal((7, 10m), (added.After!.RowNumber, added.Delta));
        Assert.Equal(added, lines[0]);   // zmiany przed liniami bez zmian
        Assert.Empty(AcwpChanges.Lines([], [], onlyChanges: true));
    }

    [Fact]
    public void Chart_has_increments_and_cumulative_cost_of_chart_kinds_including_cost_before_first_chart_period()
    {
        // Koszt w 2025/01 – przed 12 okresami wykresu (2025/09–2026/08) – wchodzi do wartości skumulowanej; okres 14 jak 12.
        RunBalanceRow[] rows = [new("A.1", "51105550", 2025, 1, false, "PLN", 1, 1000m, null), Row("A.1", "51105550", 7, 31m), Row("A.1", "51105550", 8, 25m),
            Row("A.1", "92212550", 8, 4m), Row("A.1", "91902551", 8, -500m, settlement: true), new("A.1", "51105550", 2025, 14, false, "PLN", 1, 2m, null)];

        var all = AcwpChanges.Chart(rows, Kinds, null, null, null, factor: 3m);
        var hours = AcwpChanges.Chart(rows, Kinds, "Hours", null, null, factor: 3m);

        Assert.Equal(["Hours", "Materials", "Other"], all.Series.Select(s => s.Kind));
        Assert.Equal(12, all.Cumulative.Count);
        Assert.Equal(1000m, all.Cumulative[0]);    // 2025/09
        Assert.Equal(1002m, all.Cumulative[3]);    // 2025/12
        Assert.Equal(1033m, all.Cumulative[^2]);   // 2026/07
        Assert.Equal(1062m, all.Cumulative[^1]);   // 2026/08: + 25 Materials + 4 Hours (bez rozliczenia)
        Assert.Equal(4m, hours.Cumulative[^1]);
        Assert.Equal(0m, hours.Cumulative[0]);
        Assert.Empty(AcwpChanges.Chart(rows, Kinds, null, new HashSet<string> { "X" }, null, 3m).Cumulative);
    }
}
