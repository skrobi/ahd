using System.Globalization;

namespace PzlEv.Tests.TestSupport;

/// <summary>
/// Wzorzec kalendarza okresów do sprawdzenia migracji 002: tygodnie ISO roku, okres według czwartku tygodnia,
/// ostatni tydzień okresu zamykający (O18) – wartości w postaci słownika (RRRR-MM-DD, tak / nie).
/// </summary>
public static class IsoCalendar
{
    public static IEnumerable<Dictionary<string, string?>> Rows(int year)
    {
        var weeks = Enumerable.Range(1, ISOWeek.GetWeeksInYear(year))
            .Select(week =>
            {
                var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
                return (Week: week, From: monday, To: monday.AddDays(6), Period: monday.AddDays(3).ToString("yyyy-MM", CultureInfo.InvariantCulture));
            })
            .ToList();
        for (var i = 0; i < weeks.Count; i++)
        {
            var w = weeks[i];
            var closing = i == weeks.Count - 1 || weeks[i + 1].Period != w.Period;
            yield return new Dictionary<string, string?>
            {
                ["Rok"] = year.ToString(CultureInfo.InvariantCulture),
                ["Tydzień"] = w.Week.ToString(CultureInfo.InvariantCulture),
                ["Okres"] = w.Period,
                ["Od"] = w.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Do"] = w.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Zamykający"] = closing ? "tak" : "nie",
            };
        }
    }
}
