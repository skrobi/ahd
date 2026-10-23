namespace PzlEv.Shared.Models.Db;

/// <summary>Nazwy tabel schematu (docs/model-danych.md, rozdz. 5) – wspólne dla magazynów wszystkich modułów.</summary>
public static class DbTables
{
    public const string SourceDefinition = "meta.SourceDefinition";
    public const string SourceLocation = "meta.SourceLocation";
    public const string ImportBatch = "meta.ImportBatch";
    public const string SourceFile = "meta.SourceFile";
    public const string SourceFileSeen = "meta.SourceFileSeen";
    public const string Parser = "meta.Parser";
    public const string CanonicalRow = "can.Row";
    /// <summary>Zestawienie ACTUALS po Project definition (migracja 037) – liczone przy imporcie pliku.</summary>
    public const string ActualsBalance = "can.ActualsBalance";
    public const string BuildActualsBalance = "can.BuildActualsBalance";
    public const string Run = "meta.Run";
    public const string RunPin = "meta.RunPin";
    public const string RunBalance = "run.Balance";
    public const string RunIssue = "run.Issue";
    /// <summary>Dane uruchomienia jak w strukturze programu – tabela „ev” reguł walidacji (migracja 051).</summary>
    public const string RunBalanceEv = "run.BalanceEv";

    /// <summary>Wynik klocka „Łączenie źródeł”: koszt elementu WBS uruchomienia przypisany do WP (migracja 059).</summary>
    public const string RunCostAssignment = "run.CostAssignment";
}
