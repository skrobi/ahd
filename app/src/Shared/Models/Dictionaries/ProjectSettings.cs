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

    /// <summary>Pola kwoty parsera ACTUALS do wyboru (pole → opis waluty).</summary>
    public static IReadOnlyList<(string Field, string Currency)> ActualsAmounts { get; } =
    [
        ("ValueObjCrcy", "waluta obiektu (PLN)"),
        ("ValueRepCur", "waluta raportu"),
        ("ValueTranCurr", "waluta transakcji"),
    ];

    /// <summary>Pole kwoty z wartości słownika: znane pole albo domyślne (pusta lub nieznana wartość).</summary>
    public static string ActualsAmountOf(string? value) =>
        ActualsAmounts.FirstOrDefault(a => string.Equals(a.Field, value?.Trim(), StringComparison.OrdinalIgnoreCase)).Field ?? DefaultActualsAmount;

    /// <summary>Opis waluty pola kwoty, np. „waluta obiektu (PLN)”.</summary>
    public static string CurrencyOf(string field) =>
        ActualsAmounts.FirstOrDefault(a => a.Field == field).Currency ?? field;
}
