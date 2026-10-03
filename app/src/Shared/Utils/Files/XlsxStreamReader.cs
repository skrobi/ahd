using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Odczyt arkusza Excel (.xlsx, .xlsm) strumieniowo (OpenXML, wiersz po wierszu) – bez ładowania całego skoroszytu, więc
/// także arkusze z milionem wierszy. Wartości jak przy odczycie komórek: tekst bez zmian, liczba w formacie niezmiennym
/// („R”), data RRRR-MM-DD (z godziną, gdy jest), czas trwania ([h]:mm) jako TimeSpan, wartość logiczna TRUE / FALSE.
/// Nagłówek – pierwszy wiersz z treścią; puste wiersze pominięte.
/// </summary>
public static class XlsxStreamReader
{
    private const byte NoDate = 0, IsDate = 1, IsDuration = 2;

    public static TabularSource Open(byte[] content, string fileType, string? sheetName)
    {
        string partId, name;
        string[] strings;
        byte[] dateStyles;
        bool date1904;
        using (var document = SpreadsheetDocument.Open(new MemoryStream(content), false))
        {
            var workbook = document.WorkbookPart ?? throw new InvalidDataException("Plik Excel bez skoroszytu");
            var sheets = workbook.Workbook.Sheets?.Elements<Sheet>().ToList() ?? [];
            var sheet = (sheetName is null ? sheets.FirstOrDefault() : sheets.FirstOrDefault(s => string.Equals(s.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase)))
                        ?? throw new InvalidDataException(sheetName is null ? "Plik Excel bez arkuszy" : $"Brak arkusza {sheetName}");
            partId = sheet.Id!.Value!;
            name = sheet.Name?.Value ?? "";
            strings = SharedStrings(workbook.SharedStringTablePart);
            dateStyles = DateStyles(workbook.WorkbookStylesPart);
            date1904 = workbook.Workbook.WorkbookProperties?.Date1904?.Value ?? false;
        }

        IEnumerable<(int Row, string?[] Cells)> Cells()
        {
            using var document = SpreadsheetDocument.Open(new MemoryStream(content), false);
            var part = (WorksheetPart)document.WorkbookPart!.GetPartById(partId);
            using var reader = OpenXmlReader.Create(part);
            var rowNumber = 0;
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
                {
                    reader.Read();
                    continue;
                }
                var row = (Row)reader.LoadCurrentElement()!;   // czytnik stoi już na następnym elemencie
                rowNumber = row.RowIndex?.Value is { } index ? (int)index : rowNumber + 1;
                var cells = new List<string?>();
                var column = 0;
                foreach (var cell in row.Elements<Cell>())
                {
                    column = cell.CellReference?.Value is { } reference ? ColumnNumber(reference) : column + 1;
                    while (cells.Count < column - 1)
                        cells.Add(null);
                    cells.Add(Text(cell, strings, dateStyles, date1904));
                }
                yield return (rowNumber, cells.ToArray());
            }
        }

