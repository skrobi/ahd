namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Dane kanoniczne kosztów rzeczywistych CES – can.Actuals (docs/zrodla-danych.md, rozdz. 4). Kwoty: w walucie
/// transakcji, obiektu (PLN) i raportowej (USD); element kosztowy liczbowy uzupełniony zerami do 10 znaków.
/// </summary>
public sealed record ActualsRow(
    long FileId,
    int RowNumber,
    int ParserVersion,
    string? ProjectDefinition,
    string WbsElement,
    string? CostElement,
    string? CostElementDescr,
    string? CostElementName,
    string? CoObjectName,
    string? TransactionCurrency,
    decimal? ValueTranCurr,
    string? ObjectCurrency,
    decimal? ValueObjCrcy,
    string? ReportCurrency,
    decimal? ValueRepCur,
    decimal? TotalQuantity,
    string? PartnerCctr,
    string? SourceObjectName,
    string? PartnerObjectClass,
    string? PartnerObject,
    string? OriginalMaterial,
    string? OriginalMaterialDescription,
    int FiscalYear,
    DateOnly? CreatedOn,
    int Period);
