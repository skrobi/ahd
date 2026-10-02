namespace PzlEv.Shared.Models.Sources;

/// <summary>
/// Mapowanie kolumny pliku na pole parsera (definicja źródła). Required – wartość musi być wypełniona w każdym
/// wierszu; inaczej plik nie przechodzi walidacji. Kolumny bez mapowania trafiają tylko do wierszy surowych.
/// </summary>
public sealed record ColumnMapping(string Column, string Field, bool Required);
