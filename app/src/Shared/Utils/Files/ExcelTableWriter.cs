using ClosedXML.Excel;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Zapis tabeli do Excela: jeden arkusz, pogrubiony nagłówek, autofiltr, zamrożony nagłówek. Wartość string trafia
/// jako tekst (klucze WBS i numery bez utraty zer wiodących), decimal / long jako liczba, DateOnly jako data.
/// Z opisem kolumn (ExcelColumn) – szablon do uzupełnienia jak w Excelu: format całej kolumny (tekst – Excel nie zjada
/// zer wiodących i nie zamienia kodów na daty także w nowych wierszach), lista wyboru (walidacja danych), opis
/// kolumny w komentarzu nagłówka; listy dłuższe niż mieszczą się w walidacji – arkusz „Listy” na końcu skoroszytu.
/// </summary>
public static class ExcelTableWriter
{
    /// <summary>Rodzaj kolumny szablonu – format całej kolumny.</summary>
    public enum Kind { Text, Number, Integer, Date }

    /// <summary>
    /// Kolumna szablonu: nagłówek, format, wartości do wyboru (lista), opis (komentarz nagłówka); Input – kolumna do
    /// uzupełnienia (nagłówek wyróżniony kolorem).
    /// </summary>
    public sealed record ExcelColumn(string Header, Kind Kind = Kind.Text, IReadOnlyList<string>? Choices = null, string? Note = null,
        IReadOnlyList<(string Value, string Label)>? Lookup = null, bool Input = false);

    /// <summary>Komórka z formułą Excela w zapisie A1 (np. „=H6*K$2”); Format – format liczby (null – ogólny).</summary>
    public sealed record Formula(string A1, string? Format = null);

    /// <summary>Wartość z formatem liczby Excela (np. wskaźnik „0.0000%”).</summary>
    public sealed record Formatted(object? Value, string Format);

    /// <summary>
    /// Arkusz raportu: wiersze nad nagłówkiem (Above – np. nazwa bloku kolumn i wskaźniki), nagłówek, wiersze danych
    /// (wartości jak w WriteSheets, także Formula i Formatted); FrozenColumns – kolumny zamrożone z lewej.
    /// </summary>
    public sealed record ReportSheet(string Name, IReadOnlyList<string> Headers, IEnumerable<IReadOnlyList<object?>> Rows,
        IReadOnlyList<IReadOnlyList<object?>>? Above = null, int FrozenColumns = 0);

    /// <summary>Wiersze, w które można wpisać dane z listą wyboru i formatem kolumny (poza wierszami z danymi).</summary>
    private const int TemplateRows = 2000;

    /// <summary>Skoroszyt z opisem kolumn – arkusz na tabelę (szablon / eksport słowników).</summary>
    public static void WriteTemplate(string path, IEnumerable<(string Sheet, IReadOnlyList<ExcelColumn> Columns, IEnumerable<IReadOnlyList<object?>> Rows)> sheets)
    {
        using var workbook = new XLWorkbook();
        var lists = new List<(string Name, IReadOnlyList<(string Value, string Label)> Values)>();
        foreach (var (sheetName, columns, rows) in sheets)
        {
            var (ws, count) = AddSheet(workbook, sheetName, columns.Select(c => c.Header).ToList(), rows);
            var last = Math.Max(count + 1, TemplateRows);
            for (var c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                var range = ws.Range(2, c + 1, last, c + 1);
                range.Style.NumberFormat.Format = column.Kind switch
                {
                    Kind.Text => "@",
                    Kind.Number => "General",
                    Kind.Integer => "0",
                    _ => "yyyy-mm-dd",
                };
                if (column.Note is { } note)
                    ws.Cell(1, c + 1).CreateComment().AddText(note);
                if (column.Input)
                    ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
                if (column.Choices is { Count: > 0 } choices && string.Join(",", choices).Length < 250)
                {
                    var validation = range.CreateDataValidation();
                    validation.List($"\"{string.Join(",", choices)}\"", true);
                    validation.ErrorStyle = XLErrorStyle.Warning;
                    validation.ErrorMessage = $"Dozwolone: {string.Join(", ", choices)}";
                }
                else if (column.Lookup is { Count: > 0 } lookup)
                {
                    lists.Add((column.Header, lookup));
                    var validation = range.CreateDataValidation();
                    validation.List($"=Listy!${ColumnLetter(lists.Count * 2 - 1)}$2:${ColumnLetter(lists.Count * 2 - 1)}${lookup.Count + 1}", true);
                    validation.ErrorStyle = XLErrorStyle.Warning;
                    validation.ErrorMessage = "Wartość spoza listy (arkusz „Listy”) – przy wczytaniu imię i nazwisko zostanie zamienione na USRID, jeśli jest jednoznaczne.";
                }
            }
        }
        if (lists.Count > 0)
        {
            // Listy na końcu skoroszytu – plik bez nazwy arkusza wczytuje się z pierwszego arkusza.
            var ws = workbook.Worksheets.Add("Listy");
            for (var l = 0; l < lists.Count; l++)
            {
                var (name, values) = lists[l];
                ws.Cell(1, l * 2 + 1).Value = name;
                ws.Cell(1, l * 2 + 2).Value = "Opis";
                ws.Range(1, l * 2 + 1, 1, l * 2 + 2).Style.Font.Bold = true;
                ws.Column(l * 2 + 1).Style.NumberFormat.Format = "@";
                for (var i = 0; i < values.Count; i++)
                {
                    ws.Cell(i + 2, l * 2 + 1).Value = values[i].Value;
                    ws.Cell(i + 2, l * 2 + 2).Value = values[i].Label;
                }
                ws.Column(l * 2 + 1).Width = 16;
                ws.Column(l * 2 + 2).Width = 32;
            }
        }
        workbook.SaveAs(path);
    }

