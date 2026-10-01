namespace PzlEv.Shared.Models.Pipeline;

/// <summary>Status etapu – docs/pipeline-fazy.md, rozdz. 1.2.</summary>
public enum StageStatus
{
    /// <summary>Oczekuje – poprzednie etapy niezakończone.</summary>
    Waiting,
    /// <summary>Do wykonania.</summary>
    Ready,
    /// <summary>W toku (blokada przebiegu).</summary>
    Running,
    /// <summary>Wymaga akcji – decyzja albo dane od użytkownika.</summary>
    NeedsAction,
    /// <summary>Błąd – kontrola z poziomem ERROR.</summary>
    Failed,
    /// <summary>Zakończony – bramka wyjścia spełniona.</summary>
    Done,
    /// <summary>Nieaktualny – wynik oparty na nieaktualnych wejściach.</summary>
    Stale,
}
