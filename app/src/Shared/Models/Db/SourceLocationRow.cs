namespace PzlEv.Shared.Models.Db;

/// <summary>Wersja lokalizacji RABIT – meta.SourceLocation (docs/zrodla-danych.md, rozdz. 3).</summary>
public sealed record SourceLocationRow(
    long Id,
    long LocationId,
    int Version,
    string Name,
    string Path,
    bool Active,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    DateTimeOffset? SupersededAt,
    string? SupersededBy)
{
    public bool IsCurrent => SupersededAt is null;
}
