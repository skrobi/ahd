using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Modules.Projects.Models;

/// <summary>Wczytanie nakładki z Excela (eksport struktury WBS z SAP): drzewo wg Level i wynik kontroli pliku.</summary>
public sealed record PoImportResult(string FileName, PoTree Tree, IReadOnlyList<Issue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Level == CheckLevel.Error);
}
