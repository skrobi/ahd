using Dapper;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>
/// Reguły startowe w SQL na bazie – te same nieprawidłowości co dawne reguły (formuły ACTUALS i reguły wbudowane EV) na tych samych
/// danych; okresy zamknięte przed 2026/09, bieżący 2026/10; bezpieczeństwo zapytań (odrzucenie, wycofanie, limity).
/// </summary>
public sealed class RunRulesSqlTests : IDisposable
{
    private const long Current = 1;
    private const long Previous = 2;
    private const string Program = "PAF";

    private readonly TestServices _services = new();
    private TestDatabase? _database;

    public void Dispose() => _database?.Dispose();

    private static RunBalanceRow Row(string wbs, string ce, int period, decimal amount, bool settlement = false, int rows = 1, string currency = "PLN",
        DateTime? created = null, int year = 2026) =>
        new(wbs, ce, year, period, settlement, currency, rows, amount, created ?? new DateTime(year, Math.Min(period, 12), 28));

    private SqlRunStore Store()
    {
        _database = new TestDatabase();
        return new SqlRunStore(_database.Sql);
    }

    private void Balance(long pin, IEnumerable<RunBalanceRow> rows)
    {
        using var connection = _database!.Sql.Open();
        connection.Execute(
            $"INSERT INTO {_database.Sql.Table(DbTables.RunBalance)} (PinId, WbsElement, CostElement, FiscalYear, Period, IsSettlement, Currency, Rows, Amount, LastCreatedOn) " +
            "VALUES (@pin, @WbsElement, @CostElement, @FiscalYear, @Period, @IsSettlement, @Currency, @Rows, @Amount, @LastCreatedOn)",
            rows.Select(r => new { pin, r.WbsElement, r.CostElement, r.FiscalYear, r.Period, r.IsSettlement, r.Currency, r.Rows, r.Amount, r.LastCreatedOn }));
    }

    // Dane „ev” jak przy przypięciu (bez rozliczeniowych, rodzaj kosztu z Cost Category).
    private static void Ev(SqlRunStore store, long pin, IReadOnlyList<RunBalanceRow> rows, IReadOnlyDictionary<string, string> kinds) =>
        store.SaveEv(pin, RunService.Ev(rows, [], PzlEv.Shared.Utils.Process.ProjectProcess.Default, kinds).Rows);

    private void Dictionary(string table, string columns, string values, object? args = null)
    {
        using var connection = _database!.Sql.Open();
        connection.Execute(
            $"INSERT INTO {_database.Sql.Table(table)} (RowId, Version, RecordedAt, RecordedBy, {columns}) VALUES (NEXT VALUE FOR {_database.Sql.Table("meta.LogicalId")}, 1, SYSDATETIMEOFFSET(), 'test', {values})",
            args);
    }

    private void CostCategory(string costElement, string? category, string? kind = null, bool settlement = false) =>
        Dictionary("dict.CostCategory", "Project, CostElement, CostCategory, CostKind, IsSettlement", "NULL, @costElement, @category, @kind, @settlement",
            new { costElement, category, kind, settlement });

    private static RunRuleContext Context(bool previous, string? currency = "PLN") => new(Current, previous ? Previous : null, Program, (2026, 9), (2026, 10), currency, "WEWNETRZNY");

    private sealed record Found(string Rule, CheckLevel Level, string? Element, string Message, IReadOnlyList<(string Column, string? Text, decimal? Number)> Details);

    private static List<Found> Run(SqlRunStore store, IEnumerable<RunRule> rules, RunRuleContext context)
    {
        var found = new List<Found>();
        foreach (var rule in rules)
        {
            var result = store.RunRule(rule.Sql, context);
            Assert.True(result.Error is null, $"{rule.Name}: {result.Error}");
            found.AddRange(RunService.Issues(rule, result).Select(i => new Found(rule.Name, i.Level, i.Element, i.Message.Replace(' ', ' '), RunRules.ReadDetails(i.Details))));
        }
        return found;
    }

    private static List<Found> Of(List<Found> found, string rule) => found.Where(f => f.Rule == rule && f.Details.Count > 0).ToList();

    private static decimal? Number(Found found, string column) => found.Details.Single(d => d.Column == column).Number;

    private void ActualsDictionary()
    {
        CostCategory("51105550", "Materials");
        CostCategory("92212550", "Labor");
        CostCategory("91902551", null, settlement: true);
    }

