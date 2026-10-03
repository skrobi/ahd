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
    public const string MappingCorrection = "dict.MappingCorrection";
}
