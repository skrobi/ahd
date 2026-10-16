namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Koszt bazowy projektu CAS (procedura REP_ProjectCasCosts, migracja 045): Grouping (z kolejnością w drzewie nakładki),
/// Project definition z opisem, element WBS, Cost Element z opisem i Cost grouping (słownik efektywny projektu), rok i okres
/// SAP, kwota w PLN (Value in Obj. Crcy); LastYear / LastPeriod – ostatni okres w danych ACTUALS (kolumna okresu raportu).
/// </summary>
public sealed record CasCostRow(
    string? Grouping,
    int? GroupingOrder,
    string ProjectDefinition,
    string? ProjectDefinitionDescription,
    string WbsElement,
    string? CostElement,
    string? CostElementDescription,
    string? CostGrouping,
    int FiscalYear,
    int Period,
    decimal Amount,
    int? LastYear,
    int? LastPeriod);
