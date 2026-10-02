using PzlEv.Shared.Models;

namespace PzlEv.Modules.Dashboard.Models;

/// <summary>Karta projektu z osią bieżącego przebiegu.</summary>
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