    [SqlFact]
    public void Settled_program_and_first_run_give_no_findings()
    {
        var store = Store();
        ActualsDictionary();
        Balance(Current, [Row("A.1", "51105550", 3, 100m), Row("A.1", "91902551", 3, -100m, settlement: true), Row("A.1", "51105550", 10, 50m)]);

        var found = Run(store, RunRules.ActualsStarters, Context(previous: false));

        Assert.All(found, f => Assert.Equal((CheckLevel.Pass, 0), (f.Level, f.Details.Count)));   // każda reguła – OK
        Assert.Equal(RunRules.ActualsStarters.Count, found.Count);
    }

    [SqlFact]
    public void Not_netting_program_shows_elements_with_balance_and_reason_on_whole_pool()
    {
        var store = Store();
        ActualsDictionary();
        Balance(Current,
        [
            Row("A.1", "51105550", 3, 100m), Row("A.1", "91902551", 3, -60m, settlement: true),   // rozliczenie niepełne
            Row("A.2", "92212550", 4, 30m),                                                        // brak rozliczenia
            Row("A.3", "51105550", 5, 10m), Row("A.3", "91902551", 5, -25m, settlement: true),    // rozliczenie większe niż koszt
            Row("A.4", "51105550", 6, 5m, created: new DateTime(2026, 7, 3)),
            Row("A.4", "91902551", 6, -5m, settlement: true, created: new DateTime(2026, 7, 1)),  // koszt po rozliczeniu
        ]);

        var found = Run(store, RunRules.ActualsStarters, Context(previous: false));

        var net = Of(found, "Netowanie do 0 w okresach zamkniętych");
        Assert.Equal(["A.1", "A.2", "A.3"], net.Select(i => i.Element));   // od największego salda
        Assert.All(net, i => Assert.Equal(CheckLevel.Error, i.Level));
        Assert.Contains("rozliczenie niepełne", net[0].Message);
        Assert.Contains("brak rozliczenia", net[1].Message);
        Assert.Contains("rozliczenie większe niż koszt", net[2].Message);
        Assert.Contains("saldo programu 55,00", net[0].Message);
        var late = Assert.Single(Of(found, "Koszt utworzony po rozliczeniu"));
        Assert.Equal(("A.4", CheckLevel.Warning), (late.Element, late.Level));
        Assert.Contains("po rozliczeniu z 2026-07-01", late.Message);
        var negative = Assert.Single(Of(found, "Ujemny koszt elementu WBS"));
        Assert.Equal(("A.3", -15m), (negative.Element, Number(negative, "Saldo")!.Value));   // cała pula – rozliczenie większe niż koszt
    }

    [SqlFact]
    public void Comparison_with_previous_run_finds_less_data_deviations_new_cost_elements_and_changed_closed_periods()
    {
        var store = Store();
        ActualsDictionary();
        Balance(Previous, [Row("A.1", "51105550", 3, 100m, rows: 10), Row("A.2", "92212550", 4, 40m, rows: 5)]);
        Balance(Current,
        [
            Row("A.1", "51105550", 3, 70m, rows: 3),                       // zamknięty okres zmieniony, mniej wierszy
            Row("A.2", "92212550", 4, 40m, rows: 5),
            Row("A.5", "99999999", 10, 7m),                                 // cost element spoza słownika
            Row("A.2", "91902551", 10, -1m, settlement: true),              // pierwszy raz w programie
            Row("A.6", "51105550", 12, 3m, currency: "USD"),                // okres przyszły, inna waluta
            Row("", "51105550", 9, 2m),                                     // bez elementu WBS
        ]);

        var found = Run(store, RunRules.ActualsStarters, Context(previous: true));
        var first = Run(store, RunRules.ActualsStarters, Context(previous: false));

        Assert.StartsWith("Mniej wierszy danych niż w poprzednim przebiegu: 12 < 15", Assert.Single(Of(found, "Mniej wierszy niż w poprzednim przebiegu")).Message);
        Assert.Equal(-18m, Number(Assert.Single(Of(found, "Koszt brutto mniejszy niż w poprzednim przebiegu")), "Zmiana"));   // koszt brutto 122 < 140
        Assert.Equal(["A.1", "A.5", "A.6", null, "A.2"], Of(found, "Największe odchylenia raport do raportu").Select(i => i.Element));
        Assert.Contains(Of(found, "Największe odchylenia raport do raportu – cost element"), i => i.Message.StartsWith("Cost element 51105550: zmiana -25,00"));
        Assert.Equal(("99999999", CheckLevel.Error), Of(found, "Cost element spoza słownika Cost Category").Select(i => (i.Element, i.Level)).Single());
        Assert.Equal(("91902551", CheckLevel.Pass), Of(found, "Cost element pierwszy raz w programie").Select(i => (i.Element, i.Level)).Single());
        var closed = Assert.Single(Of(found, "Zmiana salda okresu zamkniętego"));
        Assert.Equal(("A.1", -30m), (closed.Element, Number(closed, "Zmiana")!.Value));
        Assert.StartsWith("Wiersze w okresach przyszłych: 1 (2026/12 – 2026/12)", Assert.Single(Of(found, "Wiersze w okresach przyszłych")).Message);
        Assert.StartsWith("Waluta inna niż PLN: USD (1 wierszy)", Assert.Single(Of(found, "Waluta inna niż waluta kosztów")).Message);
        Assert.StartsWith("Wiersze bez elementu WBS: 1", Assert.Single(Of(found, "Wiersze bez elementu WBS")).Message);
        // Pierwszy przebieg (bez poprzedniego) – bez porównań.
        Assert.Empty(Of(first, "Mniej wierszy niż w poprzednim przebiegu").Concat(Of(first, "Największe odchylenia raport do raportu"))
            .Concat(Of(first, "Cost element pierwszy raz w programie")).Concat(Of(first, "Zmiana salda okresu zamkniętego")));
        // Waluta kosztów przeważająca w danych (inne pole kwoty): USD – waluta inna to PLN.
        Assert.StartsWith("Waluta inna niż USD: PLN", Assert.Single(Of(Run(store, [RunRules.Starter("Waluta inna niż waluta kosztów")!], Context(true, "USD")),
            "Waluta inna niż waluta kosztów")).Message);
    }

