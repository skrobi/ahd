namespace PzlEv.Test.Models;

/// <summary>Kolorowa „pigułka” (pill) – poziom steruje pędzlem przez LevelBrushConverter.</summary>
public sealed record Pill(string Level, string Text);

/// <summary>Kropka statusu etapu na osi przebiegu.</summary>
public sealed record StageDot(string Level);

/// <summary>Karta faz globalnych (Import RABIT, Mapowanie, Baza słowników).</summary>
public sealed record GlobalCard(string Code, string Tag, string Subtitle, IReadOnlyList<Pill> Pills);

/// <summary>Karta zakresu (projektu) z osią przebiegu.</summary>
public sealed record ZakresCard(
    string Code,
    string TypeLabel,
    string Name,
    Pill? ScopeWarning,
    string RunId,
    Pill RunStatus,
    IReadOnlyList<StageDot> Dots,
    string RunLine,
    string LastBy);

/// <summary>Wiersz listy „Wymaga uwagi”.</summary>
public sealed record AttentionItem(Pill Tag, string Text);

/// <summary>Wpis dziennika zdarzeń.</summary>
public sealed record EventItem(string When, string Where, string Who, string Message);

/// <summary>Wiersz panelu „Diagnostyka środowiska” – pakiet NuGet wczytany w runtime.</summary>
public sealed record PackageInfo(string Name, string Version, string Location);
