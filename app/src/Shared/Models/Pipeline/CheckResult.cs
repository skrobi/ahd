namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Wynik jednej kontroli (wiersz meta.Problem – docs/pipeline-fazy.md, rozdz. 1.3).
/// FixModuleKey wskazuje ekran naprawy (ModuleKeys) – bez zależności od kodu innego modułu.
/// </summary>
public sealed record CheckResult(
    CheckLevel Level,
    string Check,
    string Area,
    string? Element,
    string Message,
    string? FixModuleKey);
