namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Plik tabelaryczny otwarty do odczytu strumieniowego: nagłówek i opis pliku od razu, wiersze danych (tekst, bez
/// pustych wierszy) czytane na żądanie – każde wywołanie Rows() czyta plik od początku i nie gromadzi wierszy w pamięci.
/// </summary>
public sealed class TabularSource(
    IReadOnlyList<string> headers,
    string fileType,
    string? sheet,
    string? encoding,
    string? delimiter,
    Func<IEnumerable<string?[]>> rows)
{
    public IReadOnlyList<string> Headers { get; } = headers;

    public string FileType { get; } = fileType;

    public string? Sheet { get; } = sheet;

    public string? Encoding { get; } = encoding;

    public string? Delimiter { get; } = delimiter;

    public IEnumerable<string?[]> Rows() => rows();

    /// <summary>Wszystkie wiersze w pamięci – tylko dla małych plików (słowniki, podgląd).</summary>
    public TabularData ToData() => new(Headers, Rows().ToList(), FileType, Sheet, Encoding, Delimiter);
}
