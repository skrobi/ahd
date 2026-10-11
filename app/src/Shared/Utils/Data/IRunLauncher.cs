using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Uruchomienie przebiegu projektu w tle – kontrakt modułu Przebiegi dla innych modułów (ekran projektu). Rejestruje
/// go moduł Przebiegi (AppServices.Contracts). Wynik zapisuje się w bazie (meta.RunPin, run.Issue); po zakończeniu –
/// powiadomienie Windows i e-mail (Outlook).
/// </summary>
public interface IRunLauncher
{
    /// <summary>Przebieg projektu trwa w tej aplikacji.</summary>
    bool IsRunning(string project);

    /// <summary>Start przebiegu (wątek UI – logowanie do SharePoint); zwraca od razu, przebieg liczy się w tle. Null – uruchomiony, inaczej powód.</summary>
    Task<string?> Start(string project);

    /// <summary>Postęp i zakończenie przebiegu (projekt, tekst; wątek UI).</summary>
    event Action<string, string>? Progress;

    event Action<string>? Finished;

    /// <summary>Ostatnie uruchomienie przebiegu każdego projektu (lista projektów).</summary>
    IReadOnlyDictionary<string, RunPinRow> Latest();

    /// <summary>Ostatnie uruchomienia przebiegów projektu (od najnowszego).</summary>
    IReadOnlyList<RunPinRow> Pins(string project, int count);

    /// <summary>Wyniki kontroli uruchomienia.</summary>
    IReadOnlyList<RunIssueRow> Issues(long pinId);
}