    [SqlFact]
    public void Dictionary_consistency_rules_find_cost_element_without_category_cost_center_without_rate_and_wp_without_budget()
    {
        var store = Store();
        CostCategory("51105550", "Materials", "Materials");
        CostCategory("92212550", null, "Hours");
        CostCategory("91902551", null, settlement: true);
        Dictionary("dict.WpCam", "Project, P1sElement, Wp, Mpk", "@Program, 'P1', 'WP-1', 'PZW20'", new { Program });
        Dictionary("dict.WpCam", "Project, P1sElement, Wp, Mpk", "@Program, 'P2', 'WP-2', 'PZW30'", new { Program });
        Dictionary("dict.WpCam", "Project, P1sElement, Wp, Mpk", "@Program, 'P3', 'WP-3', NULL", new { Program });
        Dictionary("dict.DepartmentRate", "Project, CostCenter, Year, LaborRate", "NULL, 'PZW20', 2026, 100");
        Dictionary("dict.DepartmentRate", "Project, CostCenter, Year, LaborRate", "NULL, 'PZW30', 2025, 100");   // inny rok
        Dictionary("dict.ScheduleBudget", "Project, Wp, Bac", "@Program, 'WP-1', 1000", new { Program });
        Dictionary("dict.ScheduleBudget", "Project, Wp, Bac, BacHours", "@Program, 'WP-2', 0, NULL", new { Program });
        Dictionary("dict.ScheduleBudget", "Project, Wp, BacHours", "@Program, 'WP-3', 10", new { Program });
        RunBalanceRow[] rows =
        [
            Row("A.1", "51105550", 3, 100m), Row("A.2", "92212550", 4, 30m), Row("A.2", "92212550", 5, 12m),
            Row("A.1", "91902551", 3, -100m, settlement: true),   // rozliczeniowy bez kategorii – poza ev
            Row("A.3", "99999999", 4, 5m),                         // spoza słownika – reguła „spoza słownika”, nie „bez kategorii”
        ];
        Balance(Current, rows);
        Ev(store, Current, rows, new Dictionary<string, string>());

        var found = Run(store, RunRules.EvStarters, Context(previous: false));

        var category = Assert.Single(Of(found, "Cost element bez Cost Category"));
        Assert.Equal(("92212550", CheckLevel.Warning, 42m), (category.Element, category.Level, Number(category, "Kwota")!.Value));
        var rate = Assert.Single(Of(found, "MPK bez stawki wydziału na rok"));
        Assert.Equal(("PZW30", CheckLevel.Error), (rate.Element, rate.Level));
        Assert.Contains("na rok 2026", rate.Message);
        Assert.EndsWith("WP (1): WP-2", rate.Message);
        Assert.Equal(("WP-2", CheckLevel.Warning), Of(found, "WP bez budżetu").Select(i => (i.Element, i.Level)).Single());
        Assert.Equal(("WP-3", CheckLevel.Warning), Of(found, "WP bez MPK").Select(i => (i.Element, i.Level)).Single());
    }

