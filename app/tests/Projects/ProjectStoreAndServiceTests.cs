using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Magazyn projektów i serwis projektu na bazie testowej (TestDatabase); scenariusz 1 z docs/funkcjonalnosc.md.</summary>
public sealed class ProjectStoreAndServiceTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pzlev-" + Guid.NewGuid().ToString("N"));
    private readonly TestDatabase? _database;
    private readonly IProjectStore _store = null!;
    private readonly IDictionaryStore _dictionaries = null!;
    private readonly ProjectService _service = null!;

    public ProjectStoreAndServiceTests()
    {
        Directory.CreateDirectory(_root);
        if (TestDatabase.ConnectionString is null)   // bez bazy testy są pominięte (SqlFact)
            return;
        _database = new TestDatabase();
        _store = new SqlProjectStore(_database.Sql, _services.Clock, _services.User);
        _dictionaries = new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, [.. GlobalDictionaries.Tables, .. ProjectDictionaries.Tables]);
        _service = new ProjectService(_store, _dictionaries, new SqlJournal(_database.Sql, _services.Clock, _services.User),
            new ProjectFolders(Path.Combine(_root, "Projekty")));
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static PoTree Objectives() => PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx")).Tree;

    private static string Dictionaries => TestServices.TestData("Projekty", "Slowniki_M28.xlsx");

    [SqlFact]
    public void Created_project_reads_back_with_tree()
    {
        var tree = Objectives();
        tree.AddVirtual(tree.Nodes.Single(n => n.WbsElement == "4D06WP").Key, "Grupa raportowa");

        Assert.True(_store.Create("M28", "M28 – modernizacja", ProjectTypes.Internal, tree).Success);

        var project = _store.Find("M28")!;
        Assert.Equal(("M28 – modernizacja", ProjectTypes.Internal, 1), (project.Name, project.Type, project.Version));
        var read = _store.Objectives("M28");
        Assert.Equal(tree.Flatten().Select(x => (x.Depth, x.Node.WbsElement, x.Node.Name, x.Node.LegacyWbs, x.Node.IsVirtual)),
            read.Flatten().Select(x => (x.Depth, x.Node.WbsElement, x.Node.Name, x.Node.LegacyWbs, x.Node.IsVirtual)));
        Assert.All(read.Nodes, n => Assert.Equal(1, n.Version));
        Assert.Single(_store.Projects());
    }

    [SqlFact]
    public void Project_with_existing_code_is_rejected()
    {
        Assert.True(_store.Create("M28", "Pierwszy", ProjectTypes.Internal, new PoTree()).Success);

        var second = _store.Create("M28", "Drugi", ProjectTypes.Sac, new PoTree());

        Assert.False(second.Success);
        Assert.Contains("już istnieje", second.Conflict);
        Assert.Equal("Pierwszy", _store.Find("M28")!.Name);
        Assert.Contains(_service.ValidateBasics("M28", "Drugi", ProjectTypes.Sac), i => i.Level == CheckLevel.Error && i.Message.Contains("już istnieje"));
    }

    [SqlFact]
    public void Ces_element_belongs_to_one_project()
    {
        Assert.True(_store.Create("M28", "M28", ProjectTypes.Internal, Objectives()).Success);

        var other = new PoTree();
        other.AddElement(null, "4D06WP.RA", "ten sam element", null);
        var rejected = _store.Create("S70I", "S70i", ProjectTypes.Sac, other);

        Assert.False(rejected.Success);
        Assert.Contains("4D06WP.RA (M28)", rejected.Conflict);
        Assert.Null(_store.Find("S70I"));
        Assert.Equal("M28", _store.WbsOwners("S70I")["4d06wp.ra"]);
        Assert.Contains(_service.ValidateObjectives("S70I", other), i => i.Level == CheckLevel.Error && i.Message.Contains("M28"));
    }

    [SqlFact]
    public void Objectives_changes_keep_history_and_detect_conflicts()
    {
        Assert.True(_store.Create("M28", "M28", ProjectTypes.Internal, Objectives()).Success);
        var tree = _store.Objectives("M28");
        var root = tree.Nodes.Single(n => n.WbsElement == "4D06WP");
        var group = tree.AddVirtual(root.Key, "Inżynieria");
        tree.Move(tree.Nodes.Single(n => n.WbsElement == "4D06WP000001").Key, group.Key);
        tree.Find(tree.Nodes.Single(n => n.WbsElement == "4D06WP.RA").Key)!.Name = "Kabina – zmieniona nazwa";
        tree.Remove(tree.Nodes.Single(n => n.WbsElement == "4D06WP000004").Key);
        var stale = _store.Objectives("M28");

        var saved = _store.SaveObjectives("M28", tree);

        Assert.True(saved.Success, saved.Conflict);
        Assert.Equal((1, 1), (saved.Added, saved.Removed));
        Assert.True(saved.Updated >= 2);
        var read = _store.Objectives("M28");
        Assert.Equal(8, read.Nodes.Count);
        Assert.Equal("Inżynieria", read.Find(read.Nodes.Single(n => n.WbsElement == "4D06WP000001").ParentKey!.Value)!.Name);
        Assert.Equal(2, read.Nodes.Single(n => n.WbsElement == "4D06WP.RA").Version);
        Assert.Equal(new StoreResult(true, null, 0, 0, 0), _store.SaveObjectives("M28", read));

        stale.Find(stale.Nodes.Single(n => n.WbsElement == "4D06WP.RA").Key)!.Name = "zmiana na starej wersji";
        var conflict = _store.SaveObjectives("M28", stale);
        Assert.False(conflict.Success);
        Assert.Contains("odśwież", conflict.Conflict);
    }

    [SqlFact]
    public void Scenario_1_new_project_not_ready_then_dictionaries_make_it_ready()
    {
        // Kreator bez słowników: projekt powstaje, ale jest niegotowy (brak WP, CAM, budżetu).
        var created = _service.Create("M28", "M28 – modernizacja", ProjectTypes.Internal, Objectives(), new Dictionary<string, (string, string?)>());
        Assert.True(created.Created, string.Join("; ", created.Issues.Select(i => i.Message)));
        Assert.True(Directory.Exists(Path.Combine(_root, "Projekty", "M28", "CAM")));
        var project = _service.Find("M28")!;
        var tree = _service.Objectives("M28");
        var before = _service.Readiness(project, tree);
        Assert.False(ProjectReadiness.IsReady(before));
        Assert.Contains(before, c => c.Level == CheckLevel.Error && c.Element == "WP i CAM");

        // Uzupełnienie słowników z Excela (skoroszyt z arkuszami jak w szablonie).
        var context = _service.Context("M28", tree, null);
        var wpItem = ProjectDictionaries.Item(ProjectDictionaries.WpCam);
        var wp = _service.PreviewDictionary(wpItem.Code, context, Dictionaries, "M28", ProjectService.FindSheet(Dictionaries, wpItem));
        Assert.False(wp.HasErrors, string.Join("; ", wp.Issues.Select(i => i.Message)));
        Assert.Equal(4, wp.Added.Count);
        Assert.Equal(SaveStatus.Saved, _service.ApplyDictionary(wpItem.Code, context, wp, "M28").Status);

        var withWp = _service.Context("M28", tree, _service.Rows(ProjectDictionaries.WpCam, "M28"));
        var planItem = ProjectDictionaries.Item(ProjectDictionaries.ScheduleBudget);
        var plan = _service.PreviewDictionary(planItem.Code, withWp, Dictionaries, "M28", ProjectService.FindSheet(Dictionaries, planItem));
        Assert.False(plan.HasErrors, string.Join("; ", plan.Issues.Select(i => i.Message)));
        Assert.Equal(SaveStatus.Saved, _service.ApplyDictionary(planItem.Code, withWp, plan, "M28").Status);

        var after = _service.Readiness(project, tree);
        Assert.True(ProjectReadiness.IsReady(after), string.Join("; ", after.Select(c => c.Message)));

        var analytic = ProjectService.Analytic(tree, _service.Rows(ProjectDictionaries.WpCam, "M28"), _service.Rows(ProjectDictionaries.ScheduleBudget, "M28"));
        Assert.Equal((3, 2250m, 75000.5m), (analytic.WpCount, analytic.BacHours, analytic.BacMaterial));
        Assert.Empty(analytic.ElementsWithoutWp);
    }

    [SqlFact]
    public void Create_with_all_dictionaries_from_one_workbook()
    {
        var files = ProjectDictionaries.ForType(ProjectTypes.Sac).Where(i => i.Stored)
            .Select(i => (i.Code, Sheet: ProjectService.FindSheet(Dictionaries, i)))
            .ToDictionary(x => x.Code, x => (Dictionaries, x.Sheet));
        Assert.All(files.Values, f => Assert.NotNull(f.Sheet));

        var created = _service.Create("M28", "M28", ProjectTypes.Sac, Objectives(), files);

        Assert.True(created.Created, string.Join("; ", created.Issues.Select(i => i.Message)));
        Assert.Equal(4, _service.Rows(ProjectDictionaries.WpCam, "M28").Count);
        Assert.Equal(3, _service.Rows(ProjectDictionaries.ScheduleBudget, "M28").Count);
        Assert.Equal(2, _service.Rows(ProjectDictionaries.Exclusions, "M28").Count);
        Assert.Equal("0057100000", _service.Rows(GlobalDictionaries.CostCategory, "M28").Single()["Numer elementu kosztowego"]);
        Assert.Empty(_dictionaries.Current(GlobalDictionaries.CostCategory));   // słownik globalny bez zmian
        Assert.Equal("M28", _store.P1sOwners("S70I")["AC-CAB.6.38.01"]);

        // Ten sam element P1S w drugim projekcie – ERROR.
        var other = new PoTree();
        other.AddElement(null, "4D06WX", "inny", "AC-CAB.6.38");
        var item = ProjectDictionaries.Item(ProjectDictionaries.WpCam);
        var preview = _service.PreviewDictionary(item.Code, _service.Context("S70I", other, null), Dictionaries, "S70I", ProjectService.FindSheet(Dictionaries, item));
        Assert.Contains(preview.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("należy do projektu M28"));
    }

    [SqlFact]
    public void Dictionary_with_errors_blocks_creation()
    {
        var tree = new PoTree();
        tree.AddElement(null, "4D06WP", "kabina", "AC-XYZ");    // elementy P1S ze skoroszytu są poza zakresem
        var item = ProjectDictionaries.Item(ProjectDictionaries.WpCam);

        var outcome = _service.Create("M28", "M28", ProjectTypes.Internal, tree,
            new Dictionary<string, (string, string?)> { [item.Code] = (Dictionaries, ProjectService.FindSheet(Dictionaries, item)) });

        Assert.False(outcome.Created);
        Assert.Contains(outcome.Issues, i => i.Message.Contains("poza zakresem"));
        Assert.Null(_service.Find("M28"));
        Assert.False(Directory.Exists(Path.Combine(_root, "Projekty", "M28")));
    }

    [SqlFact]
    public void Template_contains_scope_elements_and_reads_back()
    {
        var path = Path.Combine(_root, "szablon.xlsx");
        _service.ExportDictionaries(path, "", ProjectTypes.Internal, Objectives());

        var sheets = PzlEv.Shared.Utils.Files.TabularFileReader.SheetNames(path);
        Assert.Equal(["WP i CAM", "Harmonogram i budżet", "Cost Category projektu", "Wykluczenia"], sheets);
        var wp = PzlEv.Shared.Utils.Files.TabularFileReader.Read(path, "WP i CAM");
        Assert.Equal(["AC-CAB", "AC-CAB.6.38", "AC-CAB.6.38.01", "AC-CAB.6.38.02", "AC-CAB.6.38.03", "AC-CAB.6.38.03.01"], wp.Rows.Select(r => r[0]));
    }
}
