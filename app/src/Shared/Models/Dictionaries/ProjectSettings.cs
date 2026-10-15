namespace PzlEv.Shared.Models.Dictionaries;

/// <summary>
/// Parametry słownika projektu „Parametry projektu” (dict.ProjectSetting, kolumna Parametr) – wspólne dla modułów
/// Projekty (ACWP struktury, raport kosztów) i Przebiegi (przypięcie danych ACTUALS).
/// </summary>
public static class ProjectSettings
{
    /// <summary>Pole kwoty parsera ACTUALS; brak wiersza – DefaultActualsAmount.</summary>
    public const string ActualsAmount = "Pole kwoty ACTUALS";

    public const string DefaultActualsAmount = "ValueObjCrcy";

    /// <summary>
    /// Zestaw walutowy ACTUALS: pole kwoty i pole jej waluty (kolumny raportu RABIT). Projekt liczy koszty (ACWP, raport
    /// kosztów, przebieg) w kwocie zestawu, a walutę projektu odczytuje z kolumny waluty tego zestawu w danych projektu.
    /// Waluta transakcji (Value TranCurr) nie jest zestawem – kwoty w różnych walutach się nie sumują (decyzja finansów).
    /// </summary>
    public sealed record ActualsSet(string Field, string AmountColumn, string CurrencyField, string CurrencyColumn, string Description)
    {
        /// <summary>Opis do listy wyboru, np. „Object Currency / Value in Obj. Crcy – waluta obiektu (PLN)”.</summary>
        public string Label => $"{CurrencyColumn} / {AmountColumn} – {Description}";
    }

    public static IReadOnlyList<ActualsSet> ActualsSets { get; } =
    [
        new("ValueObjCrcy", "Value in Obj. Crcy", "ObjectCurrency", "Object Currency", "waluta obiektu (PLN)"),
        new("ValueRepCur", "Val.in rep.cur.", "ReportCurrency", "Report currency", "waluta raportu"),
    ];

    /// <summary>Zestaw walutowy pola kwoty (nieznane pole – domyślny).</summary>
    public static ActualsSet SetOf(string field) => ActualsSets.FirstOrDefault(s => s.Field == field) ?? ActualsSets[0];

    /// <summary>Pola kwoty parsera ACTUALS do wyboru (pole → opis waluty).</summary>
    public static IReadOnlyList<(string Field, string Currency)> ActualsAmounts { get; } =
        ActualsSets.Select(s => (s.Field, s.Description)).ToList();

    /// <summary>Pole kwoty z wartości słownika: znane pole albo domyślne (pusta lub nieznana wartość).</summary>
    public static string ActualsAmountOf(string? value) =>
        ActualsAmounts.FirstOrDefault(a => string.Equals(a.Field, value?.Trim(), StringComparison.OrdinalIgnoreCase)).Field ?? DefaultActualsAmount;

    /// <summary>Opis waluty pola kwoty, np. „waluta obiektu (PLN)”.</summary>
    public static string CurrencyOf(string field) =>
        ActualsAmounts.FirstOrDefault(a => a.Field == field).Currency ?? field;
}
