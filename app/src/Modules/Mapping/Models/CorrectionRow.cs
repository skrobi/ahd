namespace PzlEv.Modules.Mapping.Models;

/// <summary>
/// Wersja korekty mapowania (dict.MappingCorrection, docs/mapowanie-ces-p1s.md, rozdz. 8): element albo projekt CES
/// → element P1S (PSPNR), poprzednie przypisanie, uzasadnienie, okres obowiązywania i historia techniczna.
/// </summary>
public sealed record CorrectionRow(
    long Id,
    long RowId,
    int Version,
    string Kind,
    string CesKey,
    string TargetPspnr,
    string TargetWbs,
    string? PreviousTarget,
    string? Justification,
    DateTime ValidFrom,
    DateTime? ValidTo,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    DateTimeOffset? SupersededAt,
    string? SupersededBy)
{
    /// <summary>Bieżąca wersja, nieusunięta (bez daty zamknięcia).</summary>
    public bool IsActive => SupersededAt is null && ValidTo is null;

    public string KindLabel => CorrectionKinds.Label(Kind);

    public string Change => ValidTo is { } to ? $"usunięta {to:yyyy-MM-dd}" : Version == 1 ? "dodana" : "zmieniona";
}

/// <summary>Zapis korekty: nowa albo zmiana bieżącej (RowId i wersja, którą użytkownik widział).</summary>
public sealed record CorrectionInput(string Kind, string CesKey, string TargetPspnr, string? Justification, long? RowId = null, int? Version = null);
