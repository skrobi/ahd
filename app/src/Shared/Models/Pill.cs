namespace PzlEv.Shared.Models;

/// <summary>Kolorowa „pigułka” statusu. Poziom (ok, warn, crit, info, ready, stale, accent, muted) wybiera
/// kolor przez LevelBrushConverter; wygląd – fragment Shared/Views/Partials/Partials.xaml.</summary>
public sealed record Pill(string Level, string Text);
