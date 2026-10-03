using PzlEv.Shared.Models;

namespace PzlEv.Shared.Models.Dictionaries;

/// <summary>
/// Podgląd wczytania słownika z Excela: różnice względem bieżącego stanu (+ nowe / ~ zmienione / − usunięte)
/// i wynik walidacji. Working / Removed – zestaw do zapisania po zatwierdzeniu (z wersjami z chwili podglądu).
/// </summary>
public sealed record ImportPreview(
    string DictionaryCode,
    string FileName,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Removed,
    IReadOnlyList<Issue> Issues,
    IReadOnlyList<DictRow> Working,
    IReadOnlyList<DictRow> RemovedRows)
{
    public bool HasErrors => Issues.Any(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error);

    public bool HasChanges => Added.Count + Changed.Count + Removed.Count > 0;

    public string Summary => $"+{Added.Count} nowe · ~{Changed.Count} zmienione · −{Removed.Count} usunięte";
}
