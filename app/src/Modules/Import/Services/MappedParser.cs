using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Parser według definicji parsera (docs/zrodla-danych.md, rozdz. 2): kolumna pliku (po nazwie nagłówka, bez rozróżniania
/// wielkości liter) → pole z typem. Parser pilnuje układu: brak kolumny, którą czyta = ERROR; kolumny, których nie czyta,
/// trafiają tylko do wierszy surowych. Tekst przycięty (dopełnianie zerami, długość pola); liczby w formacie polskim albo
/// z Excela (minus na końcu – zapis SAP); daty. Pole wymagane musi być wypełnione. Wartość niezgodna z typem albo brak
/// wymaganej = ERROR z numerem wiersza; plik z błędami nie tworzy danych kanonicznych.
/// </summary>
public static class MappedParser
{
    /// <summary>Najwięcej problemów pokazywanych szczegółowo dla jednego pliku (reszta jako liczba).</summary>
    public const int MaxReportedIssues = 20;

    public static ParseResult Parse(ParserRow parser, IReadOnlyList<string> headers, IReadOnlyList<string?[]> rows)
    {
        var lines = new List<(ParserField Field, int Index, string Column)>();
        var missing = new List<string>();
        foreach (var field in parser.Fields.Where(f => f.Column.Length > 0))
        {
            var index = headers.ToList().FindIndex(h => string.Equals(h.Trim(), field.Column, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                missing.Add(field.Column);
            else
                lines.Add((field, index, headers[index].Trim()));
        }
        var fields = lines.Select(l => l.Field).ToList();
        var extra = headers.Select(h => h.Trim())
            .Where(h => h.Length > 0 && parser.Fields.All(f => !string.Equals(f.Column, h, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (missing.Count > 0)
            return new ParseResult(fields, [], [Issue.Error($"Brak kolumn parsera {parser.Code}: {string.Join(", ", missing)}", "nagłówek")], 1, missing, extra);

        var result = new List<CanonicalRow>(rows.Count);
        var issues = new List<Issue>();
        var errors = 0;
        for (var r = 0; r < rows.Count; r++)
        {
            var cells = rows[r];
            var rowErrors = new List<string>();
            var values = new object?[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                var (field, index, column) = lines[i];
                var text = index < cells.Length ? Clean(cells[index]) : null;
                if (text is null)
                {
                    if (field.Required)
                        rowErrors.Add($"{column}: pole wymagane");
                    continue;
                }
                values[i] = Convert(field, column, text, rowErrors);
            }

            if (rowErrors.Count == 0)
            {
                result.Add(new CanonicalRow(r + 1, values));
                continue;
            }
            errors += rowErrors.Count;
            foreach (var error in rowErrors.Where(_ => issues.Count < MaxReportedIssues))
                issues.Add(Issue.Error(error, $"wiersz danych {r + 1}"));
        }

        if (errors > issues.Count)
            issues.Add(Issue.Error($"… i {errors - issues.Count} kolejnych błędów wartości", "plik"));
        return new ParseResult(fields, errors == 0 ? result : [], issues, errors, [], extra);
    }

    private static object? Convert(ParserField field, string column, string text, List<string> errors)
    {
        switch (field.Type)
        {
            case FieldTypes.Decimal:
                if (PolishNumber.TryParse(text, out var number))
                    return number;
                errors.Add($"{column}: '{text}' – oczekiwano liczby");
                return null;
            case FieldTypes.Integer:
                if (PolishNumber.TryParseInteger(text, out var integer) && integer is >= int.MinValue and <= int.MaxValue)
                    return (int)integer;
                errors.Add($"{column}: '{text}' – oczekiwano liczby całkowitej");
                return null;
            case FieldTypes.Date:
                if (DateText.TryParse(text, out var date))
                    return date.ToDateTime(TimeOnly.MinValue);
                errors.Add($"{column}: '{text}' – oczekiwano daty");
                return null;
            default:
                if (field.PadDigits is { } pad && text.All(char.IsAsciiDigit) && text.Length < pad)
                    text = text.PadLeft(pad, '0');
                var length = field.Length ?? FieldTypes.DefaultTextLength;
                if (text.Length <= length)
                    return text;
                errors.Add($"{column}: tekst dłuższy niż {length} znaków ({text.Length})");
                return null;
        }
    }

    private static string? Clean(string? value)
    {
        if (value is null)
            return null;
        var trimmed = value.Replace(' ', ' ').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
