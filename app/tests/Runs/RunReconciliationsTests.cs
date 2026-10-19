using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Reguły uzgodnień: formuły z miar (RunFormula), miary programu i sprawdzenie reguł słownika (RunReconciliations).</summary>
public sealed class RunReconciliationsTests
{
    private static RunBalanceRow Row(string wbs, string ce, int period, decimal amount, bool settlement = false, int rows = 1) =>
        new(wbs, ce, 2026, period, settlement, "PLN", rows, amount, null);

    private static decimal? Eval(string text, Func<string, decimal?> measure)
    {
        var (formula, error) = RunFormula.Parse(text);
        Assert.Null(error);
        return formula!.Evaluate(measure).Value;
    }

    [Fact]
    public void Formula_has_precedence_parentheses_unary_minus_decimal_comma_and_measures()
    {
        decimal? Measure(string name) => name == "BAC" ? 200m : name == "Koszt brutto" ? 150m : null;

        Assert.Equal(14m, Eval("2 + 3 * 4", Measure));
        Assert.Equal(20m, Eval("(2 + 3) * 4", Measure));
        Assert.Equal(220m, Eval("[BAC] * 1,1", Measure));
        Assert.Equal(-50m, Eval("-([BAC] - [koszt brutto])", Measure));   // nazwa bez rozróżniania wielkości liter
        Assert.Equal(0.75m, Eval("[Koszt brutto] / [BAC]", Measure));
        Assert.Equal(["BAC", "Koszt brutto"], RunFormula.Parse("[BAC] - [Koszt brutto] + [BAC]").Formula!.Measures);
    }

    [Fact]
    public void Formula_errors_name_the_problem()
    {
        Assert.StartsWith("nieznana miara [Budżet]", RunFormula.Parse("[Budżet] * 2").Error);
        Assert.Equal("brak nawiasu zamykającego „)”", RunFormula.Parse("(1 + 2").Error);
        Assert.StartsWith("nieoczekiwany znak „x”", RunFormula.Parse("1 + x").Error);
        Assert.StartsWith("formuła urwana", RunFormula.Parse("1 +").Error);
        Assert.Equal("pusta formuła", RunFormula.Parse(" ").Error);
        var division = RunFormula.Parse("[BAC] / [Wiersze]").Formula!;
        Assert.Equal("dzielenie przez 0", division.Evaluate(n => n == "BAC" ? 1m : 0m).Error);
        Assert.Equal("brak wartości miary [Wiersze]", division.Evaluate(n => n == "BAC" ? 1m : null).Error);
    }

    [Fact]
    public void Measures_come_from_run_data_previous_run_and_dictionaries()
    {
        RunBalanceRow[] current = [Row("A.1", "51105550", 3, 100m, rows: 2), Row("A.1", "91902551", 3, -60m, settlement: true), Row("", "51105550", 10, 5m)];
        RunBalanceRow[] previous = [Row("A.1", "51105550", 3, 80m, rows: 4)];
        var dictionaries = new RunDictionaryData(new Dictionary<string, string?>(), [new("WP-1", "M", true), new("WP-2", "M", false), new("WP-2", null, false)],
            new HashSet<string>(), 2026, new RunBudgetTotals(1000m, 50m, 300m));

        var m = RunReconciliations.Measures(current, null, previous, (2026, 9), (2026, 10), dictionaries);
        var first = RunReconciliations.Measures(current, null, null, (2026, 9), (2026, 10), null);

        Assert.Equal(RunMeasures.All.Select(x => x.Name).Order(), m.Keys.Order());
        Assert.Equal(105m, m["Koszt brutto"]);
        Assert.Equal(-60m, m["Rozliczenie"]);
        Assert.Equal(40m, m["Saldo okresów zamkniętych"]);
        Assert.Equal(5m, m["Koszt bieżącego okresu"]);
        Assert.Equal((80m, 4m, 4m), (m["Koszt brutto poprzedniego przebiegu"], m["Wiersze"], m["Wiersze poprzedniego przebiegu"]));
        Assert.Equal(5m, m["Koszt bez elementu WBS"]);
        Assert.Equal((1000m, 50m, 300m, 2m, 1m), (m["BAC"], m["BAC HOURS"], m["BAC MATERIAL"], m["Liczba WP"], m["Liczba WP bez budżetu"]));
        Assert.Equal((105m, 0m, 5m), (m["ACWP Other"], m["ACWP Hours"], m["Przyrost okresu Other"]));   // bez rodzajów w słowniku – Other
        Assert.Equal(0m, m["Średni przyrost Other"]);   // koszt z 2026/03 – poza 6 okresami przed 2026/10
        Assert.Null(first["Koszt brutto poprzedniego przebiegu"]);
        Assert.Null(first["BAC"]);
    }

