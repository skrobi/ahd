namespace PzlEv.Shared.Models.Sources;

/// <summary>
/// Parsery źródeł (sposób utworzenia danych kanonicznych) i układy kolumn źródeł – kontrakt danych
/// (docs/zrodla-danych.md, rozdz. 2, 4). Definicje źródeł (Administracja) wskazują parser; import go stosuje.
/// </summary>
public static class SourceParsers
{
    /// <summary>Brak parsera – zapisywane są tylko wiersze surowe.</summary>
    public const string None = "";

    /// <summary>Koszty rzeczywiste CES (wszystkie prefiksy ACTUALS_* – każdy to osobne źródło).</summary>
    public const string Actuals = "ACTUALS";

    public static readonly IReadOnlyList<string> All = [None, Actuals];

    /// <summary>Wspólny układ kolumn raportów ACTUALS_* (docs/zrodla-danych.md, rozdz. 4).</summary>
    public static readonly IReadOnlyList<string> ActualsColumns =
    [
        "Project Definition", "WBS Element", "Cost Element", "Cost element descr.", "Cost element name", "CO object name",
        "Transaction Currency", "Value TranCurr", "Object Currency", "Value in Obj. Crcy", "Report currency", "Val.in rep.cur.",
        "Total Quantity", "Partner-CCtr", "Source object name", "Partner Object Class", "Partner object", "Original material",
        "Original material description", "Fiscal Year", "Created on", "Period",
    ];
}
