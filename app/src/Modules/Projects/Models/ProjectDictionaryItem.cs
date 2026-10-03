namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Słownik projektu na ekranach projektu: kod słownika, nazwa, arkusz w szablonie Excel, typy projektu, dla których
/// jest wymagany do uruchomienia przebiegu (docs/slowniki.md, rozdz. 4), i czy jest zapisywany (Stawki CAS – O37).
/// </summary>
public sealed record ProjectDictionaryItem(string Code, string Name, string Sheet, IReadOnlyList<string> RequiredFor, bool Stored)
{
    public bool IsRequired(string type) => RequiredFor.Contains(type);
}
