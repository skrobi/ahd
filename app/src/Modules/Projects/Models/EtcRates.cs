namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Stawki do ETC Cost struktury (docs/performance-objectives.md, rozdz. 4.2): stawka MPK na rok (Labor Rate × Overhead,
/// słownik „Stawki wydziałów”; stawki w PLN), przelicznik na walutę projektu dla pracy (PLN) i materiałów (USD z vAPD)
/// – kurs ze słownika „Kursy walut” (bieżący okres); Problem – powód braku ETC Cost (np. brak kursu, waluta transakcji).
/// </summary>
public sealed record EtcRates(IReadOnlyDictionary<string, decimal> RateByMpk, int Year, string? Currency, decimal? LaborFactor, decimal? MaterialFactor,
    string? Problem)
{
    public static EtcRates None { get; } = new(new Dictionary<string, decimal>(), DateTime.Today.Year, null, null, null, null);
}
