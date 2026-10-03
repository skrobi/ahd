using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Problem (wynik kontroli) – meta.Problem (docs/pipeline-fazy.md, rozdz. 1.3). Reference łączy problem
/// z obiektem, który go wywołał (np. partia importu, przebieg). Rozwiązany – kto, kiedy i jak (automatycznie, gdy
/// przyczyna zniknęła, albo ręcznie).
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
    bool Resolved,
    DateTimeOffset? ResolvedAt = null,
    string? ResolvedBy = null,
    string? Resolution = null);
