namespace PzlEv.Modules.MasterData.Models;

/// <summary>
/// Wiersz słownika w edycji: wartości po nazwie kolumny; RowId i Version wskazują wersję, na której pracuje
/// użytkownik (null – nowy wiersz). Wersja służy do wykrycia zmiany dokonanej w międzyczasie przez kogoś innego.
/// </summary>
public sealed record DictRow(long? RowId, int? Version, IReadOnlyDictionary<string, string?> Values)
{
    public string? this[string column] => Values.TryGetValue(column, out var v) ? v : null;
}
