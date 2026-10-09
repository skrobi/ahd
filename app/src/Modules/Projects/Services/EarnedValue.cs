using System.Globalization;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Planowana wartość (PV) w strukturze projektu: budżet rozłożony liniowo na dni robocze (poniedziałek–piątek)
/// harmonogramu baseline (docs/performance-objectives.md, rozdz. 4.2).
/// </summary>
public static class EarnedValue
{
    /// <summary>
    /// Udział dni roboczych baseline, które minęły do dnia stanu (włącznie): przed startem 0, od końca 1; brak dat albo
    /// zła kolejność – null (PV nie do policzenia).
    /// </summary>
    public static decimal? Elapsed(string? start, string? finish, DateOnly statusDate)
    {
        if (Date(start) is not { } from || Date(finish) is not { } to || to < from)
            return null;
        if (statusDate < from)
            return 0;
        if (statusDate >= to)
            return 1;
        var total = WorkingDays(from, to);
        return total == 0 ? 1 : (decimal)WorkingDays(from, statusDate) / total;
    }

    /// <summary>Dni robocze (pn–pt) od from do to włącznie.</summary>
    public static int WorkingDays(DateOnly from, DateOnly to)
    {
        if (to < from)
            return 0;
        var days = to.DayNumber - from.DayNumber + 1;
        var weeks = days / 7;
        var result = weeks * 5;
        for (var d = from.AddDays(weeks * 7); d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                result++;
        return result;
    }

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
