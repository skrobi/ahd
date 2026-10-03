using System.IO;
using System.Globalization;
using System.Text;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Odczyt plików tabelarycznych (strumieniowo – miliony wierszy bez gromadzenia w pamięci):
/// Excel – pierwszy arkusz, nagłówek w pierwszym używanym wierszu (XlsxStreamReader); CSV/TXT – kodowanie UTF-8 (z BOM)
/// albo CP1250, separator wykrywany spośród ; , TAB |. Puste wiersze są pomijane.
/// </summary>
public static class TabularFileReader
{
    public static readonly IReadOnlySet<string> ExcelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm" };
    public static readonly IReadOnlySet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv", ".txt" };

    static TabularFileReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool IsSupported(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ExcelExtensions.Contains(ext) || TextExtensions.Contains(ext);
    }

    public static TabularData Read(string path, string? sheet = null) => Read(File.ReadAllBytes(path), Path.GetFileName(path), sheet);

    /// <summary>Wszystkie wiersze w pamięci – małe pliki (słowniki, podgląd). Import czyta strumieniowo (Open).</summary>
    public static TabularData Read(byte[] content, string fileName, string? sheet = null) => Open(content, fileName, sheet).ToData();

    /// <summary>Otwarcie do odczytu strumieniowego – import liczy hash i czyta wiersze z tych samych bajtów.</summary>
    public static TabularSource Open(byte[] content, string fileName, string? sheet = null)
    {
        var ext = Path.GetExtension(fileName);
        var type = ext.TrimStart('.').ToLowerInvariant();
        if (ExcelExtensions.Contains(ext))
            return XlsxStreamReader.Open(content, type, sheet);
        if (TextExtensions.Contains(ext))
            return OpenText(content, type);
        throw new NotSupportedException($"Nieobsługiwany format pliku: {fileName}");
    }

    private static TabularSource OpenText(byte[] content, string type)
    {
        var bom = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        var utf8 = System.Text.Unicode.Utf8.IsValid(content.AsSpan(bom));
        var encoding = utf8 ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : Encoding.GetEncoding(1250);
        StreamReader Reader() => new(new MemoryStream(content, bom, content.Length - bom, writable: false), encoding, detectEncodingFromByteOrderMarks: false);

        string firstLine;
        using (var reader = Reader())
            firstLine = reader.ReadLine() ?? "";
        var delimiter = DetectDelimiter(firstLine);
        var name = DelimiterName(delimiter);
        var encodingName = utf8 ? "utf-8" : "cp1250";

        string[]? header;
        using (var reader = Reader())
            header = CsvParser.Read(reader, delimiter).FirstOrDefault();
        if (header is null)
            return new TabularSource([], type, null, encodingName, name, () => []);

        IEnumerable<string?[]> Rows()
        {
            using var reader = Reader();
            foreach (var record in CsvParser.Read(reader, delimiter).Skip(1))
            {
                if (record.All(string.IsNullOrWhiteSpace))
                    continue;
                yield return record.Select(v => string.IsNullOrEmpty(v) ? null : v).ToArray();
            }
        }
        return new TabularSource(header.Select(h => h.Trim()).ToList(), type, null, encodingName, name, Rows);
    }

    private static char DetectDelimiter(string line)
    {
        char[] candidates = [';', ',', '\t', '|'];
        var best = ';';
        var bestCount = 0;
        foreach (var candidate in candidates)
        {
            var count = 0;
            var quoted = false;
            foreach (var ch in line)
            {
                if (ch == '"')
                    quoted = !quoted;
                else if (ch == candidate && !quoted)
                    count++;
            }
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }
        return best;
    }

    private static string DelimiterName(char delimiter) => delimiter == '\t' ? "TAB" : delimiter.ToString();
}
