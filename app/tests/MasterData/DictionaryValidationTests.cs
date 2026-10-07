using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.MasterData;

public class DictionaryValidationTests
{
    private static DictRow Row(params (string Column, string? Value)[] values) =>
        new(null, null, values.ToDictionary(v => v.Column, v => v.Value));

    private static List<Issue> Check(DictionarySpec spec, params DictRow[] rows)
    {
        var issues = new List<Issue>();
        var normalized = DictionaryValidator.Normalize(spec, rows, issues);
        issues.AddRange(DictionaryValidator.Validate(spec, normalized));
        return issues;
    }

    private static DictionarySpec Rates => GlobalDictionaries.Get(GlobalDictionaries.DepartmentRates);

    [Fact]
    public void Valid_rates_have_no_issues()
    {
        var issues = Check(Rates,
            Row(("MPK", " W30 "), ("Year", "2026"), ("Labor Rate", "125,50"), ("Overhead", "0")),
            Row(("MPK", "W40"), ("Year", "2026"), ("Labor Rate", "1 300,00"), ("Overhead", "12,5")));
        Assert.Empty(issues);
    }

    [Fact]
    public void Type_errors_and_missing_values_are_errors()
    {
        var issues = Check(Rates, Row(("MPK", "W30"), ("Year", "dwa tysiące"), ("Labor Rate", null), ("Overhead", "abc")));
        Assert.All(issues, i => Assert.Equal(CheckLevel.Error, i.Level));
        Assert.Contains(issues, i => i.Message.StartsWith("Year:"));
        Assert.Contains(issues, i => i.Message == "Labor Rate: pole wymagane");
        Assert.Contains(issues, i => i.Message.StartsWith("Overhead:"));
    }

    [Fact]
    public void Rate_rules_are_enforced()
    {
        var issues = Check(Rates, Row(("MPK", "W30"), ("Year", "2026"), ("Labor Rate", "0"), ("Overhead", "-1")));
        Assert.Contains(issues, i => i.Message.Contains("Labor Rate musi być większa od 0"));
        Assert.Contains(issues, i => i.Message.Contains("Overhead nie może być ujemny"));
    }

    [Fact]
    public void Rate_key_is_mpk_and_year_description_and_overhead_are_optional()
    {
        Assert.Equal(["MPK", "Year"], Rates.KeyColumns.Select(c => c.Name));

        var valid = Check(Rates,
            Row(("MPK", "4410"), ("Department", "Konstrukcja"), ("Year", "2026"), ("Labor Rate", "125,50"), ("Overhead", null)),
            Row(("MPK", "4410"), ("Department", null), ("Year", "2027"), ("Labor Rate", "130"), ("Overhead", null)));
        Assert.Empty(valid);

        var missing = Check(Rates, Row(("MPK", null), ("Department", "Konstrukcja"), ("Year", "2026"), ("Labor Rate", "1"), ("Overhead", null)));
        Assert.Contains(missing, i => i.Level == CheckLevel.Error && i.Message == "MPK: pole wymagane");

        var tooLong = Check(Rates, Row(("MPK", new string('1', 41)), ("Year", "2026"), ("Labor Rate", "1")));
        Assert.Contains(tooLong, i => i.Level == CheckLevel.Error && i.Message.Contains("dłuższy niż 40 znaków"));
    }

