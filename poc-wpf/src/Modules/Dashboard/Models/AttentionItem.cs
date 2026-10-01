using PzlEv.Shared.Models;

namespace PzlEv.Modules.Dashboard.Models;

/// <summary>Wiersz listy „Wymaga uwagi”.</summary>
public sealed record AttentionItem(Pill Tag, string Text);
