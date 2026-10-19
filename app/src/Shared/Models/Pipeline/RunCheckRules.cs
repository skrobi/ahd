namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Reguła kontroli przebiegu (docs/pipeline-fazy.md, rozdz. 4.1): kod, kontrola (grupa wyników – RUN_Issue.CheckName), nazwa
/// w słowniku „Kontrole przebiegu”, opis, poziom wyniku i próg domyślny (null – reguła bez progu). Implementacja – moduł
/// Przebiegi (RunChecks, po kodzie reguły).
/// </summary>
public sealed record RunCheckRule(string Code, string Check, string Name, string Description, CheckLevel Level, decimal? Threshold = null);

/// <summary>Ustawienie reguły w przebiegu: aktywna, poziom wyniku, próg (null – reguła bez progu).</summary>
public sealed record RunCheckSetting(bool Active, CheckLevel Level, decimal? Threshold);

/// <summary>
/// Katalog reguł kontroli przebiegu i wartości słownika „Kontrole przebiegu” (docs/slowniki.md, rozdz. 2). Pusty słownik –
/// ustawienia domyślne reguł; nowa reguła = pozycja katalogu i jej implementacja w RunChecks.
/// </summary>
public static class RunCheckRules
{
    public const string Volume = "Liczba danych";
    public const string Deviations = "Odchylenia raport do raportu";
    public const string NewCostElements = "Nowe cost elementy";
    public const string NetZero = "Netowanie programu do 0";
    public const string ClosedPeriods = "Zmiany w zamkniętych okresach";
    public const string Quality = "Jakość danych";

    /// <summary>Tolerancja kwot (zaokrąglenia) – próg domyślny reguł z progiem.</summary>
    public const decimal Tolerance = 1m;

    /// <summary>Typ programu w słowniku: wszystkie albo kod typu (META_Project.ProjectType).</summary>
    public const string AllTypes = "Wszystkie";
    public static IReadOnlyList<string> ProjectTypes { get; } = [AllTypes, "WEWNETRZNY", "SAC", "CAS"];

    /// <summary>Wariant przebiegu w słowniku (kalendarz okresów – tydzień zamykający okres).</summary>
    public const string AnyRun = "Każdy";
    public const string ClosingRun = "Zamykający";
    public const string MidPeriodRun = "W środku okresu";
    public static IReadOnlyList<string> RunKinds { get; } = [AnyRun, ClosingRun, MidPeriodRun];

    /// <summary>Poziom w słowniku; INFO – wynik informacyjny (PASS).</summary>
    public static IReadOnlyList<string> Levels { get; } = ["ERROR", "WARNING", "INFO"];

    public static CheckLevel? LevelOf(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "ERROR" => CheckLevel.Error,
        "WARNING" => CheckLevel.Warning,
        "INFO" => CheckLevel.Pass,
        _ => null,
    };

    public static RunCheckRule RowsDecrease { get; } = new("rows-decrease", Volume, "Mniej wierszy niż w poprzednim przebiegu",
        "Zrzut ACTUALS jest narastający: mniej wierszy niż w poprzednim przebiegu to niepełny zrzut albo zmiana zakresu.", CheckLevel.Error);

    public static RunCheckRule CostDecrease { get; } = new("cost-decrease", Volume, "Koszt brutto mniejszy niż w poprzednim przebiegu",
        "Koszt brutto (bez rozliczenia wychodzącego) mniejszy niż w poprzednim przebiegu o więcej niż próg (kwota).", CheckLevel.Warning, Tolerance);

    public static RunCheckRule TopDeviations { get; } = new("deviations", Deviations, "Największe odchylenia raport do raportu",
        "10 największych zmian kosztu brutto po elemencie WBS i po cost elemencie; próg – najmniejsza pokazywana zmiana (kwota).", CheckLevel.Pass, Tolerance);

    public static RunCheckRule CostElementOutside { get; } = new("cost-element-outside", NewCostElements, "Cost element spoza słownika Cost Category",
        "Cost element danych bez wpisu w słowniku Cost Category efektywnym dla programu.", CheckLevel.Error);

    public static RunCheckRule CostElementFirst { get; } = new("cost-element-first", NewCostElements, "Cost element pierwszy raz w programie",
        "Cost element, którego nie było w danych poprzedniego przebiegu.", CheckLevel.Pass);

    public static RunCheckRule NetZeroBalance { get; } = new("net-zero", NetZero, "Netowanie do 0 w okresach zamkniętych",
        "Suma wszystkich wierszy programu w okresach zamkniętych różna od 0 o więcej niż próg (kwota); 10 elementów WBS z największym saldem i przyczyną.",
        CheckLevel.Error, Tolerance);

    public static RunCheckRule PostedAfterSettlement { get; } = new("posted-after-settlement", NetZero, "Koszt utworzony po rozliczeniu",
        "Gdy program nie netuje się do 0: koszt elementu utworzony (Created on) po jego rozliczeniu w tym okresie – wymaga ponownego rozliczenia.",
        CheckLevel.Warning);

    public static RunCheckRule ClosedPeriodChanges { get; } = new("closed-period-changes", ClosedPeriods, "Zmiana salda okresu zamkniętego",
        "Saldo elementu WBS w okresie zamkniętym inne niż w poprzednim przebiegu o więcej niż próg (kwota).", CheckLevel.Warning, Tolerance);

    public static RunCheckRule NegativeCost { get; } = new("negative-cost", Quality, "Ujemny koszt elementu WBS",
        "Koszt elementu WBS narastająco poniżej minus próg (kwota) – storno po rozliczeniu.", CheckLevel.Warning, Tolerance);

    public static RunCheckRule FuturePeriods { get; } = new("future-periods", Quality, "Wiersze w okresach przyszłych",
        "Wiersze w okresach późniejszych niż bieżący okres kalendarza.", CheckLevel.Warning);

    public static RunCheckRule OtherCurrency { get; } = new("other-currency", Quality, "Waluta inna niż waluta kosztów",
        "Wiersze w walucie innej niż PLN (pole Value in Obj. Crcy) albo niż przeważająca waluta danych (inne pola kwoty).", CheckLevel.Warning);

    public static RunCheckRule NoWbsElement { get; } = new("no-wbs", Quality, "Wiersze bez elementu WBS",
        "Wiersze bez elementu WBS (sieci, zlecenia przypisane do programu).", CheckLevel.Warning);

    /// <summary>Reguły w kolejności wyników przebiegu.</summary>
    public static IReadOnlyList<RunCheckRule> All { get; } =
    [
        RowsDecrease, CostDecrease, TopDeviations, CostElementOutside, CostElementFirst, NetZeroBalance, PostedAfterSettlement,
        ClosedPeriodChanges, NegativeCost, FuturePeriods, OtherCurrency, NoWbsElement,
    ];

    public static RunCheckRule? ByName(string? name) => All.FirstOrDefault(r => string.Equals(r.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static RunCheckSetting Default(RunCheckRule rule) => new(true, rule.Level, rule.Threshold);
}