        var header = Cells().FirstOrDefault(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c)));
        if (header.Cells is null)
            return new TabularSource([], fileType, name, null, null, () => []);
        var headers = header.Cells.Select(c => (c ?? "").Trim()).ToList();
        // Wiersz co najmniej tak szeroki jak nagłówek (puste komórki na końcu wiersza nie są zapisane w pliku).
        return new TabularSource(headers, fileType, name, null, null,
            () => Cells().Where(r => r.Row > header.Row && r.Cells.Any(c => !string.IsNullOrWhiteSpace(c)))
                .Select(r => r.Cells.Length >= headers.Count ? r.Cells : [.. r.Cells, .. new string?[headers.Count - r.Cells.Length]]));
    }

    private static string? Text(Cell cell, string[] strings, byte[] dateStyles, bool date1904)
    {
        var raw = cell.CellValue?.Text;
        var type = cell.DataType?.Value;
        if (type == CellValues.InlineString)
            return cell.InlineString is { } inline ? InlineText(inline) : null;
        if (raw is null || raw.Length == 0)
            return null;
        if (type == CellValues.SharedString)
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < strings.Length ? strings[i] : raw;
        if (type == CellValues.String)
            return raw;
        if (type == CellValues.Boolean)
            return raw == "1" ? "TRUE" : "FALSE";
        if (type == CellValues.Error)
            return raw;
        if (type == CellValues.Date)
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var iso) ? DateText(iso) : raw;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return raw;
        var style = cell.StyleIndex?.Value is { } s && s < dateStyles.Length ? dateStyles[s] : NoDate;
        if (style == IsDuration)
            return TimeSpan.FromDays(number).ToString("c", CultureInfo.InvariantCulture);
        if (style == IsDate && number is >= -657435 and < 2958466)
            return DateText(DateTime.FromOADate(date1904 ? number + 1462 : number));
        return number.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string DateText(DateTime d) =>
        d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string InlineText(InlineString inline) =>
        inline.Text?.Text ?? string.Concat(inline.Elements<Run>().Select(r => r.Text?.Text));

    /// <summary>Kolumna z adresu komórki, np. „AB12” → 28.</summary>
    private static int ColumnNumber(string reference)
    {
        var column = 0;
        foreach (var ch in reference)
        {
            if (ch is < 'A' or > 'Z')
                break;
            column = column * 26 + (ch - 'A' + 1);
        }
        return column;
    }

    private static string[] SharedStrings(SharedStringTablePart? part)
    {
        if (part is null)
            return [];
        var strings = new List<string>();
        using var reader = OpenXmlReader.Create(part);
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.ElementType != typeof(SharedStringItem) || !reader.IsStartElement)
            {
                reader.Read();
                continue;
            }
            var item = (SharedStringItem)reader.LoadCurrentElement()!;
            strings.Add(item.Text?.Text ?? string.Concat(item.Elements<Run>().Select(r => r.Text?.Text)));
        }
        return strings.ToArray();
    }

    /// <summary>Rodzaj liczby dla każdego stylu komórki (cellXfs): data, czas trwania albo zwykła liczba.</summary>
    private static byte[] DateStyles(WorkbookStylesPart? part)
    {
        var stylesheet = part?.Stylesheet;
        if (stylesheet?.CellFormats is null)
            return [];
        var custom = stylesheet.NumberingFormats?.Elements<NumberingFormat>()
                         .Where(f => f.NumberFormatId?.Value is not null)
                         .ToDictionary(f => f.NumberFormatId!.Value, f => f.FormatCode?.Value ?? "")
                     ?? [];
        return stylesheet.CellFormats.Elements<CellFormat>()
            .Select(f => f.NumberFormatId?.Value is { } id ? Kind(id, custom.GetValueOrDefault(id)) : NoDate)
            .ToArray();
    }

    private static byte Kind(uint id, string? code)
    {
        if (code is null)
            return id switch
            {
                46 => IsDuration,
                >= 14 and <= 22 or >= 27 and <= 36 or 45 or 47 or >= 50 and <= 58 => IsDate,
                _ => NoDate,
            };
        var plain = new System.Text.StringBuilder();
        var duration = false;
        for (var i = 0; i < code.Length; i++)
        {
            var ch = code[i];
            if (ch == '"')
            {
                i = code.IndexOf('"', i + 1) is var end && end < 0 ? code.Length : end;
                continue;
            }
            if (ch == '\\' || ch == '_' || ch == '*')
            {
                i++;
                continue;
            }
            if (ch == '[')
            {
                var close = code.IndexOf(']', i + 1);
                var inside = close < 0 ? "" : code[(i + 1)..close].ToLowerInvariant();
                duration |= inside is "h" or "hh" or "m" or "mm" or "s" or "ss";
                i = close < 0 ? code.Length : close;
                continue;
            }
            if (ch == ';')
                break;   // tylko pierwsza sekcja formatu
            plain.Append(char.ToLowerInvariant(ch));
        }
        if (duration)
            return IsDuration;
        var text = plain.ToString();
        return text.IndexOfAny(['y', 'd', 'h', 's']) >= 0 || (text.Contains('m') && !text.Contains('0') && !text.Contains('#')) ? IsDate : NoDate;
    }
}
