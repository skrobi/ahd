using System.Globalization;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Liczby z plików SAP / RABIT i z Excela: format polski (spacja albo spacja niełamliwa tysięcy, przecinek
/// dziesiętny), format niezmienny (kropka) z komórek Excela, minus na końcu (zapis SAP, np. „12,50-”).
/// </summary>
public static class PolishNumber
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        var negative = false;
        if (s.Length > 1 && s.EndsWith('-'))
        {
            negative = true;
            s = s[..^1];
        }
        s = s.Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace("'", "");
        // Separator dziesiętny: przy obu znakach – ostatni z nich (1.234,56 i 1,234.56); jeden rodzaj występujący kilka
        // razy – separator tysięcy (1,234,567 i 1.234.567); pojedynczy przecinek – dziesiętny (zapis polski).
        var comma = s.LastIndexOf(',');
        var dot = s.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
            s = comma > dot ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (comma >= 0)
            s = s.Count(ch => ch == ',') > 1 ? s.Replace(",", "") : s.Replace(',', '.');
        else if (dot >= 0 && s.Count(ch => ch == '.') > 1)
            s = s.Replace(".", "");

        if (!decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out value))
            return false;
        if (negative)
            value = -value;
        return true;
    }

    public static bool TryParseInteger(string? text, out long value)
    {
        value = 0;
        if (!TryParse(text, out var d) || d != decimal.Truncate(d) || d < long.MinValue || d > long.MaxValue)
            return false;
        value = (long)d;
        return true;
    }

    /// <summary>Zapis kanoniczny (do przechowania): format niezmienny, bez zbędnych zer.</summary>
    public static string ToCanonical(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>Zapis do wyświetlenia: format polski z separatorem tysięcy.</summary>
    public static string ToDisplay(decimal value) => value.ToString("#,0.############", Polish);
}
