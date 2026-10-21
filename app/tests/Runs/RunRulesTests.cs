using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Reguły walidacji w SQL bez bazy: kontrola zapytania, złożenie z tabelami uruchomienia, opis wyniku, wiersz wyniku, reguły startowe.</summary>
public sealed class RunRulesTests
{
    [Theory]
    [InlineData("SELECT ElementWBS FROM biezace")]
    [InlineData("  -- komentarz\nWITH x AS (SELECT 1 AS a) SELECT a FROM x")]
    [InlineData("select N'DELETE; UPDATE' AS Tekst, [Set] FROM biezace /* INSERT */")]   // słowa w tekście, nazwie i komentarzu
    [InlineData("SELECT LastUpdate, Offset FROM biezace")]                                   // słowa kluczowe tylko jako całe słowa
    public void Check_accepts_single_select(string sql) => Assert.Null(RunRules.Check(sql));

    [Theory]
    [InlineData("", "Zapytanie SQL puste")]
    [InlineData("DELETE FROM biezace", "Zapytanie musi zaczynać się od SELECT albo WITH")]
    [InlineData("SELECT 1; DELETE FROM biezace", "Średnik niedozwolony")]
    [InlineData("SELECT 1 DELETE FROM biezace", "Niedozwolone w regule: DELETE")]
    [InlineData("SELECT * INTO kopia FROM biezace", "Niedozwolone w regule: INTO")]
    [InlineData("WITH x AS (SELECT 1 AS a) UPDATE x SET a = 2", "Niedozwolone w regule: UPDATE, SET")]
    [InlineData("SELECT 1 EXEC('DROP TABLE t')", "Niedozwolone w regule: EXEC")]
    [InlineData("SELECT 1 COMMIT", "Niedozwolone w regule: COMMIT")]
    [InlineData("SELECT * FROM OPENROWSET('x', 'y')", "Niedozwolone w regule: OPENROWSET")]
    [InlineData("SELECT * FROM sys.objects CROSS APPLY sp_who", "Procedury niedozwolone w regule: sp_who")]
    [InlineData("SELECT 'abc FROM biezace", "Niezamknięty tekst w apostrofach")]
    [InlineData("SELECT 1 /* koniec", "Niezamknięty komentarz")]
    public void Check_rejects_data_changes_more_statements_and_broken_text(string sql, string error) =>
        Assert.StartsWith(error, RunRules.Check(sql));

    [Fact]
    public void Compose_puts_rule_after_run_tables_and_joins_its_ctes_on_the_same_line()
    {
        const string Tables = "WITH biezace AS (\n  SELECT 1 AS Kwota)";

        var (plain, offset) = RunRules.Compose(Tables, "SELECT Kwota\nFROM biezace");
        var (merged, _) = RunRules.Compose(Tables, "-- c\nwith x AS (SELECT Kwota FROM biezace)\nSELECT * FROM x");

        Assert.Equal("WITH biezace AS (\n  SELECT 1 AS Kwota)\nSELECT Kwota\nFROM biezace", plain);
        Assert.Equal(2, offset);   // linia 1 zapytania reguły – linia 3 całości
        Assert.Equal("WITH biezace AS (\n  SELECT 1 AS Kwota)\n-- c\n,    x AS (SELECT Kwota FROM biezace)\nSELECT * FROM x", merged);
    }

    [Fact]
    public void Describe_fills_columns_formats_polish_values_and_without_template_lists_columns()
    {
        RunRuleColumn[] columns = [new("ElementWBS", false), new("Saldo", true), new("Wiersze", true), new("Utworzono", false), new("Rozliczeniowy", false)];
        object?[] row = ["A.1", 8884.512m, 1234, new DateTime(2026, 7, 1), true];

        var text = RunRules.Describe("{elementwbs}: saldo {Saldo}, wierszy {Wiersze}, {Utworzono}, {Rozliczeniowy}, {Brak}", columns, row).Replace(' ', ' ');
        var plain = RunRules.Describe(null, columns, ["A.1", null, 5, null, false]).Replace(' ', ' ');

        Assert.Equal("A.1: saldo 8 884,51, wierszy 1234, 2026-07-01, tak, {Brak}", text);
        Assert.Equal("ElementWBS: A.1; Saldo: (brak); Wiersze: 5; Utworzono: (brak); Rozliczeniowy: nie", plain);
        Assert.Equal(["Brak"], RunRules.UnknownPlaceholders("{ElementWBS} {Brak} {brak}", columns));
    }

