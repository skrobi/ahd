using System.IO;
using System.Globalization;
using System.Text;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Odczyt plików tabelarycznych (strumieniowo z dysku albo pamięci – miliony wierszy bez gromadzenia w pamięci):
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

    /// <summary>Nazwy arkuszy skoroszytu Excel; plik CSV / TXT – brak arkuszy.</summary>
    public static IReadOnlyList<string> SheetNames(string path)
    {
        if (!ExcelExtensions.Contains(Path.GetExtension(path)))
            return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return XlsxStreamReader.SheetNames(stream);
    }

    public static TabularData Read(string path, string? sheet = null) => OpenFile(path, sheet).ToData();

    /// <summary>Wszystkie wiersze w pamięci – małe pliki (słowniki, podgląd). Import czyta strumieniowo (OpenFile).</summary>
    public static TabularData Read(byte[] content, string fileName, string? sheet = null) => Open(content, fileName, sheet).ToData();

    /// <summary>Otwarcie treści w pamięci do odczytu strumieniowego (treść pliku z bazy, testy).</summary>
    public static TabularSource Open(byte[] content, string fileName, string? sheet = null) =>
        Open(() => new MemoryStream(content, writable: false), fileName, sheet);

    /// <summary>Otwarcie pliku z dysku do odczytu strumieniowego – każde czytanie wierszy otwiera plik od nowa (import).</summary>
    public static TabularSource OpenFile(string path, string? sheet = null) =>
        Open(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan), Path.GetFileName(path), sheet);

    /// <summary>open – nowy strumień treści przy każdym odczycie.</summary>
    public static TabularSource Open(Func<Stream> open, string fileName, string? sheet = null)
    {
        var ext = Path.GetExtension(fileName);
        var type = ext.TrimStart('.').ToLowerInvariant();
        if (ExcelExtensions.Contains(ext))
            return XlsxStreamReader.Open(open, type, sheet);
        if (TextExtensions.Contains(ext))
            return OpenText(open, type);
        throw new NotSupportedException($"Nieobsługiwany format pliku: {fileName}");
    }

    private static TabularSource OpenText(Func<Stream> open, string type)
    {
        bool bom, utf8;
        using (var stream = open())
            (bom, utf8) = DetectUtf8(stream);
        var encoding = utf8 ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : Encoding.GetEncoding(1250);
        StreamReader Reader()
        {
            var stream = open();
            if (bom)
                stream.ReadExactly(new byte[3]);
            return new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16);
        }

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

        // Numer wiersza = numer rekordu (nagłówek – 1); rekord z końcem linii w cudzysłowie liczony jako jeden wiersz.
        IEnumerable<(int Row, string?[] Cells)> Rows()
        {
            using var reader = Reader();
            var number = 1;
            foreach (var record in CsvParser.Read(reader, delimiter).Skip(1))
            {
                number++;
                if (record.All(string.IsNullOrWhiteSpace))
                    continue;
                yield return (number, record.Select(v => string.IsNullOrEmpty(v) ? null : v).ToArray());
            }
        }
        return new TabularSource(header.Select(h => h.Trim()).ToList(), type, null, encodingName, name, Rows);
    }

    /// <summary>BOM UTF-8 i czy cała treść jest poprawnym UTF-8 (odczyt strumieniowy, bez treści w pamięci).</summary>
    private static (bool Bom, bool Utf8) DetectUtf8(Stream stream)
    {
        var bytes = new byte[1 << 16];
        var chars = new char[bytes.Length + 4];
        var count = stream.ReadAtLeast(bytes, 3, throwOnEndOfStream: false);
        var bom = count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetDecoder();
        try
        {
            var offset = bom ? 3 : 0;
            while (count > 0)
            {
                while (offset < count)
                {
                    decoder.Convert(bytes, offset, count - offset, chars, 0, chars.Length, false, out var used, out _, out _);
                    offset += Math.Max(used, 1);
                }
                count = stream.Read(bytes);
                offset = 0;
            }
            decoder.Convert(bytes, 0, 0, chars, 0, chars.Length, true, out _, out _, out _);
            return (bom, true);
        }
        catch (DecoderFallbackException)
        {
            return (bom, false);
        }
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
