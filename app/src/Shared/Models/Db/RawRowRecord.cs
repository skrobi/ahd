namespace PzlEv.Shared.Models.Db;

/// <summary>Wiersz pliku z zapisanej treści (meta.SourceFileContent): wartości bez zmian (tekst), numer wiersza danych (od 1).</summary>
public sealed record RawRowRecord(long FileId, int RowNumber, string?[] Values);
