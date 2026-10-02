namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Wersja pliku źródłowego – meta.SourceFile (klucz: SHA-256 treści). Plik identyfikuje lokalizacja + nazwa;
/// CanonicalStatus mówi, czy powstały dane kanoniczne (i dlaczego nie).
/// </summary>
public sealed record SourceFileRow(
    long Id,
    string Sha256,
    string Location,
    string FileName,
    string SourceCode,
    long Size,
    DateTimeOffset ModifiedAt,
    string FileType,
    string? Sheet,
    string? Encoding,
    string? Delimiter,
    IReadOnlyList<string> Columns,
    string Signature,
    int RowCount,
    long BatchId,
    DateTimeOffset ImportedAt,
    string ImportedBy,
    string CanonicalStatus,
    int CanonicalRows,
    int? ParserVersion);
