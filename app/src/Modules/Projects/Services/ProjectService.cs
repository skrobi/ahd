using System.IO;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Projekt PZL-EV (docs/funkcjonalnosc.md, F01, F02): utworzenie z nakładką Performance Objectives, słownikami
/// projektu i folderami; utrzymanie nakładki i słowników projektu; gotowość; baza analityczna i eksport do Excela.
/// </summary>
public sealed class ProjectService(IProjectStore store, IDictionaryStore dictionaries, IJournal journal, ProjectFolders folders)
{
    private const string Area = "Projekty";

    private readonly DictionaryService _dictionaries = new(dictionaries, journal);

    public ProjectFolders Folders => folders;

    public IReadOnlyList<ProjectInfo> Projects() => store.Projects();

    public ProjectInfo? Find(string code) => store.Find(code);

    public PoTree Objectives(string code) => store.Objectives(code);

    public List<Issue> ValidateBasics(string code, string name, string type) =>
        ProjectRules.ValidateBasics(code, name, type, store.Projects().Select(p => p.Code));

    public List<Issue> ValidateObjectives(string code, PoTree tree) => ObjectivesValidator.Validate(tree, store.WbsOwners(code));

    public PoImportResult ReadObjectives(string path) => PerformanceObjectivesReader.Read(path);

