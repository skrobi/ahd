namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Baza analityczna projektu: drzewo nakładki połączone z WP, CAM, BAC i datami; braki (element nakładki bez WP,
/// WP bez budżetu); sumy kontrolne i zestawienie według CAM.
/// </summary>
public sealed record AnalyticBase(
    IReadOnlyList<AnalyticRow> Rows,
    IReadOnlyList<CamSummary> ByCam,
    IReadOnlyList<string> ElementsWithoutWp,
    IReadOnlyList<string> WpsWithoutBudget,
    int WpCount,
    decimal BacHours,
    decimal BacMaterial);
