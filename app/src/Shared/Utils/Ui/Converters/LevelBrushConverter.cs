using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PzlEv.Shared.Utils.Ui.Converters;

/// <summary>
/// Mapuje poziom ("ok", "warn", "crit", "info", "stale", "ready", "accent", "muted")
/// na pędzel z Theme.xaml. ConverterParameter = "fg" (tekst) albo "bg" (tło).
/// </summary>
public sealed class LevelBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = (value as string ?? "muted").ToLowerInvariant();
        var fg = string.Equals(parameter as string, "fg", StringComparison.OrdinalIgnoreCase);

        string key = level switch
        {
            "ok"     => fg ? "Ok"     : "OkSoft",
            "warn"   => fg ? "Warn"   : "WarnSoft",
            "crit"   => fg ? "Crit"   : "CritSoft",
            "info"   => fg ? "Info"   : "InfoSoft",
            "ready"  => fg ? "Ready"  : "ReadySoft",
            "stale"  => fg ? "Stale"  : "StaleSoft",
            "accent" => fg ? "AccentInk" : "Accent",
            "muted"  => fg ? "Muted"  : "Surface2",
            _        => fg ? "Muted"  : "Surface2",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
