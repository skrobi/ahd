namespace PzlEv.Shared.Models.Db;

/// <summary>Decyzja dla pliku w danym imporcie – meta.SourceFileSeen (docs/pipeline-fazy.md, G1).</summary>
public sealed record SourceFileSeenRow(
    long Id,
    long BatchId,
    string Location,
    string FileName,
    long Size,
    DateTimeOffset ModifiedAt,
    string? Sha256,
    string Decision,
    string? SourceCode,
    int? Rows,
    string Description);
