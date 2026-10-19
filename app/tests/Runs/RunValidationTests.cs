using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Reguły walidacji ACTUALS zapisane formułami (RunValidationLanguage, reguły startowe) – okresy zamknięte przed 2026/09, bieżący 2026/10.</summary>
public sealed class RunValidationTests
{
    private static readonly (int, int) ClosedBefore = (2026, 9);
    private static readonly (int, int) Today = (2026, 10);
    private static readonly HashSet<string> Dictionary = new(StringComparer.OrdinalIgnoreCase) { "51105550", "92212550", "91902551" };

    private static RunBalanceRow Row(string wbs, string ce, int period, decimal amount, bool settlement = false, int rows = 1, string currency = "PLN",
        DateTime? created = null, int year = 2026) =>
        new(wbs, ce, year, period, settlement, currency, rows, amount, created ?? new DateTime(year, Math.Min(period, 12), 28));

    private sealed record Found(string Rule, string Check, CheckLevel Level, string? Element, string Message, decimal? Amount);

    private static List<Found> Run(IReadOnlyList<RunBalanceRow> current, IReadOnlyList<RunBalanceRow>? previous, string? currency = "PLN")
    {
        var data = new RunValidationData(current, previous, Dictionary, ClosedBefore, Today, currency);
        var found = new List<Found>();
        foreach (var rule in RunValidationRules.Starters)
        {
            var (results, error) = RunValidationLanguage.Evaluate(rule, data);
            Assert.Null(error);
            found.AddRange(results.Select(r => new Found(rule.Name, rule.Check, r.Level, r.Element, r.Message, r.Amount)));
        }
        return found;
    }

    private static List<Found> Of(List<Found> found, string check, CheckLevel? level = null) =>
        found.Where(f => f.Check == check && (level is null || f.Level == level)).ToList();

