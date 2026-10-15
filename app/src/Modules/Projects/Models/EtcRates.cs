namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Stawki do ETC Cost struktury (docs/performance-objectives.md, rozdz. 4.2): stawka MPK na rok (Labor Rate × Overhead,
/// słownik „Stawki wydziałów”; stawki w PLN), przelicznik na walutę projektu dla pracy (PLN) i materiałów (USD z vAPD)
/// – kurs ze słownika „Kursy walut” (bieżący okres); Problem – powód braku ETC Cost (np. brak kursu, waluta nieznana).
/// Factors – przelicznik kwoty w danej walucie na walutę projektu (waluty z kursem w „Kursy walut”, PLN i waluta projektu;
/// kwoty „Harmonogram i budżet” wpisane w innej walucie niż obecna waluta projektu).
/// </summary>
public sealed record EtcRates(IReadOnlyDictionary<string, decimal> RateByMpk, int Year, string? Currency, decimal? LaborFactor, decimal? MaterialFactor,
    string? Problem, IReadOnlyDictionary<string, decimal>? Factors = null)
{
    public static EtcRates None { get; } = new(new Dictionary<string, decimal>(), DateTime.Today.Year, null, null, null, null);

    /// <summary>Przelicznik waluty na walutę projektu: pusta albo równa walucie projektu (albo waluta projektu nieznana) – 1; brak kursu – null.</summary>
    public decimal? Factor(string? currency) =>
        currency is null || Currency is null || string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase) ? 1
        : Factors?.TryGetValue(currency, out var factor) == true ? factor : null;
}
