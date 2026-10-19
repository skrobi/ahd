namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Reguły walidacji ACTUALS (grupa „Walidacja ACTUALS” – docs/pipeline-fazy.md, rozdz. 4.1): reguły startowe zapisane formułami
/// (RunValidationLanguage) – cała pula ACTUALS programu przed narzutami CAS, bez Wykluczeń. Obowiązują, dopóki słownik „Reguły
/// walidacji” jest pusty (okno Kontrole – zapis tworzy wiersze do edycji).
/// </summary>
public static class RunValidationRules
{
    public const string Area = "Walidacja ACTUALS";
    public const int Top = 10;

    private const string Wbs = "{JEŻELI(PUSTE([Element WBS]); \"(bez elementu WBS)\"; [Element WBS])}";

    public static IReadOnlyList<RunValidationRule> Starters { get; } =
    [
        new("Mniej wierszy niż w poprzednim przebiegu", RunCheckRules.Volume, true, null, null,
            "[Jest poprzedni] ORAZ SUMA([Wiersze]) < POPRZ.SUMA([Wiersze])", "SUMA([Wiersze]) - POPRZ.SUMA([Wiersze])",
            "Mniej wierszy danych niż w poprzednim przebiegu: {SUMA([Wiersze])} < {POPRZ.SUMA([Wiersze])} – niepełny zrzut ACTUALS albo zmiana zakresu",
            "{JEŻELI([Jest poprzedni]; \"Wierszy danych \" & SUMA([Wiersze]) & \" (w poprzednim przebiegu \" & POPRZ.SUMA([Wiersze]) & \")\"; \"Pierwszy przebieg programu – brak danych do porównania (\" & SUMA([Wiersze]) & \" wierszy)\")}",
            0, CheckLevel.Error, null, "Zrzut ACTUALS jest narastający – mniej wierszy niż w poprzednim przebiegu to brak danych."),
        new("Koszt brutto mniejszy niż w poprzednim przebiegu", RunCheckRules.Volume, true, "NIE [Rozliczeniowy]", null,
            "[Jest poprzedni] ORAZ SUMA([Kwota]) < POPRZ.SUMA([Kwota]) - [Próg]", "SUMA([Kwota]) - POPRZ.SUMA([Kwota])",
            "Koszt brutto mniejszy niż w poprzednim przebiegu: {SUMA([Kwota])} < {POPRZ.SUMA([Kwota])}", null,
            0, CheckLevel.Warning, RunCheckRules.Tolerance, "Koszt brutto (wiersze bez cost elementów rozliczeniowych) mniejszy o więcej niż próg niż w poprzednim przebiegu."),
        new("Największe odchylenia raport do raportu", RunCheckRules.Deviations, true, null, "[Element WBS]",
            "[Jest poprzedni] ORAZ ABS(SUMA([Kwota]) - POPRZ.SUMA([Kwota])) >= [Próg]", "SUMA([Kwota]) - POPRZ.SUMA([Kwota])",
            "element WBS " + Wbs + ": zmiana {SUMA([Kwota]) - POPRZ.SUMA([Kwota])} ({POPRZ.SUMA([Kwota])} → {SUMA([Kwota])})", null,
            Top, CheckLevel.Pass, RunCheckRules.Tolerance, "Elementy WBS z największą zmianą salda (cała pula) od poprzedniego przebiegu; próg – najmniejsza zmiana."),
        new("Największe odchylenia raport do raportu – cost element", RunCheckRules.Deviations, true, null, "[Cost element]",
            "[Jest poprzedni] ORAZ ABS(SUMA([Kwota]) - POPRZ.SUMA([Kwota])) >= [Próg]", "SUMA([Kwota]) - POPRZ.SUMA([Kwota])",
            "cost element {[Cost element]}: zmiana {SUMA([Kwota]) - POPRZ.SUMA([Kwota])} ({POPRZ.SUMA([Kwota])} → {SUMA([Kwota])})", null,
            Top, CheckLevel.Pass, RunCheckRules.Tolerance, "Cost elementy z największą zmianą kwoty (cała pula) od poprzedniego przebiegu; próg – najmniejsza zmiana."),
        new("Cost element spoza słownika Cost Category", RunCheckRules.NewCostElements, true, "NIE PUSTE([Cost element]) ORAZ NIE [W Cost Category]", "[Cost element]",
            "LICZBA() > 0", "SUMA([Kwota])",
            "Cost element {[Cost element]} spoza słownika Cost Category – dodaj go (Słowniki → Cost Category → Uzupełnij z ACTUALS)", null,
            0, CheckLevel.Error, null, "Cost element z danych ACTUALS bez wpisu w słowniku Cost Category programu (globalny i zmiany programu)."),
        new("Cost element pierwszy raz w programie", RunCheckRules.NewCostElements, true, "NIE PUSTE([Cost element])", "[Cost element]",
            "[Jest poprzedni] ORAZ LICZBA() > 0 ORAZ POPRZ.LICZBA() = 0 ORAZ LICZBA([W Cost Category]) > 0", "SUMA([Kwota])",
            "Cost element {[Cost element]} pierwszy raz w programie", null,
            0, CheckLevel.Pass, null, "Cost element ze słownika, którego w poprzednim przebiegu nie było (spoza słownika – reguła wyżej)."),
        new("Netowanie do 0 w okresach zamkniętych", RunCheckRules.NetZero, true, "[Okres zamknięty]", "[Element WBS]",
            "ABS(CAŁOŚĆ.SUMA([Kwota])) > [Próg] ORAZ ABS(SUMA([Kwota])) > [Próg]", "SUMA([Kwota])",
            Wbs + ": saldo {SUMA([Kwota])} (koszt {SUMA([Kwota]; NIE [Rozliczeniowy])}, rozliczenie {SUMA([Kwota]; [Rozliczeniowy])}) – "
            + "{JEŻELI(SUMA([Kwota]; [Rozliczeniowy]) = 0; \"brak rozliczenia (reguła rozliczenia, element statystyczny albo brak cost elementu „Rozliczeniowy” w słowniku)\"; "
            + "JEŻELI(ABS(SUMA([Kwota]; [Rozliczeniowy])) > ABS(SUMA([Kwota]; NIE [Rozliczeniowy])); \"rozliczenie większe niż koszt (storno po rozliczeniu?)\"; \"rozliczenie niepełne\"))}; "
            + "ostatni okres z ruchem {MAKS([Rok/okres]; [Kwota] <> 0)}; główne cost elementy: {NAJWIĘKSZE([Cost element]; [Kwota]; 3; NIE [Rozliczeniowy])}; "
            + "saldo programu {CAŁOŚĆ.SUMA([Kwota])}",
            "Program netuje się do 0 w okresach zamkniętych (przed {[Pierwszy otwarty okres]}): saldo {SUMA([Kwota])}",
            Top, CheckLevel.Error, RunCheckRules.Tolerance, "Program nie netuje się do 0 w okresach zamkniętych – elementy WBS z saldem i przyczyna."),
        new("Koszt utworzony po rozliczeniu", RunCheckRules.NetZero, true, "[Okres zamknięty]", "[Element WBS]; [Rok/okres]",
            "ABS(CAŁOŚĆ.SUMA([Kwota])) > [Próg] ORAZ MAKS([Utworzono]; NIE [Rozliczeniowy]) > MAKS([Utworzono]; [Rozliczeniowy])", null,
            Wbs + " {[Rok/okres]}: koszt utworzony {MAKS([Utworzono]; NIE [Rozliczeniowy])}, po rozliczeniu z {MAKS([Utworzono]; [Rozliczeniowy])} – wymaga ponownego rozliczenia", null,
            Top, CheckLevel.Warning, RunCheckRules.Tolerance, "Gdy program nie netuje się do 0 (saldo > próg): koszt utworzony później niż ostatnie rozliczenie elementu w okresie."),
        new("Zmiana salda okresu zamkniętego", RunCheckRules.ClosedPeriods, true, "[Okres zamknięty]", "[Element WBS]; [Rok/okres]",
            "[Jest poprzedni] ORAZ ABS(SUMA([Kwota]) - POPRZ.SUMA([Kwota])) > [Próg]", "SUMA([Kwota]) - POPRZ.SUMA([Kwota])",
            Wbs + " {[Rok/okres]}: saldo okresu zamkniętego zmieniło się o {SUMA([Kwota]) - POPRZ.SUMA([Kwota])} od poprzedniego przebiegu", null,
            Top, CheckLevel.Warning, RunCheckRules.Tolerance, "Saldo elementu WBS w okresie zamkniętym inne niż w poprzednim przebiegu."),
        new("Ujemny koszt elementu WBS", RunCheckRules.Quality, true, null, "[Element WBS]",
            "SUMA([Kwota]) < -[Próg]", "SUMA([Kwota])",
            Wbs + ": ujemne saldo narastająco {SUMA([Kwota])} (koszt {SUMA([Kwota]; NIE [Rozliczeniowy])}, rozliczenie {SUMA([Kwota]; [Rozliczeniowy])}) – storno albo rozliczenie większe niż koszt?", null,
            Top, CheckLevel.Warning, RunCheckRules.Tolerance, "Element WBS z ujemnym saldem narastająco (cała pula: koszt i rozliczenie)."),
        new("Wiersze w okresach przyszłych", RunCheckRules.Quality, true, "[Okres przyszły]", null,
            "LICZBA() > 0", "SUMA([Kwota])",
            "Wiersze w okresach przyszłych: {SUMA([Wiersze])} ({LISTA([Rok/okres])})", null,
            0, CheckLevel.Warning, null, "Wiersze z okresem po bieżącym okresie kalendarza."),
        new("Waluta inna niż waluta kosztów", RunCheckRules.Quality, true, "NIE PUSTE([Waluta]) ORAZ [Waluta] <> [Waluta kosztów]", null,
            "LICZBA() > 0", null,
            "Waluta inna niż {[Waluta kosztów]}: {LISTA([Waluta])} ({SUMA([Wiersze])} wierszy) – saldo w różnych walutach", null,
            0, CheckLevel.Warning, null, "Wiersze w walucie innej niż waluta pola kwoty ACTUALS (PLN) albo przeważająca w danych."),
        new("Wiersze bez elementu WBS", RunCheckRules.Quality, true, "PUSTE([Element WBS])", null,
            "LICZBA() > 0", "SUMA([Kwota])",
            "Wiersze bez elementu WBS: {SUMA([Wiersze])} (sieci, zlecenia przypisane do programu)", null,
            0, CheckLevel.Warning, null, "Wiersze bez elementu WBS (sieci, zlecenia przypisane do programu)."),
    ];

    /// <summary>Kontrole (wiersze macierzy wyników) reguł startowych w kolejności.</summary>
    public static IReadOnlyList<string> Checks { get; } = [.. Starters.Select(r => r.Check).Distinct()];

    public static RunValidationRule? Starter(string name) =>
        Starters.FirstOrDefault(r => string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
