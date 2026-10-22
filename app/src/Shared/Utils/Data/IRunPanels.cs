namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Panel przebiegu programu: widok (WPF), zwolnienie przy zamknięciu ekranu (odpięcie od zdarzeń przebiegu); FixRequested – „Popraw
/// w aplikacji” przy wyniku kontroli: słownik z filtrem do pokazania (słownik programu – ekran programu, globalny – ekran Słowniki).
/// </summary>
public interface IRunPanel : IDisposable
{
    object View { get; }

    event Action<DictionaryLink>? FixRequested;
}

/// <summary>
/// Panel przebiegu programu (zakładka „Przebieg” ekranu programu – docs/funkcjonalnosc.md, F06): etapy uruchomienia ze statusami,
/// wyniki etapu, dalsze etapy, potwierdzenie z komentarzem, ponowne wykonanie etapu. Rejestruje go moduł Przebiegi (AppServices.Contracts).
/// </summary>
public interface IRunPanels
{
    IRunPanel Create(string project);
}