    private static string ColumnLetter(int column)
    {
        var letters = "";
        for (; column > 0; column = (column - 1) / 26)
            letters = (char)('A' + (column - 1) % 26) + letters;
        return letters;
    }

    public static void Write(string path, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows) =>
        WriteSheets(path, [(sheetName, headers, rows)]);

    /// <summary>Kilka tabel w jednym skoroszycie – arkusz na tabelę (np. szablon słowników projektu).</summary>
    public static void WriteSheets(string path, IEnumerable<(string Sheet, IReadOnlyList<string> Headers, IEnumerable<IReadOnlyList<object?>> Rows)> sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (sheetName, headers, rows) in sheets)
            AddSheet(workbook, sheetName, headers, rows);
        workbook.SaveAs(path);
    }

    /// <summary>Raport z formułami – arkusz na ReportSheet (np. raport kosztów CES).</summary>
    public static void WriteReport(string path, IEnumerable<ReportSheet> sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var sheet in sheets)
            AddSheet(workbook, sheet.Name, sheet.Headers, sheet.Rows, sheet.Above, sheet.FrozenColumns);
        workbook.SaveAs(path);
    }

    private static (IXLWorksheet Sheet, int Rows) AddSheet(XLWorkbook workbook, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows,
        IReadOnlyList<IReadOnlyList<object?>>? above = null, int frozenColumns = 0)
    {
        var ws = workbook.Worksheets.Add(SafeSheetName(sheetName));
        above ??= [];
        for (var a = 0; a < above.Count; a++)
        {
            for (var c = 0; c < above[a].Count; c++)
                SetValue(ws.Cell(a + 1, c + 1), above[a][c]);
        }
        var headerRow = above.Count + 1;
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            ws.Column(c + 1).Width = Math.Clamp(headers[c].Length + 4, 12, 48);
        }

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
                SetValue(ws.Cell(r, c + 1), row[c]);
            r++;
        }
        ws.SheetView.FreezeRows(headerRow);
        if (frozenColumns > 0)
            ws.SheetView.FreezeColumns(frozenColumns);
        if (headers.Count > 0)
            ws.Range(headerRow, 1, Math.Max(r - 1, headerRow), headers.Count).SetAutoFilter();
        return (ws, r - headerRow - 1);
    }

    private static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case string s:
                cell.Value = s;
                cell.Style.NumberFormat.Format = "@";
                break;
            case decimal d:
                cell.Value = (double)d;
                break;
            case long l:
                cell.Value = l;
                break;
            case int i:
                cell.Value = i;
                break;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "yyyy-mm-dd";
                break;
            case bool b:
                cell.Value = b ? "tak" : "nie";
                break;
            case Formula f:
                cell.FormulaA1 = f.A1;
                if (f.Format is { } formulaFormat)
                    cell.Style.NumberFormat.Format = formulaFormat;
                break;
            case Formatted { Value: var inner, Format: var format }:
                SetValue(cell, inner);
                cell.Style.NumberFormat.Format = format;
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return safe.Length > 31 ? safe[..31] : safe;
    }
}
