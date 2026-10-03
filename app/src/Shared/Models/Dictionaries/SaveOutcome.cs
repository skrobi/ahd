using PzlEv.Shared.Models;

namespace PzlEv.Shared.Models.Dictionaries;

public enum SaveStatus
{
    Saved,
    NoChanges,
    Rejected,
    NeedsConfirmation,
    Conflict,
}

/// <summary>
/// Wynik zapisu słownika: Rejected – są ERROR (nic nie zapisano); NeedsConfirmation – są WARNING, zapis
/// wymaga potwierdzenia; Conflict – ktoś zmienił dane w międzyczasie (trzeba odświeżyć).
/// </summary>
public sealed record SaveOutcome(SaveStatus Status, IReadOnlyList<Issue> Issues, string Message);
