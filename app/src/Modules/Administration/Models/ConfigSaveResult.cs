using PzlEv.Shared.Models;

namespace PzlEv.Modules.Administration.Models;

/// <summary>Wynik zapisu konfiguracji: błędy walidacji albo konflikt (zmiana w międzyczasie) blokują zapis.</summary>
public sealed record ConfigSaveResult(bool Success, IReadOnlyList<Issue> Issues, string Message);
