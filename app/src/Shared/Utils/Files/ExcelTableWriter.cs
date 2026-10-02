using System.IO;
using ClosedXML.Excel;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Zapis tabeli do Excela: jeden arkusz, pogrubiony nagłówek. Wartość string trafia jako tekst (klucze WBS
/// i numery bez utraty zer wiodących), decimal / long jako liczba, DateOnly jako data.
/// </summary>
public static class ExcelTableWriter
{
    public static void Write(string path, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var workbook = new XLWorkbook();
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
        workbook.SaveAs(path);
    }

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return safe.Length > 31 ? safe[..31] : safe;
    }
}
