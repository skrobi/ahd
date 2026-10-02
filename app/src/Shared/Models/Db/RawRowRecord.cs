namespace PzlEv.Shared.Models.Db;

/// <summary>Wiersz surowy pliku – stg.RawRow: wartości bez zmian (tekst), numer wiersza danych (od 1).</summary>
public sealed record RawRowRecord(long FileId, int RowNumber, string?[] Values);
