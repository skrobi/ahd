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
/// Katalog reguł wbudowanych przebiegu – grupa „Walidacja EV” – i wartości słownika „Kontrole przebiegu” (docs/slowniki.md, rozdz. 2).
/// Pusty słownik – ustawienia domyślne reguł; nowa reguła = pozycja katalogu i jej implementacja w RunChecks. Reguły ACTUALS –
/// formuły (RunValidationRules, słownik „Reguły walidacji”).
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

    /// <summary>Reguły w kolejności wyników przebiegu (grupa „Walidacja EV”; reguły ACTUALS – formuły, RunValidationRules).</summary>
    public static IReadOnlyList<RunCheckRule> All { get; } =
    [
        CostElementWithoutCategory, CostCenterWithoutRate, WpWithoutCostCenter, WpWithoutBudget, NewCostKind, CostKindJump, CostKindDecrease, CostKindClosedChanges,
    ];

    public static RunCheckRule? ByName(string? name) => All.FirstOrDefault(r => string.Equals(r.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static RunCheckSetting Default(RunCheckRule rule) => new(true, rule.Level, rule.Threshold);

    /// <summary>
    /// Grupa „Walidacja EV” (okno Kontrole, dobór kontroli etapu): reguły wbudowane na danych jak w strukturze programu – bez cost
    /// elementów rozliczeniowych, po Wykluczeniach, CAS po narzutach; podgrupy – kontrola reguły (Spójność ze słownikami, Rodzaje kosztu).
    /// </summary>
    public const string EvArea = "Walidacja EV";

    public static IReadOnlyList<string> Areas { get; } = [Dictionaries, CostKinds];

    public static string AreaOf(RunCheckRule rule) => rule.Check;
}
