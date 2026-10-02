using PzlEv.Shared.Models;

namespace PzlEv.Modules.Dashboard.Models;

/// <summary>Karta fazy globalnej (Import RABIT, Mapowanie, Baza słowników).</summary>
public sealed record GlobalCard(string Code, string Tag, string Subtitle, IReadOnlyList<Pill> Pills);
