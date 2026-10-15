using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Koszt projektu CAS po narzutach (CasCosts) – kontrakt modułu Projekty dla innych modułów (przebieg tygodniowy projektu
/// CAS liczy koszty po narzutach i kursem CAS, docs/pipeline-fazy.md, rozdz. 4.1). Rejestruje go moduł Projekty.
/// </summary>
public interface ICasCostSource
{
    /// <summary>Kalkulator kosztu CAS projektu w jego walucie; null – projekt nie jest projektem CAS.</summary>
    CasCosts? For(string project);
}
