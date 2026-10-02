namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Kontekst wykonania etapu. Etap czyta wejścia wyłącznie z bazy (docs/pipeline-fazy.md, rozdz. 1.1),
/// więc kontekst niesie tylko identyfikatory, użytkownika i anulowanie.
/// </summary>
public sealed record StageContext(
    string? ProjectCode,
    string? RunId,
    string User,
    CancellationToken CancellationToken);
