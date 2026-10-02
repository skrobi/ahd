using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Problem (wynik kontroli) – meta.Problem (docs/pipeline-fazy.md, rozdz. 1.3). Reference łączy problem
/// z obiektem, który go wywołał (np. partia importu, przebieg).
/// </summary>
public sealed record ProblemRecord(
    long Id,
    DateTimeOffset At,
    CheckLevel Level,
    string Check,
    string Area,
    string? Element,
    string Message,
    string? Reference,
    bool Resolved);
