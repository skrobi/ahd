using System.IO;
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

    /// <summary>Kolumna szablonu: nagłówek, format, wartości do wyboru (lista), opis (komentarz nagłówka).</summary>
    public sealed record ExcelColumn(string Header, Kind Kind = Kind.Text, IReadOnlyList<string>? Choices = null, string? Note = null,
        IReadOnlyList<(string Value, string Label)>? Lookup = null);

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

    private static (IXLWorksheet Sheet, int Rows) AddSheet(XLWorkbook workbook, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var ws = workbook.Worksheets.Add(SafeSheetName(sheetName));
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            ws.Column(c + 1).Width = Math.Clamp(headers[c].Length + 4, 12, 48);
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
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
                    default:
                        cell.Value = row[c]!.ToString();
                        break;
                }
            }
            r++;
        }
        ws.SheetView.FreezeRows(1);
        if (headers.Count > 0)
            ws.Range(1, 1, Math.Max(r - 1, 1), headers.Count).SetAutoFilter();
        return (ws, r - 2);
    }

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return safe.Length > 31 ? safe[..31] : safe;
    }
}
