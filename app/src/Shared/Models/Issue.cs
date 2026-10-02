using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Shared.Models;

/// <summary>
/// Wynik pojedynczej kontroli do pokazania użytkownikowi (walidacja zapisu, import, etap).
/// Element wskazuje miejsce: wiersz, kolumnę, plik. Wygląd – Shared/Views/Partials/Partials.xaml.
/// </summary>
public sealed record Issue(CheckLevel Level, string Message, string? Element = null)
{
    public static Issue Error(string message, string? element = null) => new(CheckLevel.Error, message, element);

    public static Issue Warning(string message, string? element = null) => new(CheckLevel.Warning, message, element);

    public string LevelText => Level switch
    {
        CheckLevel.Error => "ERROR",
        CheckLevel.Warning => "WARNING",
        _ => "PASS",
    };

    /// <summary>Poziom jako nazwa koloru palety (Pill / LevelBrushConverter).</summary>
    public string LevelColor => Level switch
    {
        CheckLevel.Error => "crit",
        CheckLevel.Warning => "warn",
        _ => "ok",
    };
}
