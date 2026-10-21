using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Przebiegi projektów w tle – kontrakt modułu Przebiegi dla innych modułów (lista projektów: stan i zakończenie przebiegu;
/// uruchomienie – tylko zakładka Przebieg ekranu projektu, IRunPanels). Rejestruje go moduł Przebiegi (AppServices.Contracts).
/// Wynik zapisuje się w bazie (meta.RunPin, run.Issue); po zakończeniu – powiadomienie Windows.
/// </summary>
public interface IRunLauncher
{
    /// <summary>Przebieg projektu trwa w tej aplikacji.</summary>
    bool IsRunning(string project);

    /// <summary>Zakończenie przebiegu (projekt; wątek UI).</summary>
    event Action<string>? Finished;

    /// <summary>Ostatnie uruchomienie przebiegu każdego projektu (lista projektów).</summary>
    IReadOnlyDictionary<string, RunPinRow> Latest();

    /// <summary>Ostatnie uruchomienia przebiegów projektu (od najnowszego).</summary>
    IReadOnlyList<RunPinRow> Pins(string project, int count);

    /// <summary>Wyniki kontroli uruchomienia.</summary>
    IReadOnlyList<RunIssueRow> Issues(long pinId);
}
