using PzlEv.Shared.Models;

namespace PzlEv.Modules.Dashboard.Models;

/// <summary>Karta fazy globalnej (Import RABIT, Źródła danych, Słowniki globalne); klik przechodzi do modułu <see cref="ModuleKey"/>.</summary>
public sealed record GlobalCard(string Code, string Tag, string Subtitle, IReadOnlyList<Pill> Pills)
{
    /// <summary>Moduł otwierany kliknięciem karty (null = karta nieklikalna).</summary>
    public string? ModuleKey { get; init; }
}
