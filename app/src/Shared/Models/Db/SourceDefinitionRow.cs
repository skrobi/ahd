using PzlEv.Shared.Models.Sources;

namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Wersja definicji źródła – meta.SourceDefinition (docs/zrodla-danych.md, rozdz. 2). DefinitionId – definicja
/// logiczna (stała między wersjami); zmiana tworzy nową wersję. Parser – kod parsera (meta.Parser), pusty = tylko
/// wiersze surowe; Mapping – kolumny pliku → pola parsera (z oznaczeniem wymaganych).
/// </summary>
public sealed record SourceDefinitionRow(
    long Id,
    long DefinitionId,
    int Version,
    string Code,
    string Prefix,
    string ReportType,
    IReadOnlyList<string> Columns,
    string Signature,
    string Parser,
    int ParserVersion,
    IReadOnlyList<ColumnMapping> Mapping,
    bool Active,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    DateTimeOffset? SupersededAt,
    string? SupersededBy)
{
    public bool IsCurrent => SupersededAt is null;
}
