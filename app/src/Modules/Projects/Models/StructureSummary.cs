namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Sumy struktury projektu (zakładka Wskaźniki): WP i CAM, budżet, okres, braki; koszt rzeczywisty (ACWP) elementów
/// nakładki, w tym bez przypisania do WP (brak WP albo kilka WP pod elementem), i koszt elementów CES projektu spoza
/// nakładki. HasCosts – koszty wczytane.
/// </summary>
public sealed record StructureSummary(
    int Wps,
    int Cams,
    decimal BacHours,
    decimal BacMaterial,
    string? Start,
    string? Finish,
    int ElementsWithoutWp,
    int WpsWithoutBudget,
    decimal Acwp = 0,
    decimal AcwpWithoutWp = 0,
    decimal AcwpOutside = 0,
    bool HasCosts = false);
