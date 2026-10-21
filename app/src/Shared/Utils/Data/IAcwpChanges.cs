namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Zakres okna „Zmiany ACWP”: program; Kind – rodzaj kosztu (Hours / Materials / Other; null – wszystkie); WbsElements – elementy
/// WBS (wiersz struktury z poddrzewem albo element wyniku kontroli; null – cały program); CostElement – jeden cost element (null –
/// wszystkie); PinId – uruchomienie „jest” (null – najnowsze; „było” – poprzednie uruchomienie wcześniejszego tygodnia, jak w przebiegu);
/// Scope – opis zakresu w tytule okna.
/// </summary>
public sealed record AcwpChangesRequest(string Project, string? Kind = null, IReadOnlyCollection<string>? WbsElements = null, string? CostElement = null,
    long? PinId = null, string Scope = "");

/// <summary>
/// Okno „Zmiany ACWP” (docs/funkcjonalnosc.md, F06) – kontrakt modułu Przebiegi dla innych modułów (struktura programu).
/// Rejestruje go moduł Przebiegi (AppServices.Contracts).
/// </summary>
public interface IAcwpChanges
{
    void Show(AcwpChangesRequest request);
}
