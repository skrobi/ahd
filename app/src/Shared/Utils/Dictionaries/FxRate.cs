using System.Globalization;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Kurs ze słownika „Kursy walut” (do PLN: ile PLN za jednostkę waluty) obowiązujący od – do: kolumna „Kurs” (przeliczenia
/// walut w strukturze projektu) albo „Kurs CAS” (koszty projektów CAS – GlobalDictionaries.CasFxColumn). PLN – 1.
/// </summary>
public static class FxRate
{
    /// <summary>Kurs obowiązujący w dniu day (kilka – najpóźniej rozpoczęty); brak – null.</summary>
    public static decimal? On(IReadOnlyList<DictRow> rows, string currency, DateOnly day, string column = "Kurs") =>
        Valid(rows, currency, day, day, column);

    /// <summary>Kurs obowiązujący w miesiącu (w miesiącu kilka – najpóźniej rozpoczęty, jak wskaźniki narzutu CAS); brak – null.</summary>
    public static decimal? InMonth(IReadOnlyList<DictRow> rows, string currency, int year, int month, string column = "Kurs")
    {
        var first = new DateOnly(year, month, 1);
        return Valid(rows, currency, first, first.AddMonths(1).AddDays(-1), column);
    }

    private static decimal? Valid(IReadOnlyList<DictRow> rows, string currency, DateOnly from, DateOnly to, string column)
    {
        if (string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
            return 1;
        return rows.Where(r => string.Equals(r["Waluta"], currency, StringComparison.OrdinalIgnoreCase))
            .Select(r => (From: Date(r["Od"]), To: Date(r["Do"]), Rate: Dec(r[column])))
            .Where(r => r.From is { } start && start <= to && (r.To is null || r.To >= from) && r.Rate is > 0)
            .OrderByDescending(r => r.From)
            .Select(r => r.Rate)
            .FirstOrDefault();
    }

    private static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? Dec(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
}
