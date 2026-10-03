namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Wiersz bazy analitycznej (docs/funkcjonalnosc.md, F01, krok 5): węzeł nakładki z WP i CAM podpiętymi przez
/// Legacy WBS, budżetem i datami (sumy poddrzewa – każdy WP liczony raz). Gap – brak do uzupełnienia (null – brak braków).
/// </summary>
public sealed record AnalyticRow(
    int Depth,
    string Name,
    string? WbsElement,
    string? LegacyWbs,
    bool IsVirtual,
    IReadOnlyList<string> Wps,
    IReadOnlyList<string> Cams,
    decimal BacHours,
    decimal BacMaterial,
    string? Start,
    string? Finish,
    string? Gap);