    [SqlFact]
    public void Cost_kind_rules_find_new_kind_jump_over_average_decrease_and_closed_period_change()
    {
        var store = Store();
        var kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["51105550"] = "Materials", ["92212550"] = "Hours" };
        // Materials: 6 okresów przed 2026/10 po 10 (średnio 10), w 2026/10 – 31 > 3 × 10; Other – nowy; Hours – spadek i zmiana w zamkniętym 2026/05.
        RunBalanceRow[] previous = [.. Enumerable.Range(4, 6).Select(p => Row("A.1", "51105550", p, 10m)), Row("A.2", "92212550", 5, 100m)];
        RunBalanceRow[] current = [.. previous.Where(r => r.CostElement == "51105550"), Row("A.1", "51105550", 10, 31m), Row("A.2", "92212550", 5, 60m),
            Row("A.3", "99999999", 10, 5m), Row("A.1", "91902551", 10, -500m, settlement: true)];
        Ev(store, Previous, previous, kinds);
        Ev(store, Current, current, kinds);

        var found = Run(store, RunRules.EvStarters, Context(previous: true));

        var jump = Assert.Single(Of(found, "Skok przyrostu kosztu"));
        Assert.Equal(("Materials", 31m, 10m), (jump.Element, Number(jump, "Przyrost")!.Value, Number(jump, "Srednia")!.Value));
        Assert.Contains("w okresie 2026/10: 31,00 – 3,10 × średni przyrost 10,00", jump.Message);
        var newKind = Assert.Single(Of(found, "Nowy rodzaj kosztu w programie"));
        Assert.Equal(("Other", 5m), (newKind.Element, Number(newKind, "Kwota")!.Value));
        var decrease = Assert.Single(Of(found, "Spadek kosztu narastająco w rodzaju"));
        Assert.Equal(("Hours", -40m), (decrease.Element, Number(decrease, "Zmiana")!.Value));
        var closed = Assert.Single(Of(found, "Zmiana rodzaju kosztu w zamkniętych okresach"));
        Assert.Equal(("Hours", -40m), (closed.Element, Number(closed, "Zmiana")!.Value));
        Assert.DoesNotContain(found, i => i.Element == "Materials" && i.Rule != "Skok przyrostu kosztu");   // rozliczenie poza rodzajami
        Assert.Empty(Of(Run(store, RunRules.EvStarters, Context(previous: false)), "Spadek kosztu narastająco w rodzaju"));   // bez poprzedniego
    }

    [SqlFact]
    public void Data_changing_query_is_rejected_data_stays_and_errors_limits_and_line_numbers_are_reported()
    {
        var store = Store();
        Balance(Current, [Row("A.1", "51105550", 3, 100m)]);
        int Count()
        {
            using var connection = _database!.Sql.Open();
            return connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {_database.Sql.Table(DbTables.RunBalance)}");
        }

        Assert.StartsWith("Zapytanie musi zaczynać się od SELECT", store.RunRule($"DELETE FROM {_database!.Sql.Table(DbTables.RunBalance)}", Context(false)).Error);
        Assert.StartsWith("Niedozwolone w regule: DELETE", store.RunRule($"SELECT 1 AS a DELETE FROM {_database.Sql.Table(DbTables.RunBalance)}", Context(false)).Error);
        Assert.StartsWith("Średnik", store.RunRule($"SELECT 1 AS a; TRUNCATE TABLE {_database.Sql.Table(DbTables.RunBalance)}", Context(false)).Error);
        Assert.Equal(1, Count());

        var error = store.RunRule("SELECT ElementWBS,\n       Brak\nFROM biezace", Context(false)).Error;
        Assert.StartsWith("linia 2: Invalid column name 'Brak'", error);
        var many = store.RunRule("SELECT TOP (600) a.object_id FROM sys.all_objects a CROSS JOIN sys.all_objects b", Context(false));
        Assert.Equal((RunRules.MaxRows, true, (string?)null), (many.Rows.Count, many.Truncated, many.Error));
        var own = store.RunRule("WITH moje AS (SELECT ElementWBS, Kwota FROM biezace)\nSELECT ElementWBS, Kwota * 2 AS Podwojona, @BiezacyOkres AS Okres, @JestPoprzedni AS Jest FROM moje",
            Context(false));
        Assert.Null(own.Error);
        Assert.Equal(["ElementWBS", "Podwojona", "Okres", "Jest"], own.Columns.Select(c => c.Name));
        Assert.Equal([false, true, false, true], own.Columns.Select(c => c.IsNumber));
        Assert.Equal(["A.1", 200m, "2026/10", 0], own.Rows.Single().Select(v => v is decimal d ? Math.Round(d, 2) : v));
        Assert.StartsWith("Przekroczony limit czasu", store.RunRule("SELECT COUNT_BIG(*) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c CROSS JOIN sys.all_objects d",
            Context(false)).Error);
    }
}
