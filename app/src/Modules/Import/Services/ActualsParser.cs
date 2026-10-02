using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Parser kosztów rzeczywistych CES (ACTUALS_*) → can.Actuals (docs/zrodla-danych.md, rozdz. 4). Kolumny według
/// nazw z nagłówka; liczby w formacie polskim albo z Excela; wymagane: WBS Element, Fiscal Year, Period.
/// Wartość niezgodna z typem = ERROR z numerem wiersza; plik z błędami nie tworzy danych kanonicznych.
/// </summary>
public static class ActualsParser
{
    public const int Version = 1;

    /// <summary>Najwięcej problemów pokazywanych szczegółowo dla jednego pliku (reszta jako liczba).</summary>
    public const int MaxReportedIssues = 20;

    public static ParseResult Parse(long fileId, IReadOnlyList<string> headers, IReadOnlyList<string?[]> rows)
    {
        var index = SourceParsers.ActualsColumns.ToDictionary(
            c => c, c => headers.ToList().FindIndex(h => string.Equals(h.Trim(), c, StringComparison.OrdinalIgnoreCase)));
        var missing = index.Where(kv => kv.Value < 0).Select(kv => kv.Key).ToList();
        if (missing.Count > 0)
            return new ParseResult([], [Issue.Error($"Brak kolumn: {string.Join(", ", missing)}", "nagłówek")], 1);

        var result = new List<ActualsRow>(rows.Count);
        var issues = new List<Issue>();
        var errors = 0;

        for (var r = 0; r < rows.Count; r++)
        {
            var cells = rows[r];
            var rowNumber = r + 1;
            var rowErrors = new List<string>();
            string? Text(string column) => index[column] < cells.Length ? Clean(cells[index[column]]) : null;
            decimal? Number(string column)
            {
                var text = Text(column);
                if (text is null)
                    return null;
                if (PolishNumber.TryParse(text, out var value))
                    return value;
                rowErrors.Add($"{column}: '{text}' – oczekiwano liczby");
                return null;
            }
            int? Integer(string column)
            {
                var text = Text(column);
                if (text is null)
                    return null;
                if (PolishNumber.TryParseInteger(text, out var value) && value is >= int.MinValue and <= int.MaxValue)
                    return (int)value;
                rowErrors.Add($"{column}: '{text}' – oczekiwano liczby całkowitej");
                return null;
            }
            DateOnly? Date(string column)
            {
                var text = Text(column);
                if (text is null)
                    return null;
                if (DateText.TryParse(text, out var value))
                    return value;
                rowErrors.Add($"{column}: '{text}' – oczekiwano daty");
                return null;
            }

            var wbs = Text("WBS Element");
            var year = Integer("Fiscal Year");
            var period = Integer("Period");
            if (wbs is null) rowErrors.Add("WBS Element: pole wymagane");
            if (year is null && Text("Fiscal Year") is null) rowErrors.Add("Fiscal Year: pole wymagane");
            if (period is null && Text("Period") is null) rowErrors.Add("Period: pole wymagane");

            var costElement = Text("Cost Element");
            if (costElement is not null && costElement.All(char.IsAsciiDigit) && costElement.Length < 10)
                costElement = costElement.PadLeft(10, '0');

            var row = new ActualsRow(
                fileId, rowNumber, Version,
                Text("Project Definition"), wbs ?? "", costElement, Text("Cost element descr."), Text("Cost element name"),
                Text("CO object name"), Text("Transaction Currency"), Number("Value TranCurr"), Text("Object Currency"),
                Number("Value in Obj. Crcy"), Text("Report currency"), Number("Val.in rep.cur."), Number("Total Quantity"),
                Text("Partner-CCtr"), Text("Source object name"), Text("Partner Object Class"), Text("Partner object"),
                Text("Original material"), Text("Original material description"), year ?? 0, Date("Created on"), period ?? 0);

            if (rowErrors.Count > 0)
            {
                errors += rowErrors.Count;
                foreach (var error in rowErrors)
                {
                    if (issues.Count < MaxReportedIssues)
                        issues.Add(Issue.Error(error, $"wiersz danych {rowNumber}"));
                }
            }
            else
            {
                result.Add(row);
            }
        }

        if (errors > issues.Count)
            issues.Add(Issue.Error($"… i {errors - issues.Count} kolejnych błędów wartości", "plik"));
        return new ParseResult(errors == 0 ? result : [], issues, errors);
    }

    private static string? Clean(string? value)
    {
        if (value is null)
            return null;
        var trimmed = value.Replace(' ', ' ').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