    [Fact]
    public void Details_round_trip_keeps_numbers_and_texts_and_element_is_first_element_column()
    {
        RunRuleColumn[] columns = [new("Saldo", true), new("CostElement", false), new("ElementWBS", false), new("Okres", false)];
        object?[] row = [-15.5m, "51105550", null, "2026/03"];

        var details = RunRules.ReadDetails(RunRules.Details(columns, row));

        Assert.Equal([("Saldo", null, -15.5m), ("CostElement", "51105550", null), ("ElementWBS", null, null), ("Okres", "2026/03", null)],
            details.Select(d => (d.Column, d.Text, d.Number)));
        Assert.Equal(("CostElement", "51105550"), RunRules.ElementOf(columns, row));   // ElementWBS pusty
        Assert.Null(RunRules.ElementOf([new("Saldo", true)], [1m]));
        Assert.Empty(RunRules.ReadDetails("nie json"));
    }

    [Fact]
    public void Rule_result_rows_become_issues_with_rule_level_ok_without_rows_and_error_when_not_computed()
    {
        var rule = new RunRule("Ujemny koszt", CheckLevel.Warning, true, "SELECT 1", "{ElementWBS}: {Saldo}");
        RunRuleColumn[] columns = [new("ElementWBS", false), new("Saldo", true)];

        var found = RunService.Issues(rule, new RunRuleResult(columns, [["A.3", -15m], [null, -2m]]));
        var ok = Assert.Single(RunService.Issues(rule, new RunRuleResult(columns, [])));
        var failed = Assert.Single(RunService.Issues(rule, RunRuleResult.Failed("linia 2: Invalid column name 'X'.")));
        var truncated = RunService.Issues(rule with { Level = CheckLevel.Pass }, new RunRuleResult(columns, [["A.1", 1m]], Truncated: true));

        Assert.Equal([("Ujemny koszt", CheckLevel.Warning, "A.3", "A.3: -15,00"), ("Ujemny koszt", CheckLevel.Warning, null, "(brak): -2,00")],
            found.Select(i => (i.Check, i.Level, i.Element, i.Message)));
        Assert.All(found, i => Assert.NotNull(i.Details));
        Assert.Equal((CheckLevel.Pass, (string?)null), (ok.Level, ok.Details));
        Assert.Equal(CheckLevel.Error, failed.Level);
        Assert.Contains("Reguła niepoliczona: linia 2: Invalid column name 'X'.", failed.Message);
        Assert.Equal([CheckLevel.Pass, CheckLevel.Warning], truncated.Select(i => i.Level));   // obcięty wynik – ostrzeżenie
    }

    [Fact]
    public void Starter_rules_are_read_only_queries_with_unique_names_using_run_tables()
    {
        Assert.All(RunRules.Starters, r => Assert.Null(RunRules.Check(r.Sql)));
        Assert.Equal(RunRules.Starters.Count, RunRules.Starters.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal((13, 8), (RunRules.ActualsStarters.Count, RunRules.EvStarters.Count));
        Assert.All(RunRules.ActualsStarters, r => Assert.DoesNotContain("ev", RunRules.TablesUsed(r.Sql)));   // ACTUALS – cała pula
        Assert.All(RunRules.EvStarters, r => Assert.DoesNotContain("biezace", RunRules.TablesUsed(r.Sql)));
        Assert.Equal(["biezace", "cost_category"], RunRules.TablesUsed(RunRules.Starter("Cost element spoza słownika Cost Category")!.Sql));
    }

    [Fact]
    public void Rule_rows_round_trip_and_dictionary_rejects_data_changing_query()
    {
        var rule = RunRules.Starter("Wiersze bez elementu WBS")! with { Level = CheckLevel.Pass, IsActive = false };
        var spec = GlobalDictionaries.Get(GlobalDictionaries.RunValidations);

        Assert.Equal(rule, RunRuleRows.Rule(new DictRow(null, null, RunRuleRows.Values(rule))));
        var issues = DictionaryValidator.Validate(spec, [new DictRow(null, null, RunRuleRows.Values(rule)),
            new DictRow(null, null, RunRuleRows.Values(rule with { Name = "Zła", Sql = "UPDATE x SET a = 1" }))]);
        Assert.Equal("Zapytanie SQL: Zapytanie musi zaczynać się od SELECT albo WITH", Assert.Single(issues, i => i.Level == CheckLevel.Error).Message);
    }
}
