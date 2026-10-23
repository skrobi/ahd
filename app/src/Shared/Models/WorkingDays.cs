namespace PzlEv.Shared.Models;

/// <summary>Dni robocze pn–pt (rozkład liniowy budżetu – PV struktury, propozycje estymacji CAM w latach).</summary>
public static class WorkingDays
{
    /// <summary>Dni robocze (pn–pt) od from do to włącznie.</summary>
    public static int Between(DateOnly from, DateOnly to)
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
}
