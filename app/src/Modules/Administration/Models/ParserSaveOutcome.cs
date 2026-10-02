namespace PzlEv.Modules.Administration.Models;

/// <summary>Wynik zapisu parsera w magazynie: konflikt (null – zapisano) i zmiany tabeli parsera.</summary>
public sealed record ParserSaveOutcome(string? Conflict, IReadOnlyList<string> TableChanges);
