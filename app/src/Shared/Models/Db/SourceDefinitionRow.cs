namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Wersja definicji źródła – meta.SourceDefinition (docs/zrodla-danych.md, rozdz. 2). DefinitionId – definicja
/// logiczna (stała między wersjami); zmiana tworzy nową wersję. Definicja rozpoznaje plik po prefiksie i wskazuje
/// parser (meta.Parser) – układ kolumn, typy i pola wymagane pilnuje parser; pusty parser = tylko wiersze surowe.
/// </summary>
public sealed record SourceDefinitionRow(
    long Id,
    long DefinitionId,
    int Version,
    string Code,
    string Prefix,
    string ReportType,
    string Parser,
    bool Active,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    DateTimeOffset? SupersededAt,
    string? SupersededBy)
{
    public bool IsCurrent => SupersededAt is null;
}
