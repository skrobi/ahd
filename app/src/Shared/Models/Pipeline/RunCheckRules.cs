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
    public const string Dictionaries = "Spójność ze słownikami";
    public const string CostKinds = "Rodzaje kosztu";

    /// <summary>Liczba okresów przed bieżącym, z których liczony jest średni przyrost rodzaju kosztu (skok przyrostu, miary średniego przyrostu).</summary>
    public const int IncrementPeriods = 6;

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

    public static RunCheckRule CostElementWithoutCategory { get; } = new("cost-element-without-category", Dictionaries, "Cost element bez Cost Category",
        "Cost element kosztu (bez rozliczeniowych) ze słownika Cost Category efektywnego dla programu z pustą kolumną Cost Category – koszt poza raportem po kategoriach.",
        CheckLevel.Warning);

    public static RunCheckRule CostCenterWithoutRate { get; } = new("mpk-without-rate", Dictionaries, "MPK bez stawki wydziału na rok",
        "MPK WP ze słownika „WP i CAM” bez wiersza w „Stawki wydziałów” na rok bieżącego okresu – ETC Cost WP bez stawki.", CheckLevel.Error);

    public static RunCheckRule WpWithoutCostCenter { get; } = new("wp-without-mpk", Dictionaries, "WP bez MPK",
        "WP ze słownika „WP i CAM” bez MPK – ETC Cost WP bez stawki wydziału.", CheckLevel.Warning);

    public static RunCheckRule WpWithoutBudget { get; } = new("wp-without-budget", Dictionaries, "WP bez budżetu",
        "WP ze słownika „WP i CAM” bez BAC, BAC HOURS i BAC MATERIAL (puste albo 0) w „Harmonogram i budżet”.", CheckLevel.Warning);

    public static RunCheckRule NewCostKind { get; } = new("new-cost-kind", CostKinds, "Nowy rodzaj kosztu w programie",
        "Koszt rodzaju (Hours / Materials / Other – Rodzaj kosztu w Cost Category, puste – Other), którego w poprzednim przebiegu nie było (|koszt| ≤ próg).",
        CheckLevel.Warning, Tolerance);

    public static RunCheckRule CostKindJump { get; } = new("cost-kind-jump", CostKinds, "Skok przyrostu kosztu",
        "Przyrost rodzaju kosztu w bieżącym okresie większy niż próg × średni przyrost z 6 poprzednich okresów (koszt zaksięgowany w okresie; średnia ≤ 0 – bez wyniku).",
        CheckLevel.Warning, 3m);

    public static RunCheckRule CostKindDecrease { get; } = new("cost-kind-decrease", CostKinds, "Spadek kosztu narastająco w rodzaju",
        "Koszt rodzaju narastająco mniejszy niż w poprzednim przebiegu o więcej niż próg (kwota).", CheckLevel.Warning, Tolerance);

    public static RunCheckRule CostKindClosedChanges { get; } = new("cost-kind-closed-changes", CostKinds, "Zmiana rodzaju kosztu w zamkniętych okresach",
        "Koszt rodzaju w okresach zamkniętych inny niż w poprzednim przebiegu o więcej niż próg (kwota); okresy z największą zmianą.", CheckLevel.Warning, Tolerance);

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
        RowsDecrease, CostDecrease, TopDeviations, CostElementOutside, CostElementFirst, CostElementWithoutCategory, CostCenterWithoutRate,
        WpWithoutCostCenter, WpWithoutBudget, NewCostKind, CostKindJump, CostKindDecrease, CostKindClosedChanges, NetZeroBalance, PostedAfterSettlement, ClosedPeriodChanges, NegativeCost, FuturePeriods, OtherCurrency, NoWbsElement,
    ];

    public static RunCheckRule? ByName(string? name) => All.FirstOrDefault(r => string.Equals(r.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static RunCheckSetting Default(RunCheckRule rule) => new(true, rule.Level, rule.Threshold);

    /// <summary>Grupa kontroli (okno Kontrole, dobór kontroli etapu): Walidacja ACTUALS, Spójność ze słownikami, Rodzaje kosztu.</summary>
    public const string ActualsArea = "Walidacja ACTUALS";

    public static IReadOnlyList<string> Areas { get; } = [ActualsArea, Dictionaries, CostKinds];

    /// <summary>Grupa kontroli reguły: kontrole danych ACTUALS (liczba danych, odchylenia, nowe cost elementy, netowanie, okresy zamknięte, jakość) albo sama kontrola.</summary>
    public static string AreaOf(RunCheckRule rule) => rule.Check is Dictionaries or CostKinds ? rule.Check : ActualsArea;
}
