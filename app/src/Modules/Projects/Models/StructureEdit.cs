namespace PzlEv.Modules.Projects.Models;

/// <summary>Zmiana wiersza tabeli struktury do zapisu (ProjectService.SaveStructureEdits): Id wiersza, wiersz, zmienione kolumny.</summary>
public sealed record StructureEdit(string Id, StructureRow Row, IReadOnlyDictionary<string, string?> Changes);

/// <summary>Wynik zapisu wiersza tabeli struktury: zapisany albo nie (zmiany zostają w wierszu) i komunikat.</summary>
public sealed record StructureEditResult(string Id, bool Saved, string Message);
