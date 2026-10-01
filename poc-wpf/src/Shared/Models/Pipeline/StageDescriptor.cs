namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Opis etapu deklarowany przez moduł, który go realizuje (IModule.Stages).
/// CanAutoRun = false oznacza etap z decyzją człowieka (potwierdzenie, pliki CAM, zatwierdzenie) –
/// przy przyszłej automatyzacji łańcuch etapów zatrzymuje się na nim.
/// </summary>
public sealed record StageDescriptor(
    string Code,
    string Name,
    string Step,
    StageScope Scope,
    bool CanAutoRun);
