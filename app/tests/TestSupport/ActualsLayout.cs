namespace PzlEv.Tests.TestSupport;

/// <summary>Układ raportu ACTUALS_* (app/testdata/RABIT) = układ parsera ACTUALS z migracji 004.</summary>
public static class ActualsLayout
{
    public const string Parser = "ACTUALS";

    public static readonly IReadOnlyList<string> Columns =
    [
        "Project Definition", "WBS Element", "Cost Element", "Cost element name", "CO object name", "Transaction Currency", "Value TranCurr",
        "Object Currency", "Value in Obj. Crcy", "Report currency", "Val.in rep.cur.", "Total Quantity", "Partner Object Class", "Partner object",
        "Original material", "Original material description", "Original Order Number", "Item", "Purchase order number", "Fiscal Year",
        "Created on", "Period", "Invoice Number",
    ];
}
