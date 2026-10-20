using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Przebieg programu (RunOverview): macierz reguła × uruchomienia, porównanie dwóch uruchomień, zakres „Pokaż zmiany”.</summary>
public sealed class RunOverviewTests
{
    private const string Net = "Netowanie do 0 w okresach zamkniętych";
    private const string Kinds = "Spadek kosztu narastająco w rodzaju";

    private static RunPinRow Pin(long id, string code, int no, int day) =>
        new(id, 1, code, "PAF", no, new DateTimeOffset(2026, 10, day, 8, 0, 0, TimeSpan.Zero), "jan", null, RunStatuses.Ok, null, 10, 100m, 0m, 0, 0, null, null);

    private static RunIssueRow Issue(long pin, string check, CheckLevel level, long id, string? details = null) => new(id, pin, check, level, null, "x", null, 3, details);

    // Wiersz wyniku reguły jak w przebiegu: kolumny tekstowe i liczbowe.
    private static string Details(params (string Column, object? Value)[] values) =>
        RunRules.Details([.. values.Select(v => new RunRuleColumn(v.Column, v.Value is decimal))], [.. values.Select(v => v.Value)]);

    [Fact]
    public void Matrix_has_runs_from_oldest_rules_in_library_order_and_worst_level_per_cell()
    {
        RunPinRow[] pins = [Pin(2, "R-PAF-2026-10-T41", 1, 9), Pin(1, "R-PAF-2026-10-T40", 2, 2)];   // od najnowszego, jak magazyn
        var issues = new Dictionary<long, IReadOnlyList<RunIssueRow>>
        {
            [1] = [Issue(1, Net, CheckLevel.Error, 1, "{}"), Issue(1, Net, CheckLevel.Error, 2, "{}"), Issue(1, "Import", CheckLevel.Warning, 3),
                   Issue(1, "Największe odchylenia", CheckLevel.Pass, 8, "{}")],
            [2] = [Issue(2, Net, CheckLevel.Pass, 4), Issue(2, "Koszty po narzutach CAS", CheckLevel.Pass, 5), Issue(2, "Przebieg", CheckLevel.Error, 6),
                   Issue(2, RunService.Pinned, CheckLevel.Pass, 7), Issue(2, "Reguła spoza biblioteki", CheckLevel.Warning, 9, "{}")],
        };

        var matrix = RunOverview.Matrix(pins, issues, ["Największe odchylenia", Net]);

        Assert.Equal(["2026-10 T40 #2", "2026-10 T41 #1"], matrix.Columns.Select(c => c.Label));
        Assert.Equal(["Import", RunService.Pinned, "Największe odchylenia", Net, "Koszty po narzutach CAS", "Reguła spoza biblioteki", "Przebieg"],
            matrix.Rows.Select(r => r.CheckName));
        var net = matrix.Rows.Single(r => r.CheckName == Net).Cells;
        Assert.Equal(("2 ERROR", CheckLevel.Error), (net[0].Text, net[0].Level));
        Assert.Equal(("OK", CheckLevel.Pass), (net[1].Text, net[1].Level));                     // reguła bez wierszy
        var deviations = matrix.Rows.Single(r => r.CheckName == "Największe odchylenia").Cells;
        Assert.Equal(("1 INFO", "–"), (deviations[0].Text, deviations[1].Text));                   // wiersz reguły INFO; brak wyniku
        Assert.Equal("1 WARNING", matrix.Rows[0].Cells[0].Text);
        Assert.Equal(("INFO", "OK"), (RunOverview.LevelText(issues[1][3]), RunOverview.LevelText(issues[2][0])));
    }

