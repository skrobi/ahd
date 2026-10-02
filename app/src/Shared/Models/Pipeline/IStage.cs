namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Kontrakt etapu (docs/pipeline-fazy.md, rozdz. 1.1): bramka wejścia, akcja z bramką wyjścia.
/// Implementacje powstają w module-właścicielu etapu; ten sam kontrakt obsłuży uruchomienie z ekranu
/// przez analityka i późniejsze łączenie etapów bez UI. W PoC – tylko kontrakt, bez implementacji.
/// </summary>
public interface IStage
{
    StageDescriptor Descriptor { get; }

    Task<StageResult> CheckGateAsync(StageContext context);

    Task<StageResult> RunAsync(StageContext context);
}
