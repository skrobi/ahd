namespace PzlEv.Shared.Utils.Data;

/// <summary>Panel przebiegu programu: widok (WPF) i zwolnienie przy zamknięciu ekranu (odpięcie od zdarzeń przebiegu).</summary>
public interface IRunPanel : IDisposable
{
    object View { get; }
}

/// <summary>
/// Panel przebiegu programu (zakładka „Przebieg” ekranu programu – docs/funkcjonalnosc.md, F06): etapy uruchomienia ze statusami,
/// wyniki etapu, dalsze etapy, potwierdzenie z komentarzem, ponowne wykonanie etapu. Rejestruje go moduł Przebiegi (AppServices.Contracts).
/// </summary>
public interface IRunPanels
{
    IRunPanel Create(string project);
}
