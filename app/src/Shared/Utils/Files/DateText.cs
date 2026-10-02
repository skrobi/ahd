using System.Globalization;

namespace PzlEv.Shared.Utils.Files;

/// <summary>Daty z plików i z edycji: RRRR-MM-DD (także z czasem 00:00:00 z Excela) albo DD.MM.RRRR.</summary>
public static class DateText
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "dd.MM.yyyy", "d.M.yyyy", "yyyy.MM.dd", "yyyy/MM/dd",
    ];

    public static bool TryParse(string? text, out DateOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (DateTime.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            value = DateOnly.FromDateTime(dt);
            return true;
        }
        return false;
    }

    public static string ToCanonical(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
