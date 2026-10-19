namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Okno „Zmiany ACWP” (docs/funkcjonalnosc.md, F06) – kontrakt modułu Przebiegi dla innych modułów (struktura programu).
/// Rejestruje go moduł Przebiegi (AppServices.Contracts).
/// </summary>
public interface IAcwpChanges
{
    /// <summary>
    /// Otwiera okno dla programu: kind – rodzaj kosztu (Hours / Materials / Other; null – wszystkie), wbsElements – elementy WBS
    /// wiersza struktury z poddrzewem (null – cały program), scope – opis zakresu w tytule okna.
    /// </summary>
    void Show(string project, string? kind, IReadOnlyCollection<string>? wbsElements, string scope);
}