    [Fact]
    public void Duplicate_key_is_an_error_and_similar_key_a_warning()
    {
        var issues = Check(Rates,
            Row(("MPK", "W30"), ("Year", "2026"), ("Labor Rate", "1"), ("Overhead", "0")),
            Row(("MPK", "W30"), ("Year", "2026"), ("Labor Rate", "2"), ("Overhead", "0")),
            Row(("MPK", "w30 "), ("Year", "2027"), ("Labor Rate", "2"), ("Overhead", "0")));
        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message.StartsWith("Duplikat klucza W30 | 2026"));
        Assert.Contains(issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("'W30'") && i.Message.Contains("'w30'"));
    }

    [Fact]
    public void Cost_element_number_is_padded_and_missing_category_is_a_warning()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.CostCategory);
        var issues = new List<Issue>();
        var rows = DictionaryValidator.Normalize(spec,
            [Row(("Numer elementu kosztowego", "51105550"), ("Cost Category", "Direct Materials")),
             Row(("Numer elementu kosztowego", "9221X550"), ("Cost Category", null))], issues);
        issues.AddRange(DictionaryValidator.Validate(spec, rows));

        Assert.Equal("0051105550", rows[0]["Numer elementu kosztowego"]);
        Assert.Equal("9221X550", rows[1]["Numer elementu kosztowego"]);
        var issue = Assert.Single(issues);
        Assert.Equal(CheckLevel.Warning, issue.Level);
        Assert.Contains("bez Cost Category", issue.Message);
    }

    [Fact]
    public void Similar_category_values_are_a_warning()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.CostCategory);
        var issues = Check(spec,
            Row(("Numer elementu kosztowego", "1"), ("Cost Category", "Engineering labor")),
            Row(("Numer elementu kosztowego", "2"), ("Cost Category", "engineering  Labor")));
        Assert.Contains(issues, i => i.Level == CheckLevel.Warning && i.Element == "Cost Category");
    }

    [Fact]
    public void Fx_rules_are_enforced()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.FxRates);
        var issues = Check(spec, Row(("Waluta", "usd"), ("Okres", "2026-13"), ("Kurs", "-4")));
        Assert.Contains(issues, i => i.Message.Contains("Waluta 'usd'"));
        Assert.Contains(issues, i => i.Message.Contains("Okres '2026-13'"));
        Assert.Contains(issues, i => i.Message.Contains("Kurs musi być większy od 0"));
    }

    [Fact]
    public void Calendar_rules_detect_overlap_order_and_closing_weeks()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.Calendar);
        var issues = Check(spec,
            Row(("Rok", "2026"), ("Tydzień", "40"), ("Okres", "2026-09"), ("Od", "2026-09-28"), ("Do", "2026-10-04"), ("Zamykający", "tak")),
            Row(("Rok", "2026"), ("Tydzień", "41"), ("Okres", "2026-09"), ("Od", "2026-10-03"), ("Do", "2026-10-09"), ("Zamykający", "tak")),
            Row(("Rok", "2026"), ("Tydzień", "42"), ("Okres", "2026-10"), ("Od", "2026-10-19"), ("Do", "2026-10-12"), ("Zamykający", "nie")));
        Assert.Contains(issues, i => i.Message.StartsWith("Tydzień nakłada się"));
        Assert.Contains(issues, i => i.Message == "Od jest późniejsze niż Do");
        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message.Contains("2 tygodnie zamykające"));
        Assert.Contains(issues, i => i.Level == CheckLevel.Warning && i.Element == "2026-10");
    }

    [Fact]
    public void Validity_periods_must_not_overlap()
    {
        var spec = new DictionarySpec
        {
            Code = "test",
            Name = "Test",
            Description = "",
            Columns = [new("Kod", ColumnType.Text, Key: true), new("Od", ColumnType.Date, Key: true), new("Do", ColumnType.Date)],
            Validity = ("Od", "Do"),
        };
        var issues = Check(spec,
            Row(("Kod", "A"), ("Od", "2026-01-01"), ("Do", "2026-06-30")),
            Row(("Kod", "A"), ("Od", "2026-06-01"), ("Do", null)),
            Row(("Kod", "B"), ("Od", "2026-05-01"), ("Do", "2026-04-01")));
        Assert.Contains(issues, i => i.Message.StartsWith("Okres obowiązywania nakłada się"));
        Assert.Contains(issues, i => i.Message == "Od jest późniejsze niż Do");
    }
}
