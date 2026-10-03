using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Gotowość projektu – lista kontrolna F02 (docs/funkcjonalnosc.md): ERROR blokuje uruchomienie przebiegu, WARNING
/// informuje (np. bez CAM z listy osób nie powstaną pliki CAM). PASS – kontrola spełniona.
/// </summary>
public static class ProjectReadiness
{
    /// <param name="dictionaryRows">Liczba bieżących wierszy słowników projektu (kod słownika → liczba).</param>
    /// <param name="camsOutsidePersons">CAM ze słownika „WP i CAM” spoza słownika Osoby.</param>
    public static List<Issue> Check(string type, PoTree objectives, IReadOnlyDictionary<string, int> dictionaryRows,
        IReadOnlyCollection<string> camsOutsidePersons, Issue folderStructure, Issue camAccess)
    {
        var checks = new List<Issue>
        {
            objectives.ElementCount > 0
                ? Pass($"Nakładka Performance Objectives: {objectives.ElementCount} elementów CES, {objectives.VirtualCount} węzłów wirtualnych")
                : Issue.Error("Nakładka Performance Objectives nie zawiera żadnego elementu", "Performance Objectives"),
        };

        foreach (var item in ProjectDictionaries.ForType(type).Where(i => i.IsRequired(type)))
        {
            if (!item.Stored)
                checks.Add(Issue.Error($"{item.Name} – zawartość słownika nieustalona (O37); przebieg projektu CAS jest zablokowany", item.Name));
            else if (dictionaryRows.GetValueOrDefault(item.Code) is var rows && rows > 0)
                checks.Add(Pass($"{item.Name}: {rows} wierszy"));
            else
                checks.Add(Issue.Error($"{item.Name} – słownik pusty; wymagany dla typu {ProjectTypes.Label(type)}, blokuje uruchomienie przebiegu", item.Name));
        }

        if (dictionaryRows.GetValueOrDefault(ProjectDictionaries.WpCam) > 0)
        {
            checks.Add(camsOutsidePersons.Count == 0
                ? Pass("WP mają przypisanego CAM z listy osób")
                : Issue.Warning($"CAM spoza listy osób (bez konta nie powstaną pliki CAM): {string.Join(", ", camsOutsidePersons.Take(10))}", "WP i CAM"));
        }
        checks.Add(folderStructure);
        checks.Add(camAccess);
        return checks;
    }

    public static bool IsReady(IEnumerable<Issue> checks) => checks.All(c => c.Level != CheckLevel.Error);

    private static Issue Pass(string message) => new(CheckLevel.Pass, message);
}
