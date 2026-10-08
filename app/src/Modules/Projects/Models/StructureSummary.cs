namespace PzlEv.Modules.Projects.Models;

/// <summary>Sumy struktury projektu (zakładka Wskaźniki): WP i CAM, budżet, okres, braki.</summary>
public sealed record StructureSummary(
    int Wps,
    int Cams,
    decimal BacHours,
    decimal BacMaterial,
    string? Start,
    string? Finish,
    int ElementsWithoutWp,
    int WpsWithoutBudget);
