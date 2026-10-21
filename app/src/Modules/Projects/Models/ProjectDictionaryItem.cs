namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Słownik projektu na ekranach projektu: kod słownika, nazwa, arkusz w szablonie Excel i czy jest zapisywany. Czego wymaga
/// przebieg, sprawdzają reguły walidacji przebiegu (docs/pipeline-fazy.md, rozdz. 4.1).
/// </summary>
public sealed record ProjectDictionaryItem(string Code, string Name, string Sheet, bool Stored);
