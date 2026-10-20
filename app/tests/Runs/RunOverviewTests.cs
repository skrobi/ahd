using PzlEv.Modules.Runs.Data;
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

    [Fact]
    public void Step_results_are_grouped_by_rule_worst_first_with_problem_from_column_or_result_description()
    {
        RunIssueRow Row(long id, string check, CheckLevel level, string message, string? details) => new(id, 1, check, level, null, message, null, 3, details);
        RunIssueRow[] issues =
        [
            Row(1, "Wiersze bez elementu WBS", CheckLevel.Warning, "Wiersze bez elementu WBS: 3", Details(("Wiersze", 3m))),
            Row(2, Net, CheckLevel.Error, "A.1: saldo 40,00 – rozliczenie niepełne", Details(("ElementWBS", "A.1"), ("Problem", "rozliczenie niepełne"), ("Wskazowka", "sprawdź regułę"), ("Saldo", 40m))),
            Row(3, Net, CheckLevel.Error, "A.2: saldo 30,00", Details(("ElementWBS", "A.2"), ("Problem", "brak rozliczenia"), ("Saldo", 30m), ("OstatniOkres", "2026/04"))),
            Row(4, "Mniej wierszy", CheckLevel.Pass, "OK – brak nieprawidłowości", null),
            Row(5, "Odchylenia", CheckLevel.Pass, "Element WBS A.1: zmiana", Details(("ElementWBS", "A.1"), ("Zmiana", -30m))),
        ];

        var results = RunOverview.RuleResults(issues);
        var table = RunOverview.ResultTable(results[0].Issues);

        Assert.Equal([(Net, "ERROR", 2), ("Wiersze bez elementu WBS", "WARNING", 1), ("Odchylenia", "INFO", 1), ("Mniej wierszy", "OK", 0)],
            results.Select(r => (r.Name, r.LevelText, r.Problems)));
        Assert.Equal(["ElementWBS", "Saldo", "OstatniOkres"], table.Columns);   // Problem i Wskazowka – osobno
        Assert.Equal(("rozliczenie niepełne", "sprawdź regułę"), (table.Rows[0].Problem, table.Rows[0].Hint));
        Assert.Equal(("brak rozliczenia", "A.2: saldo 30,00"), (table.Rows[1].Problem, table.Rows[1].Hint));   // bez wskazówki – opis wyniku
        Assert.Equal(["A.2", "30,00", "2026/04"], table.Rows[1].Values);
        Assert.Equal(["Saldo"], table.Numeric!);                                 // kwoty – do prawej
        Assert.Null(table.CommonHint);                                            // wskazówki różne – zaznaczonego wiersza
        var plain = RunOverview.ResultTable(results[1].Issues).Rows.Single();
        Assert.Equal(("Wiersze bez elementu WBS: 3", (string?)null), (plain.Problem, plain.Hint));   // bez kolumny Problem – opis wyniku
        Assert.Equal("OK – brak nieprawidłowości", RunOverview.ResultTable(results[3].Issues).Rows.Single().Problem);
    }

    [Fact]
    public void Actuals_scope_of_result_row_comes_from_wbs_cost_element_cost_kind_and_period_columns()
    {
        RunIssueRow Issue(string? details) => new(1, 77, "R", CheckLevel.Warning, null, "x", null, 3, details);
        IReadOnlyCollection<string> Kind(string kind) => kind == "Materials" ? ["51105550", "51105560"] : [];

        var wbs = RunOverview.ActualsScope(Issue(Details(("ElementWBS", "A.1"), ("CostElement", "51105550"), ("RokOkres", "2026/03"), ("Saldo", 5m))), Kind)!.Value;
        var kind = RunOverview.ActualsScope(Issue(Details(("RodzajKosztu", "Materials"), ("Okres", "2026/10"), ("Przyrost", 1m))), Kind)!.Value;
        var numbers = RunOverview.ActualsScope(Issue(Details(("CostElement", "92212550"), ("Rok", 2026m), ("Okres", 14m))), Kind)!.Value;

        Assert.Equal((77L, "A.1", 2026, 3), (wbs.Scope.PinId, wbs.Scope.WbsElement, wbs.Scope.Year!.Value, wbs.Scope.Period!.Value));
        Assert.Equal(["51105550"], wbs.Scope.CostElements);
        Assert.Equal("ElementWBS = A.1 · CostElement = 51105550 · okres 2026/03", wbs.Description);
        Assert.Equal((null, 2026, 10), (kind.Scope.WbsElement, kind.Scope.Year, kind.Scope.Period));
        Assert.Equal(["51105550", "51105560"], kind.Scope.CostElements);
        Assert.Equal((2026, 12), (numbers.Scope.Year!.Value, numbers.Scope.Period!.Value));   // okres 13–16 jak 12
        Assert.Null(RunOverview.ActualsScope(Issue(Details(("WP", "WP-1"))), Kind));
        Assert.Null(RunOverview.ActualsScope(Issue(Details(("ElementWBS", null), ("Wiersze", 2m))), Kind));
        Assert.Null(RunOverview.ActualsScope(Issue(null), Kind));
    }
}
