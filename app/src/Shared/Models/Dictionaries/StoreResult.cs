namespace PzlEv.Shared.Models.Dictionaries;

/// <summary>Wynik zapisu w magazynie: sukces albo konflikt (wiersz zmieniony w międzyczasie, klucz zajęty).</summary>
public sealed record StoreResult(bool Success, string? Conflict, int Added, int Updated, int Removed)
{
    public static StoreResult Rejected(string conflict) => new(false, conflict, 0, 0, 0);
}
