using System.IO;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Odczyt plików tabelarycznych:
/// Excel – pierwszy arkusz, nagłówek w pierwszym używanym wierszu; CSV/TXT – kodowanie UTF-8 (z BOM) albo CP1250,
/// separator wykrywany spośród ; , TAB |. Puste wiersze są pomijane.
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

    /// <summary>Odczyt z treści pliku – import liczy hash i czyta wiersze z tych samych bajtów.</summary>
    public static TabularData Read(byte[] content, string fileName, string? sheet = null)
    {
        var ext = Path.GetExtension(fileName);
        if (ExcelExtensions.Contains(ext))
            return ReadExcel(content, ext, sheet);
        if (TextExtensions.Contains(ext))
            return ReadText(content, ext);
        throw new NotSupportedException($"Nieobsługiwany format pliku: {fileName}");
    }

    private static TabularData ReadExcel(byte[] content, string ext, string? sheetName)
    {
        using var workbook = new XLWorkbook(new MemoryStream(content));
        var ws = sheetName is null ? workbook.Worksheets.First() : workbook.Worksheet(sheetName);
        var used = ws.RangeUsed();
        var type = ext.TrimStart('.').ToLowerInvariant();
        if (used is null)
            return new TabularData([], [], type, ws.Name, null, null);

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var lastCol = used.LastColumn().ColumnNumber();

        var headers = new List<string>(lastCol);
        for (var c = 1; c <= lastCol; c++)
            headers.Add((CellText(ws.Cell(firstRow, c)) ?? "").Trim());

        var rows = new List<string?[]>(Math.Max(0, lastRow - firstRow));
        for (var r = firstRow + 1; r <= lastRow; r++)
        {
            var values = new string?[lastCol];
            var any = false;
            for (var c = 1; c <= lastCol; c++)
            {
                var text = CellText(ws.Cell(r, c));
                values[c - 1] = text;
                any |= !string.IsNullOrWhiteSpace(text);
            }
            if (any)
                rows.Add(values);
        }
        return new TabularData(headers, rows, type, ws.Name, null, null);
    }

    private static string? CellText(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank)
            return null;
        if (v.IsText)
            return v.GetText();
        if (v.IsNumber)
            return v.GetNumber().ToString("R", CultureInfo.InvariantCulture);
        if (v.IsDateTime)
        {
            var d = v.GetDateTime();
            return d.TimeOfDay == TimeSpan.Zero
                ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        if (v.IsBoolean)
            return v.GetBoolean() ? "TRUE" : "FALSE";
        if (v.IsTimeSpan)
            return v.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture);
        return "#" + v.GetError();
    }

    private static TabularData ReadText(byte[] content, string ext)
    {
        var (text, encoding) = Decode(content);
        var firstLine = text.Split('\n', 2)[0].TrimEnd('\r');
        var delimiter = DetectDelimiter(firstLine);
        var records = CsvParser.Parse(text, delimiter);
        var type = ext.TrimStart('.').ToLowerInvariant();
        if (records.Count == 0)
            return new TabularData([], [], type, null, encoding, DelimiterName(delimiter));

        var headers = records[0].Select(h => h.Trim()).ToList();
        var rows = new List<string?[]>(records.Count - 1);
        foreach (var record in records.Skip(1))
        {
            if (record.All(string.IsNullOrWhiteSpace))
                continue;
            rows.Add(record.Select(v => string.IsNullOrEmpty(v) ? null : v).ToArray());
        }
        return new TabularData(headers, rows, type, null, encoding, DelimiterName(delimiter));
    }

    private static (string Text, string Encoding) Decode(byte[] content)
    {
        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
            return (text.TrimStart('﻿'), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(1250).GetString(content), "cp1250");
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