    /// <summary>
    /// Nakładka po ponownym imporcie z SAP (odświeżenie): struktura z pliku; elementy o tym samym WBS element zachowują
    /// identyfikator (historia węzła trwa). Węzły wirtualne i ręczne zmiany nie są przenoszone – O47.
    /// </summary>
    public static PoTree Refresh(PoTree current, PoTree imported)
    {
        var existing = current.Nodes.Where(n => !n.IsVirtual && n.WbsElement is not null)
            .GroupBy(n => n.WbsElement!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var keys = new Dictionary<long, long>();
        var nodes = new List<PoNode>();
        foreach (var node in imported.Nodes)
        {
            var copy = node.Copy();
            if (node.WbsElement is not null && existing.TryGetValue(node.WbsElement, out var old))
            {
                copy = CopyWithKey(node, old.Key);
                copy.NodeId = old.NodeId;
                copy.Version = old.Version;
            }
            keys[node.Key] = copy.Key;
            nodes.Add(copy);
        }
        foreach (var node in nodes)
            node.ParentKey = node.ParentKey is { } p ? keys[p] : null;
        return new PoTree(nodes);
    }

    private static PoNode CopyWithKey(PoNode node, long key) => new()
    {
        Key = key, ParentKey = node.ParentKey, SortOrder = node.SortOrder, Level = node.Level, IsVirtual = node.IsVirtual,
        ProjectDefinition = node.ProjectDefinition, WbsElement = node.WbsElement, Name = node.Name, PersonResponsible = node.PersonResponsible,
        ProfitCenter = node.ProfitCenter, LegacyWbs = node.LegacyWbs, PerformanceObligation = node.PerformanceObligation,
        SacObjNumber = node.SacObjNumber, IsStatistical = node.IsStatistical, IsAcctAsstElement = node.IsAcctAsstElement,
    };

    /// <summary>Zapis nakładki po edycji (z historią); ERROR walidacji blokuje zapis.</summary>
    public (List<Issue> Issues, StoreResult? Result) SaveObjectives(string code, PoTree tree)
    {
        var issues = ValidateObjectives(code, tree);
        if (issues.Any(i => i.Level == CheckLevel.Error))
            return (issues, null);
        var result = store.SaveObjectives(code, tree);
        if (result.Success)
            journal.Add(Area, $"{code}: nakładka Performance Objectives +{result.Added} ~{result.Updated} −{result.Removed}", code);
        return (issues, result);
    }

    // ---------- słowniki projektu ----------

    /// <summary>Konta AD i nazwiska ze słownika globalnego Osoby (CAM wybierany z listy osób).</summary>
    public IReadOnlySet<string> Persons() =>
        dictionaries.Current(GlobalDictionaries.Persons)
            .SelectMany(r => new[] { r.Values.GetValueOrDefault("Konto AD"), r.Values.GetValueOrDefault("Imię i nazwisko") })
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <param name="wpCam">Wiersze „WP i CAM” do sprawdzenia harmonogramu (null – słownika nie wczytano).</param>
    public ProjectDictionaryContext Context(string code, PoTree tree, IEnumerable<DictRow>? wpCam) =>
        new(tree.Nodes.Where(n => !n.IsVirtual && n.LegacyWbs is not null).Select(n => n.LegacyWbs!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            store.P1sOwners(code),
            Persons(),
            wpCam?.Select(r => r["WP"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<DictRow> Rows(string dictionary, string code) => _dictionaries.Load(ProjectDictionaries.Base(dictionary), code);

    /// <summary>Arkusz słownika w skoroszycie (nazwa jak w szablonie); null – plik bez takiego arkusza albo CSV.</summary>
    public static string? FindSheet(string path, ProjectDictionaryItem item) =>
        TabularFileReader.SheetNames(path).FirstOrDefault(s =>
            s.Equals(item.Sheet, StringComparison.OrdinalIgnoreCase) || s.Equals(item.Name, StringComparison.OrdinalIgnoreCase));

    public ImportPreview PreviewDictionary(string dictionary, ProjectDictionaryContext context, string path, string code, string? sheet) =>
        _dictionaries.PreviewImport(ProjectDictionaries.For(dictionary, context), path, code, sheet);

    public SaveOutcome ApplyDictionary(string dictionary, ProjectDictionaryContext context, ImportPreview preview, string code) =>
        _dictionaries.ApplyImport(ProjectDictionaries.For(dictionary, context), preview, code);

    /// <summary>Słowniki projektu do Excela – arkusz na słownik; pusty „WP i CAM” dostaje elementy P1S z zakresu (szablon).</summary>
    public void ExportDictionaries(string path, string code, string type, PoTree tree)
    {
        var sheets = new List<(string, IReadOnlyList<string>, IEnumerable<IReadOnlyList<object?>>)>();
        foreach (var item in ProjectDictionaries.ForType(type).Where(i => i.Stored))
        {
            var spec = ProjectDictionaries.Base(item.Code);
            var rows = code.Length > 0 ? Rows(item.Code, code) : [];
            IEnumerable<IReadOnlyList<object?>> data = rows.Select(r => (IReadOnlyList<object?>)spec.Columns.Select(c => ValueFormat.ToExcel(c, r[c.Name])).ToList());
            if (item.Code == ProjectDictionaries.WpCam && rows.Count == 0)
            {
                data = tree.Flatten().Select(x => x.Node).Where(n => !n.IsVirtual && n.LegacyWbs is not null)
                    .Select(n => n.LegacyWbs!).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(l => (IReadOnlyList<object?>)new object?[] { l, null, null, null });
            }
            sheets.Add((item.Sheet, spec.Columns.Select(c => c.Name).ToList(), data));
        }
        ExcelTableWriter.WriteSheets(path, sheets);
        journal.Add(Area, $"{(code.Length > 0 ? code : "nowy projekt")}: słowniki projektu pobrane do Excela", code.Length > 0 ? code : null);
    }

    /// <summary>Słowniki globalne z bieżącym stanem (krok „Słowniki projektu” kreatora): nazwa, wiersze, ostatnia zmiana.</summary>
    public IReadOnlyList<(string Name, int Rows, string LastChange)> GlobalState() =>
        GlobalDictionaries.All.Select(spec =>
        {
            var rows = dictionaries.Current(spec.Code);
            var last = rows.MaxBy(r => r.RecordedAt);
            return (spec.Name, rows.Count, last is null ? "brak wierszy" : $"{last.RecordedAt:yyyy-MM-dd HH:mm} · {last.RecordedBy}");
        }).ToList();

    /// <summary>Ostatnia zmiana słownika projektu (do pokazania przy słowniku).</summary>
    public string LastChange(string dictionary, string code)
    {
        var last = dictionaries.Current(dictionary, code).MaxBy(r => r.RecordedAt);
        return last is null ? "" : $"{last.RecordedAt:yyyy-MM-dd HH:mm} · {last.RecordedBy}";
    }

    // ---------- gotowość, baza analityczna ----------

    public List<Issue> Readiness(ProjectInfo project, PoTree tree)
    {
        var counts = ProjectDictionaries.Items.Where(i => i.Stored).ToDictionary(i => i.Code, i => Rows(i.Code, project.Code).Count);
        var persons = Persons();
        var camsOutside = Rows(ProjectDictionaries.WpCam, project.Code).Select(r => r["CAM"]).OfType<string>()
            .Where(c => !persons.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        return ProjectReadiness.Check(project.Type, tree, counts, camsOutside, folders.CheckStructure(project.Code), folders.CamAccessWarning(project.Code));
    }

    public static AnalyticBase Analytic(PoTree tree, IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule) =>
        AnalyticBaseBuilder.Build(tree, wpCam, schedule);

    public void ExportAnalytic(string path, string code, AnalyticBase analytic)
    {
        ExcelTableWriter.WriteSheets(path,
        [
            ("Baza analityczna",
                ["Poziom", "Nazwa", "Element CES", "Legacy WBS (P1S)", "Węzeł wirtualny", "WP", "CAM", "BAC HOURS", "BAC MATERIAL", "Start", "Koniec", "Braki"],
                analytic.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
                {
                    r.Depth + 1, new string(' ', r.Depth * 2) + r.Name, r.WbsElement, r.LegacyWbs, r.IsVirtual, string.Join(", ", r.Wps), string.Join(", ", r.Cams),
                    r.BacHours, r.BacMaterial, r.Start, r.Finish, r.Gap,
                })),
            ("Wg CAM", ["CAM", "WP", "BAC HOURS", "BAC MATERIAL", "Start", "Koniec"],
                analytic.ByCam.Select(c => (IReadOnlyList<object?>)new object?[] { c.Cam, c.Wps, c.BacHours, c.BacMaterial, c.Start, c.Finish })),
        ]);
        journal.Add(Area, $"{(code.Length > 0 ? code : "nowy projekt")}: baza analityczna pobrana do Excela", code.Length > 0 ? code : null);
    }

    // ---------- utworzenie projektu ----------

    /// <summary>
    /// „Utwórz projekt” (F01, krok 5): kontrola danych, nakładki, słowników i folderów; rejestracja projektu z nakładką
    /// w bazie; zapis słowników (wczytane podglądy); utworzenie folderów i kontrola struktury. Słownik z ERROR blokuje.
    /// </summary>
    /// <param name="previews">Wczytane słowniki projektu: kod słownika → plik (ścieżka, arkusz).</param>
    public CreateOutcome Create(string code, string name, string type, PoTree tree, IReadOnlyDictionary<string, (string Path, string? Sheet)> previews)
    {
        var issues = ValidateBasics(code, name, type);
        issues.AddRange(ValidateObjectives(code, tree));
        var loaded = PreviewAll(code, tree, previews);
        foreach (var (dictionary, preview) in loaded)
            issues.AddRange(preview.Issues.Where(i => i.Level == CheckLevel.Error).Select(i => i with { Element = $"{ProjectDictionaries.Item(dictionary).Name}: {i.Element}" }));
        issues.AddRange(folders.CheckBeforeCreate(code).Where(i => i.Level == CheckLevel.Error));
        if (issues.Any(i => i.Level == CheckLevel.Error))
            return new CreateOutcome(false, [], issues.Where(i => i.Level == CheckLevel.Error).ToList());

        var steps = new List<string>();
        var created = store.Create(code, name.Trim(), type, tree);
        if (!created.Success)
            return new CreateOutcome(false, [], [Issue.Error(created.Conflict!, code)]);
        steps.Add($"Zarejestrowano projekt {code} ({ProjectTypes.Label(type)}) z nakładką: {tree.ElementCount} elementów CES, {tree.VirtualCount} węzłów wirtualnych");
        journal.Add(Area, $"{code}: utworzono projekt „{name.Trim()}” ({ProjectTypes.Label(type)})", code);

        var problems = new List<Issue>();
        var wpRows = loaded.TryGetValue(ProjectDictionaries.WpCam, out var wpPreview) ? wpPreview.Working : null;
        var context = Context(code, tree, wpRows);
        foreach (var (dictionary, preview) in loaded)
        {
            var outcome = ApplyDictionary(dictionary, context, preview, code);
            if (outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges)
                steps.Add($"{ProjectDictionaries.Item(dictionary).Name}: {preview.Working.Count} wierszy");
            else
                problems.Add(Issue.Error($"{ProjectDictionaries.Item(dictionary).Name} nie zapisany: {outcome.Message}", dictionary));
        }

        try
        {
            var made = folders.Create(code);
            steps.Add($"Utworzono foldery: {folders.PathOf(code)} ({string.Join(", ", ProjectFolders.Subfolders)}) – {made.Count} nowych");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(Issue.Warning($"Nie utworzono folderów: {ex.Message}", folders.PathOf(code)));
        }
        var structure = folders.CheckStructure(code);
        if (structure.Level == CheckLevel.Pass)
            steps.Add("Struktura folderów zgodna z konfiguracją");
        else
            problems.Add(structure);
        return new CreateOutcome(true, steps, problems);
    }

    /// <summary>Podglądy słowników z plików: najpierw „WP i CAM” (jego WP sprawdza harmonogram).</summary>
    public Dictionary<string, ImportPreview> PreviewAll(string code, PoTree tree, IReadOnlyDictionary<string, (string Path, string? Sheet)> files)
    {
        var result = new Dictionary<string, ImportPreview>();
        var withoutWp = Context(code, tree, null);
        if (files.TryGetValue(ProjectDictionaries.WpCam, out var wpFile))
            result[ProjectDictionaries.WpCam] = PreviewDictionary(ProjectDictionaries.WpCam, withoutWp, wpFile.Path, code, wpFile.Sheet);
        var context = withoutWp with { Wps = result.TryGetValue(ProjectDictionaries.WpCam, out var wp) ? Context(code, tree, wp.Working).Wps : null };
        foreach (var (dictionary, file) in files.Where(f => f.Key != ProjectDictionaries.WpCam))
            result[dictionary] = PreviewDictionary(dictionary, context, file.Path, code, file.Sheet);
        return result;
    }
}
