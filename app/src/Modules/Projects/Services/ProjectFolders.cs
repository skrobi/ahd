using System.IO;
using PzlEv.Shared.Models;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Foldery projektu na dysku sieciowym (docs/architektura.md, rozdz. 7): Projekty\&lt;Projekt&gt;\ z folderami
/// Finanse, CAM, EV. Foldery okresów (RRRR-MM, Wyslane, Zwrocone) tworzą etapy przebiegu. Uprawnienia CAM do
/// folderu Zwrocone nadaje IT – aplikacja ich nie sprawdza (docs/uprawnienia.md, rozdz. 5).
/// </summary>
public sealed class ProjectFolders(string projectsFolder)
{
    public static readonly IReadOnlyList<string> Subfolders = ["Finanse", "CAM", "EV"];

    public string ProjectsFolder { get; } = projectsFolder;

    public string PathOf(string code) => Path.Combine(ProjectsFolder, code);

    /// <summary>Struktura folderów do podglądu w kreatorze.</summary>
    public string Preview(string code) =>
        $"""
        {PathOf(string.IsNullOrEmpty(code) ? "<KOD>" : code)}\
        ├── Finanse\<RRRR-MM>\             pliki dla finansów (P4)
        ├── CAM\<RRRR-MM>\Wyslane\         pliki dla CAM (P6, przebieg zamykający)
        ├── CAM\<RRRR-MM>\Zwrocone\        pliki zwrócone przez CAM
        └── EV\<RRRR-MM>\                  wyniki EV i plik dla Cobra (P9)
        """;

    /// <summary>Kontrole przed utworzeniem projektu (F01, krok 4): korzeń dostępny, prawo zapisu, brak folderu o tym kodzie.</summary>
    public List<Issue> CheckBeforeCreate(string code)
    {
        var issues = new List<Issue>();
        var root = Path.GetDirectoryName(ProjectsFolder.TrimEnd('\\', '/'));
        if (root is null || !Directory.Exists(root))
        {
            issues.Add(Issue.Error($"Korzeń folderów środowiska {root} jest niedostępny", root));
            return issues;
        }
        issues.Add(new Issue(Shared.Models.Pipeline.CheckLevel.Pass, "Korzeń folderów środowiska dostępny", root));
        issues.Add(CanWrite()
            ? new Issue(Shared.Models.Pipeline.CheckLevel.Pass, "Prawo zapisu w folderze Projekty", ProjectsFolder)
            : Issue.Error("Brak prawa zapisu w folderze Projekty", ProjectsFolder));
        if (code.Length > 0)
        {
            issues.Add(Directory.Exists(PathOf(code))
                ? Issue.Error($"Folder {code} już istnieje – wybierz inny kod albo usuń folder", PathOf(code))
                : new Issue(Shared.Models.Pipeline.CheckLevel.Pass, $"Folder {code} jeszcze nie istnieje", PathOf(code)));
        }
        issues.Add(CamAccessWarning(code));
        return issues;
    }

    /// <summary>Kontrola gotowości (F02): struktura folderów zgodna z konfiguracją.</summary>
    public Issue CheckStructure(string code)
    {
        try
        {
            var missing = Subfolders.Where(f => !Directory.Exists(Path.Combine(PathOf(code), f))).ToList();
            if (!Directory.Exists(PathOf(code)))
                return Issue.Warning("Brak folderu projektu", PathOf(code));
            return missing.Count == 0
                ? new Issue(Shared.Models.Pipeline.CheckLevel.Pass, "Struktura folderów zgodna z konfiguracją", PathOf(code))
                : Issue.Warning($"Brak folderów: {string.Join(", ", missing)}", PathOf(code));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Issue.Warning($"Nie udało się sprawdzić folderów: {ex.Message}", PathOf(code));
        }
    }

    /// <summary>Dostęp CAM do folderu Zwrocone – nadaje IT; aplikacja przygotowuje treść zgłoszenia.</summary>
    public Issue CamAccessWarning(string code) =>
        Issue.Warning("Dostęp CAM do folderu CAM\\<RRRR-MM>\\Zwrocone nadaje IT – wyślij zgłoszenie (treść poniżej); aplikacja nie sprawdza uprawnień", PathOf(code));

    public string ItRequest(string code) =>
        $"Proszę o nadanie osobom pełniącym funkcję CAM w projekcie {code} prawa zapisu do folderów {PathOf(code)}\\CAM\\<RRRR-MM>\\Zwrocone "
        + $"(pliki zwracane przez CAM) oraz prawa odczytu do {PathOf(code)}\\CAM\\<RRRR-MM>\\Wyslane. Lista CAM – słownik „WP i CAM” projektu w PZL-EV.";

    /// <summary>Tworzy brakujące foldery projektu; zwraca utworzone ścieżki.</summary>
    public IReadOnlyList<string> Create(string code)
    {
        var created = new List<string>();
        foreach (var folder in new[] { PathOf(code) }.Concat(Subfolders.Select(f => Path.Combine(PathOf(code), f))))
        {
            if (Directory.Exists(folder))
                continue;
            Directory.CreateDirectory(folder);
            created.Add(folder);
        }
        return created;
    }

    private bool CanWrite()
    {
        try
        {
            Directory.CreateDirectory(ProjectsFolder);
            var probe = Path.Combine(ProjectsFolder, $".pzl-ev-zapis-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