    [Fact]
    public void Compare_matches_rule_rows_by_text_columns_and_shows_differences_in_number_columns()
    {
        RunIssueRow Row(long id, long pin, string check, CheckLevel level, string? details, string? element = null) => new(id, pin, check, level, element, $"opis {id}", null, 3, details);
        RunIssueRow[] before =
        [
            Row(1, 1, Net, CheckLevel.Error, Details(("ElementWBS", "P-1"), ("Saldo", 100m), ("OstatniOkres", "2026/08"))),
            Row(2, 1, Net, CheckLevel.Error, Details(("ElementWBS", "P-2"), ("Saldo", 40m), ("OstatniOkres", "2026/08"))),
            Row(3, 1, "Koszt brutto mniejszy", CheckLevel.Warning, Details(("Koszt", 50m), ("KosztPoprzedni", 60m))),
            Row(4, 1, Kinds, CheckLevel.Warning, Details(("RodzajKosztu", "Hours"), ("Kwota", 10m))),
            Row(5, 1, "Import", CheckLevel.Warning, null, "plik.csv"),
            Row(6, 1, "Wiersze bez elementu WBS", CheckLevel.Pass, null),   // OK reguły – poza porównaniem
        ];
        RunIssueRow[] after =
        [
            Row(11, 2, Net, CheckLevel.Error, Details(("ElementWBS", "P-1"), ("Saldo", 100.004m), ("OstatniOkres", "2026/08"))),
            Row(12, 2, "Koszt brutto mniejszy", CheckLevel.Warning, Details(("Koszt", 70m), ("KosztPoprzedni", 60m))),
            Row(13, 2, Kinds, CheckLevel.Warning, Details(("RodzajKosztu", "Hours"), ("Kwota", 10m))),
            Row(14, 2, Kinds, CheckLevel.Warning, Details(("RodzajKosztu", "Hours"), ("Kwota", 5m))),
            Row(15, 2, "Cost element spoza słownika", CheckLevel.Error, Details(("CostElement", "0051100000"), ("Kwota", 7m))),
            Row(16, 2, "Wiersze bez elementu WBS", CheckLevel.Warning, Details(("Wiersze", 3m))),
        ];

        var rows = RunOverview.Compare(before, after, "PAF");

        string Status(string check, string? key) => rows.Single(r => r.CheckName == check && r.Element == key).Status;
        Assert.Equal(RunCompareRow.Same, Status(Net, "P-1 · 2026/08"));                    // różnica < 0,01
        Assert.Equal(RunCompareRow.Resolved, Status(Net, "P-2 · 2026/08"));
        Assert.Equal(RunCompareRow.Resolved, Status("Import", "plik.csv"));
        Assert.Equal(RunCompareRow.Changed, Status(Kinds, "Hours"));                        // dwa wiersze zamiast jednego
        Assert.Equal(RunCompareRow.New, Status("Cost element spoza słownika", "0051100000"));
        Assert.Equal(RunCompareRow.New, Status("Wiersze bez elementu WBS", null));
        Assert.DoesNotContain(rows, r => r.LevelBefore == CheckLevel.Pass);
        var cost = rows.Single(r => r.CheckName == "Koszt brutto mniejszy");
        Assert.Equal(RunCompareRow.Changed, cost.Status);
        Assert.Equal("Koszt: 50,00 → 70,00 (+20,00)", cost.Changes.Replace(' ', ' '));
        Assert.Equal(("Cost element spoza słownika", CheckLevel.Error), (rows[0].CheckName, rows[0].Level!.Value));   // ERROR, nowy przed bez zmian
        Assert.Equal("opis 13 (+1)", rows.Single(r => r.CheckName == Kinds).Message);
        Assert.Equal(("Hours", 2L), (rows.Single(r => r.CheckName == Kinds).Issue.Changes!.Kind, rows.Single(r => r.CheckName == Kinds).Issue.Changes!.PinId!.Value));
    }

    [Fact]
    public void Show_changes_uses_wbs_element_cost_kind_or_cost_element_column_of_rule_row_else_whole_program()
    {
        RunIssueRow Issue(string check, string? details) => new(1, 77, check, CheckLevel.Warning, null, "x", null, 3, details);

        Assert.Equal(["A.1"], RunOverview.ChangesFor(Issue("Netowanie", Details(("ElementWBS", "A.1"), ("CostElement", "51105550"))), "PAF")!.WbsElements);
        Assert.Equal(new PzlEv.Shared.Utils.Data.AcwpChangesRequest("PAF", Kind: "Other", PinId: 77, Scope: "Spadek: Other"),
            RunOverview.ChangesFor(Issue("Spadek", Details(("RodzajKosztu", "Other"), ("Kwota", 5m))), "PAF"));
        Assert.Equal("99999999", RunOverview.ChangesFor(Issue("Spoza", Details(("CostElement", "99999999"))), "PAF")!.CostElement);
        var program = RunOverview.ChangesFor(Issue("WP bez MPK", Details(("WP", "WP-1"))), "PAF")!;
        Assert.Equal((null, null, null, (long?)77), (program.Kind, program.WbsElements, program.CostElement, program.PinId));
        Assert.Null(RunOverview.ChangesFor(Issue("Bez WBS", Details(("ElementWBS", null), ("Wiersze", 2m)))!, "PAF")!.WbsElements);   // NULL – cały program
        Assert.Null(RunOverview.ChangesFor(Issue("Import", null), "PAF"));
        Assert.NotNull(RunOverview.ChangesFor(Issue(RunService.Pinned, null), "PAF"));
    }
}
