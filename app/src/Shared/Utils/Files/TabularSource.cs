namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Plik tabelaryczny otwarty do odczytu strumieniowego: nagłówek i opis pliku od razu, wiersze danych (tekst, bez
/// pustych wierszy) czytane na żądanie – każde wywołanie Rows() czyta plik od początku i nie gromadzi wierszy w pamięci.
/// NumberedRows() – te same wiersze z numerem wiersza w pliku (jak w Excelu), do komunikatów o błędach.
/// </summary>
public sealed class TabularSource(
    IReadOnlyList<string> headers,
    string fileType,
    string? sheet,
    string? encoding,
    string? delimiter,
    Func<IEnumerable<(int Row, string?[] Cells)>> rows)
{
    public IReadOnlyList<string> Headers { get; } = headers;

    public string FileType { get; } = fileType;

    public string? Sheet { get; } = sheet;

    public string? Encoding { get; } = encoding;

    public string? Delimiter { get; } = delimiter;

    public IEnumerable<string?[]> Rows() => rows().Select(r => r.Cells);

    public IEnumerable<(int Row, string?[] Cells)> NumberedRows() => rows();

    /// <summary>Wszystkie wiersze w pamięci – tylko dla małych plików (słowniki, podgląd).</summary>
    public TabularData ToData()
    {
        var all = NumberedRows().ToList();
        return new(Headers, all.Select(r => r.Cells).ToList(), FileType, Sheet, Encoding, Delimiter, all.Select(r => r.Row).ToList());
    }
}
