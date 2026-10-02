using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Tests.TestSupport;

/// <summary>Standardowy układ raportu ACTUALS_* (app/testdata/RABIT) i jego mapowanie na pola parsera ACTUALS (migracja 004).</summary>
public static class ActualsLayout
{
    public const string Parser = "ACTUALS";

    public static readonly IReadOnlyList<string> Columns =
    [
        "Project Definition", "WBS Element", "Cost Element", "Cost element descr.", "Cost element name", "CO object name",
        "Transaction Currency", "Value TranCurr", "Object Currency", "Value in Obj. Crcy", "Report currency", "Val.in rep.cur.",
        "Total Quantity", "Partner-CCtr", "Source object name", "Partner Object Class", "Partner object", "Original material",
        "Original material description", "Fiscal Year", "Created on", "Period",
    ];

    /// <summary>Mapowanie po nazwach; wymagane WBS Element, Fiscal Year, Period.</summary>
    public static IReadOnlyList<ColumnMapping> Mapping(IReadOnlyList<ParserField> fields, IReadOnlyList<string>? columns = null) =>
        SourceConfigService.MapByName(columns ?? Columns, fields)
            .Select(m => m with { Required = m.Field is "WbsElement" or "FiscalYear" or "Period" })
            .ToList();
}
