using PzlEv.Shared.Utils.Process;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Proces projektu (ProjectProcess – ścieżka wspólna albo wariant typu projektu) – kontrakt modułu Projekty dla innych
/// modułów (przebieg tygodniowy liczy koszty procesem projektu, docs/pipeline-fazy.md, rozdz. 4.1). Rejestruje go moduł Projekty.
/// </summary>
public interface IProjectProcessSource
{
    ProjectProcess For(string project);
}
