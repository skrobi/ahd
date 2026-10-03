using PzlEv.Shared.Models;

namespace PzlEv.Modules.Dashboard.Models;

/// <summary>Wiersz listy „Wymaga uwagi”: otwarty problem z rejestru (meta.Problem) – skąd, opis, kiedy.</summary>
public sealed record AttentionItem(long ProblemId, Pill Tag, string Text, string When);