    private static RunReconciliationRow Rule(string name, string left, string comparison, string right, decimal? tolerance = null, string level = "ERROR",
        string type = RunCheckRules.AllTypes, string run = RunCheckRules.AnyRun, bool active = true) =>
        new(type, name, run, active, left, comparison, right, tolerance, level);

    [Fact]
    public void Rules_of_program_type_and_run_variant_are_checked_with_tolerance()
    {
        var measures = new Dictionary<string, decimal?> { ["Koszt brutto"] = 1150m, ["BAC"] = 1000m, ["Saldo okresów zamkniętych"] = 0.5m, ["Koszt brutto poprzedniego przebiegu"] = null };
        RunReconciliationRow[] rules =
        [
            Rule("Przekroczenie budżetu", "[Koszt brutto]", "<=", "[BAC] * 1,1", level: "WARNING"),
            Rule("Netowanie", "[Saldo okresów zamkniętych]", "=", "0", tolerance: 1m),
            Rule("Wzrost kosztu", "[Koszt brutto]", ">=", "[Koszt brutto poprzedniego przebiegu]"),
            Rule("Tylko CAS", "[BAC]", "=", "0", type: "CAS"),
            Rule("Tylko zamykający", "[BAC]", "=", "0", run: RunCheckRules.ClosingRun),
            Rule("Wyłączona", "[BAC]", "=", "0", active: false),
        ];

        var issues = RunReconciliations.Check(rules, "SAC", isClosing: false, measures);

        Assert.Equal(["Netowanie", "Przekroczenie budżetu", "Wzrost kosztu"], issues.Select(i => i.Element));
        Assert.All(issues, i => Assert.Equal(RunReconciliations.CheckName, i.Check));
        Assert.Equal(CheckLevel.Pass, issues[0].Level);
        var budget = issues[1];
        Assert.Equal((CheckLevel.Warning, 50m), (budget.Level, budget.Amount));
        Assert.Contains("niespełniona – [Koszt brutto] = 1 150, [BAC] * 1,1 = 1 100, warunek <=", budget.Message.Replace('\u00A0', ' ').Replace('\u202F', ' '));
        Assert.Equal(CheckLevel.Warning, issues[2].Level);
        Assert.Contains("reguła niesprawdzona – brak wartości miary [Koszt brutto poprzedniego przebiegu]", issues[2].Message);
        Assert.Contains(RunReconciliations.Check(rules, "CAS", isClosing: true, measures), i => i.Element == "Tylko CAS" && i.Level == CheckLevel.Error);
        Assert.True(RunMeasures.Holds("<>", 1m, 3m, 1m));
        Assert.False(RunMeasures.Holds("<>", 1m, 1.5m, 1m));
        Assert.True(RunMeasures.Holds(">=", 9.5m, 10m, 1m));
    }

    [Fact]
    public void Dictionary_rejects_wrong_formula_and_negative_tolerance()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.RunReconciliations);
        DictRow Row(string name, string left, string right, string? tolerance) => new(null, null, new Dictionary<string, string?>
        {
            ["Typ programu"] = RunCheckRules.AllTypes, ["Nazwa"] = name, ["Przebieg"] = RunCheckRules.AnyRun, ["Aktywna"] = "tak",
            ["Lewa strona"] = left, ["Warunek"] = "<=", ["Prawa strona"] = right, ["Tolerancja"] = tolerance, ["Poziom"] = "ERROR", ["Opis"] = null,
        });

        var issues = DictionaryValidator.Validate(spec,
            [Row("A", "[Koszt brutto]", "[BAC] * 1.1", "0"), Row("B", "[Budżet]", "1", null), Row("C", "[BAC]", "(1", "-1")]);

        Assert.Equal(3, issues.Count(i => i.Level == CheckLevel.Error));
        Assert.Contains(issues, i => i.Message.StartsWith("Lewa strona: nieznana miara [Budżet]"));
        Assert.Contains(issues, i => i.Message.StartsWith("Prawa strona: brak nawiasu"));
        Assert.Contains(issues, i => i.Message == "Tolerancja nie może być ujemna");
    }
}
