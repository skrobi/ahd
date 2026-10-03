using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Parser według definicji parsera (docs/zrodla-danych.md, rozdz. 2): kolumna pliku (po nazwie nagłówka, bez rozróżniania
/// wielkości liter) → pole z typem. Parser pilnuje układu: brak kolumny, którą czyta = ERROR; kolumny, których nie czyta,
/// zostają tylko w treści pliku. Tekst przycięty (dopełnianie zerami, długość pola); liczby w formacie polskim albo
/// z Excela (minus na końcu – zapis SAP), zaokrąglone do 8 miejsc (jak w bazie); daty. Pole wymagane musi być wypełnione.
/// Wartość niezgodna z typem albo brak wymaganej = ERROR z numerem wiersza; plik z błędami nie tworzy danych kanonicznych.
/// Import czyta wiersz po wierszu (Prepare → RowMapper.Map); Parse – cały plik w pamięci (małe pliki, testy).
/// </summary>
public static class MappedParser
{
    /// <summary>Najwięcej problemów pokazywanych szczegółowo dla jednego pliku (reszta jako liczba).</summary>
    public const int MaxReportedIssues = 20;

    /// <summary>Miejsca po przecinku liczb – jak DECIMAL(28,8) w CAN_Row.</summary>
    public const int DecimalPlaces = 8;

    /// <summary>Parser przygotowany dla nagłówka pliku: kolumny czytane, brakujące i spoza parsera.</summary>
    public static RowMapper Prepare(ParserRow parser, IReadOnlyList<string> headers)
    {
        var sources = new List<(ParserField Field, int Index, string Column)>();
        var missing = new List<string>();
        var trimmed = headers.Select(h => h.Trim()).ToList();
        foreach (var field in parser.Fields.Where(f => f.Column.Length > 0))
        {
            var index = trimmed.FindIndex(h => string.Equals(h, field.Column, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                missing.Add(field.Column);
            else
                sources.Add((field, index, trimmed[index]));
        }
        var extra = trimmed
            .Where(h => h.Length > 0 && parser.Fields.All(f => !string.Equals(f.Column, h, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new RowMapper(parser, sources, missing, extra);
    }

    public static ParseResult Parse(ParserRow parser, IReadOnlyList<string> headers, IReadOnlyList<string?[]> rows)
    {
        var mapper = Prepare(parser, headers);
        if (mapper.MissingColumns.Count > 0)
            return new ParseResult(mapper.Fields, [], [Issue.Error($"Brak kolumn parsera {parser.Code}: {string.Join(", ", mapper.MissingColumns)}", "nagłówek")],
                1, mapper.MissingColumns, mapper.ExtraColumns);

        var result = new List<CanonicalRow>(rows.Count);
        var issues = new IssueCollector();
        for (var r = 0; r < rows.Count; r++)
        {
            var values = mapper.Map(rows[r], r + 1, issues);
            if (values is not null)
                result.Add(new CanonicalRow(r + 1, values));
        }
        return new ParseResult(mapper.Fields, issues.Errors == 0 ? result : [], issues.Issues, issues.Errors, [], mapper.ExtraColumns);
    }

    internal static object? Convert(ParserField field, string column, string text, List<string> errors)
    {
        switch (field.Type)
        {
            case FieldTypes.Decimal:
                if (PolishNumber.TryParse(text, out var number))
                    return Math.Round(number, DecimalPlaces, MidpointRounding.AwayFromZero);
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

    internal static string? Clean(string? value)
    {
        if (value is null)
            return null;
        var trimmed = value.Replace('\u00A0', ' ').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

/// <summary>Parser dla nagłówka pliku: zamiana wiersza pliku na wartości pól (w kolejności Fields).</summary>
public sealed class RowMapper(ParserRow parser, IReadOnlyList<(ParserField Field, int Index, string Column)> sources,
    IReadOnlyList<string> missingColumns, IReadOnlyList<string> extraColumns)
{
    public ParserRow Parser { get; } = parser;

    /// <summary>Pola czytane z pliku (kolejność wartości wiersza).</summary>
    public IReadOnlyList<ParserField> Fields { get; } = sources.Select(s => s.Field).ToList();

    /// <summary>Kolumny pliku tych pól (nazwy z nagłówka pliku).</summary>
    public IReadOnlyList<string> Columns { get; } = sources.Select(s => s.Column).ToList();

    /// <summary>Indeks kolumny pliku dla każdego pola.</summary>
    public IReadOnlyList<int> Indexes { get; } = sources.Select(s => s.Index).ToList();

    public IReadOnlyList<string> MissingColumns { get; } = missingColumns;

    public IReadOnlyList<string> ExtraColumns { get; } = extraColumns;

    /// <summary>Wartości pól wiersza; null – wiersz z błędami (dopisane do issues z numerem wiersza danych).</summary>
    public object?[]? Map(string?[] cells, int rowNumber, IssueCollector issues)
    {
        var rowErrors = new List<string>();
        var values = new object?[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            var (field, index, column) = sources[i];
            var text = index < cells.Length ? MappedParser.Clean(cells[index]) : null;
            if (text is null)
            {
                if (field.Required)
                    rowErrors.Add($"{column}: pole wymagane");
                continue;
            }
            values[i] = MappedParser.Convert(field, column, text, rowErrors);
        }
        if (rowErrors.Count == 0)
            return values;
        issues.Add(rowErrors, rowNumber);
        return null;
    }
}

/// <summary>Błędy wartości pliku: liczba wszystkich i pierwsze MaxReportedIssues szczegółowo (pamięć przy milionach wierszy).</summary>
public sealed class IssueCollector
{
    private readonly List<Issue> _issues = [];

    public int Errors { get; private set; }

    public IReadOnlyList<Issue> Issues =>
        Errors > _issues.Count ? [.. _issues, Issue.Error($"… i {Errors - _issues.Count} kolejnych błędów wartości", "plik")] : _issues;

    public void Add(IEnumerable<string> errors, int rowNumber)
    {
        foreach (var error in errors)
        {
            Errors++;
            if (_issues.Count < MappedParser.MaxReportedIssues)
                _issues.Add(Issue.Error(error, $"wiersz danych {rowNumber}"));
        }
    }
}
