namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Zawartość pliku tabelarycznego (Excel, CSV, TXT): nagłówek i wiersze danych jako tekst – bez interpretacji typów.
/// Liczby z komórek Excela są zapisane w formacie niezmiennym (kropka dziesiętna), daty jako RRRR-MM-DD.
/// RowNumbers – numer wiersza w pliku (jak w Excelu) dla każdego wiersza danych; null – nieznany.
/// </summary>
public sealed record TabularData(
    IReadOnlyList<string> Headers,
    IReadOnlyList<string?[]> Rows,
    string FileType,
    string? Sheet,
    string? Encoding,
    string? Delimiter,
    IReadOnlyList<int>? RowNumbers = null);