    [Fact]
    public void Starter_rules_have_valid_syntax_unique_names_and_cover_actuals_checks()
    {
        Assert.All(RunValidationRules.Starters, r => Assert.Empty(RunValidationLanguage.Validate(r)));
        Assert.Equal(RunValidationRules.Starters.Count, RunValidationRules.Starters.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(RunValidationRules.Starters, r => RunCheckRules.ByName(r.Name) is not null);
    }

    [Fact]
    public void Settled_program_nets_to_zero_and_first_run_has_nothing_to_compare()
    {
        RunBalanceRow[] current = [Row("A.1", "51105550", 3, 100m), Row("A.1", "91902551", 3, -100m, settlement: true), Row("A.1", "51105550", 10, 50m)];

        var found = Run(current, null);

        Assert.Equal(CheckLevel.Pass, Assert.Single(Of(found, RunCheckRules.NetZero)).Level);   // okres 10 otwarty – poza saldem
        Assert.Contains("Pierwszy przebieg", Assert.Single(Of(found, RunCheckRules.Volume)).Message);
        Assert.DoesNotContain(found, f => f.Level != CheckLevel.Pass);
    }

    [Fact]
    public void Not_netting_program_shows_elements_with_balance_and_reason_on_whole_pool()
    {
        RunBalanceRow[] current =
        [
            Row("A.1", "51105550", 3, 100m), Row("A.1", "91902551", 3, -60m, settlement: true),   // rozliczenie niepełne
            Row("A.2", "92212550", 4, 30m),                                                        // brak rozliczenia
            Row("A.3", "51105550", 5, 10m), Row("A.3", "91902551", 5, -25m, settlement: true),    // rozliczenie większe niż koszt
            Row("A.4", "51105550", 6, 5m, created: new DateTime(2026, 7, 3)),
            Row("A.4", "91902551", 6, -5m, settlement: true, created: new DateTime(2026, 7, 1)),  // koszt po rozliczeniu
        ];

        var found = Run(current, null);

        var net = Of(found, RunCheckRules.NetZero, CheckLevel.Error);
        Assert.Equal(["A.1", "A.2", "A.3"], net.Select(i => i.Element));   // od największego salda
        Assert.Contains("rozliczenie niepełne", net[0].Message);
        Assert.Contains("brak rozliczenia", net[1].Message);
        Assert.Contains("rozliczenie większe niż koszt", net[2].Message);
        Assert.Contains("saldo programu 55", net[0].Message);
        Assert.Contains("główne cost elementy: 51105550 100", net[0].Message);
        var late = Assert.Single(Of(found, RunCheckRules.NetZero, CheckLevel.Warning));
        Assert.Equal("A.4", late.Element);
        Assert.Contains("po rozliczeniu z 2026-07-01", late.Message);
        Assert.Equal(("A.3", -15m), Of(found, RunCheckRules.Quality).Select(f => (f.Element, f.Amount!.Value)).Single());   // cała pula – rozliczenie większe niż koszt
    }

    [Fact]
    public void Comparison_with_previous_run_finds_less_data_deviations_new_cost_elements_and_changed_closed_periods()
    {
        RunBalanceRow[] previous = [Row("A.1", "51105550", 3, 100m, rows: 10), Row("A.2", "92212550", 4, 40m, rows: 5)];
        RunBalanceRow[] current =
        [
            Row("A.1", "51105550", 3, 70m, rows: 3),                       // zamknięty okres zmieniony, mniej wierszy
            Row("A.2", "92212550", 4, 40m, rows: 5),
            Row("A.5", "99999999", 10, 7m),                                 // cost element spoza słownika
            Row("A.2", "91902551", 10, -1m, settlement: true),              // pierwszy raz w programie
            Row("A.6", "51105550", 12, 3m, currency: "USD"),                // okres przyszły, inna waluta
            Row("", "51105550", 9, 2m),                                     // bez elementu WBS
        ];

        var found = Run(current, previous);

        Assert.StartsWith("Mniej wierszy danych niż w poprzednim przebiegu: 12 < 15", Of(found, RunCheckRules.Volume, CheckLevel.Error).Single().Message);
        Assert.Equal(-30m + 7m + 3m + 2m, Of(found, RunCheckRules.Volume, CheckLevel.Warning).Single().Amount);   // koszt brutto 122 < 140
        Assert.Equal(["A.1", "A.5", "A.6", null, "A.2"], Of(found, RunCheckRules.Deviations).Where(i => i.Message.StartsWith("element WBS")).Select(i => i.Element));
        Assert.Contains(Of(found, RunCheckRules.Deviations), i => i.Message.StartsWith("cost element 51105550: zmiana -25"));
        Assert.Equal("99999999", Of(found, RunCheckRules.NewCostElements, CheckLevel.Error).Single().Element);
        Assert.Equal("91902551", Of(found, RunCheckRules.NewCostElements, CheckLevel.Pass).Single().Element);
        var closed = Of(found, RunCheckRules.ClosedPeriods).Single();
        Assert.Equal(("A.1", -30m), (closed.Element, closed.Amount));
        var quality = Of(found, RunCheckRules.Quality).Select(i => i.Message).ToList();
        Assert.Contains(quality, q => q.StartsWith("Wiersze w okresach przyszłych: 1 (2026/12)"));
        Assert.Contains(quality, q => q.StartsWith("Waluta inna niż PLN: USD"));
        Assert.Contains(quality, q => q.StartsWith("Wiersze bez elementu WBS: 1"));
    }

    [Fact]
    public void Threshold_and_level_of_template_override_the_rule()
    {
        var rule = RunValidationRules.Starter("Ujemny koszt elementu WBS")!;
        var data = new RunValidationData([Row("A.1", "51105550", 3, -5m)], null, Dictionary, ClosedBefore, Today, "PLN");

        Assert.Single(RunValidationLanguage.Evaluate(rule, data).Results);
        Assert.Empty(RunValidationLanguage.Evaluate(rule, data, threshold: 10m).Results);
    }

    private static RunValidationRule Rule(string condition, string message = "x", string? filter = null, string? groupBy = null, string? amount = null,
        string? ok = null, int top = 0) =>
        new("Test", "Test", true, filter, groupBy, condition, amount, message, ok, top, CheckLevel.Warning, 1m, null);

    [Fact]
    public void Syntax_errors_name_the_part_and_the_problem()
    {
        string Error(RunValidationRule rule) => string.Join(" | ", RunValidationLanguage.Validate(rule));

        Assert.Contains("Warunek: nieznane pole [Kwotaa]", Error(Rule("SUMA([Kwotaa]) > 0")));
        Assert.Contains("Warunek: pole [Kwota] poza grupowaniem", Error(Rule("[Kwota] > 0")));
        Assert.Contains("Filtr: agregat SUMA w filtrze", Error(Rule("LICZBA() > 0", filter: "SUMA([Kwota]) > 0")));
        Assert.Contains("Grupuj: „[Brak]”", Error(Rule("LICZBA() > 0", groupBy: "Kwota; [Brak]")));
        Assert.Contains("nieznana funkcja SUMMA", Error(Rule("SUMMA([Kwota]) > 0")));
        Assert.Contains("Warunek: brak „)”", Error(Rule("(SUMA([Kwota]) > 0")));
        Assert.Contains("Komunikat: {SUMA([Kwota]} – SUMA: brak „)”", Error(Rule("LICZBA() > 0", "Kwota {SUMA([Kwota]}")));
        Assert.Contains("agregat SUMA wewnątrz innego agregatu", Error(Rule("SUMA(SUMA([Kwota])) > 0")));
        Assert.Contains("Komunikat OK: pole [Element WBS] poza grupowaniem", Error(Rule("LICZBA() > 0", groupBy: "[Element WBS]", ok: "{[Element WBS]}")));
        Assert.Empty(RunValidationLanguage.Validate(Rule("SUMA([Kwota]) > [Próg]", "{[Element WBS]}: {SUMA([Kwota])}", groupBy: "[Element WBS]")));
    }

    [Fact]
    public void Language_groups_compares_with_previous_run_and_formats_message()
    {
        RunBalanceRow[] previous = [Row("A.1", "51105550", 3, 10m), Row("A.9", "51105550", 3, 4m)];
        RunBalanceRow[] current = [Row("A.1", "51105550", 3, 12.5m), Row("A.1", "92212550", 4, 1000m), Row("A.2", "51105550", 10, 1m)];
        var data = new RunValidationData(current, previous, Dictionary, ClosedBefore, Today, "PLN");
        var rule = Rule("ABS(SUMA([Kwota]) - POPRZ.SUMA([Kwota])) > 0", "{[Element WBS]}: {POPRZ.SUMA([Kwota])} → {SUMA([Kwota])}; okresy {LISTA([Rok/okres])}; "
            + "{NAJWIĘKSZE([Cost element]; [Kwota]; 1)}; {JEŻELI(CAŁOŚĆ.LICZBA() = 3; \"tak\" & \"!\"; \"nie\")}",
            groupBy: "[Element WBS]", amount: "SUMA([Kwota]) - POPRZ.SUMA([Kwota])", top: 2);

        var (results, error) = RunValidationLanguage.Evaluate(rule, data);

        Assert.Null(error);
        Assert.Equal(["A.1", "A.9"], results.Select(r => r.Element));   // najwyżej 2, od największej |kwoty|: A.1 1002,5, A.9 -4, A.2 1
        Assert.Equal("A.1: 10 → 1 012,5; okresy 2026/03, 2026/04; 92212550 1 000; tak!", results[0].Message.Replace('\u00a0', ' '));
        Assert.Equal(-4m, results[1].Amount);
        var ok = Rule("LICZBA() > 5", ok: "Pozycji {LICZBA()}, wierszy {SUMA([Wiersze])}, waluta {[Waluta kosztów]}, okres {[Bieżący okres]}");
        Assert.Equal((CheckLevel.Pass, "Pozycji 3, wierszy 3, waluta PLN, okres 2026/10"),
            RunValidationLanguage.Evaluate(ok, data).Results.Select(r => (r.Level, r.Message)).Single());
    }
}
