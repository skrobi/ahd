using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Mapping;
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
            new ProjectFolders(Path.Combine(_root, "Projekty")), new SqlMappingStore(_database.Sql, _services.Clock, _services.User), null);
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
        var before = _service.Readiness(project, tree, _service.Mapping());
        Assert.False(ProjectReadiness.IsReady(before));
        Assert.Contains(before, c => c.Level == CheckLevel.Error && c.Element == "WP i CAM");

        // Uzupełnienie słowników z Excela (skoroszyt z arkuszami jak w szablonie).
        var context = _service.Context("M28", tree, null, _service.Mapping());
        var wpItem = ProjectDictionaries.Item(ProjectDictionaries.WpCam);
        var wp = _service.PreviewDictionary(wpItem.Code, context, Dictionaries, "M28", ProjectService.FindSheet(Dictionaries, wpItem));
        Assert.False(wp.HasErrors, string.Join("; ", wp.Issues.Select(i => i.Message)));
        Assert.Equal(4, wp.Added.Count);
        Assert.Equal(SaveStatus.Saved, _service.ApplyDictionary(wpItem.Code, context, wp, "M28").Status);

        var withWp = _service.Context("M28", tree, _service.Rows(ProjectDictionaries.WpCam, "M28"), _service.Mapping());
        var planItem = ProjectDictionaries.Item(ProjectDictionaries.ScheduleBudget);
        var plan = _service.PreviewDictionary(planItem.Code, withWp, Dictionaries, "M28", ProjectService.FindSheet(Dictionaries, planItem));
        Assert.False(plan.HasErrors, string.Join("; ", plan.Issues.Select(i => i.Message)));
        Assert.Equal(SaveStatus.Saved, _service.ApplyDictionary(planItem.Code, withWp, plan, "M28").Status);

        var after = _service.Readiness(project, tree, _service.Mapping());
        Assert.True(ProjectReadiness.IsReady(after), string.Join("; ", after.Select(c => c.Message)));

        var analytic = ProjectService.Analytic(tree, _service.Rows(ProjectDictionaries.WpCam, "M28"), _service.Rows(ProjectDictionaries.ScheduleBudget, "M28"), _service.Mapping());
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
        var preview = _service.PreviewDictionary(item.Code, _service.Context("S70I", other, null, _service.Mapping()), Dictionaries, "S70I", ProjectService.FindSheet(Dictionaries, item));
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
    public void Mapping_inputs_are_read_once_per_session_until_refresh()
    {
        var first = _service.Mapping();

        Assert.Same(first, _service.Mapping());              // kolejne ekrany nie czytają raportu i PZLPROD ponownie
        Assert.NotSame(first, _service.Mapping(refresh: true));
        Assert.Equal("Brak połączenia z PZLPROD (pzl-ev.json, PzlProd)", first.P1sError);
    }

    [SqlFact]
    public void Template_contains_scope_elements_and_reads_back()
    {
        var path = Path.Combine(_root, "szablon.xlsx");
        _service.ExportDictionaries(path, "", ProjectTypes.Internal, Objectives(), MappingInputs.None);

        var sheets = PzlEv.Shared.Utils.Files.TabularFileReader.SheetNames(path);
        Assert.Equal(["WP i CAM", "Harmonogram i budżet", "Cost Category projektu", "Wykluczenia"], sheets);
        var wp = PzlEv.Shared.Utils.Files.TabularFileReader.Read(path, "WP i CAM");
        Assert.Equal(["AC-CAB", "AC-CAB.6.38", "AC-CAB.6.38.01", "AC-CAB.6.38.02", "AC-CAB.6.38.03", "AC-CAB.6.38.03.01"], wp.Rows.Select(r => r[0]));
    }
    [SqlFact]
    public void Structure_edits_are_saved_to_overlay_and_project_dictionaries()
    {
        Assert.True(_store.Create("M28", "M28", ProjectTypes.Internal, Objectives()).Success);
        var inputs = _service.Mapping();
        StructureRow Row(string wbs) => ProjectService.Structure(_store.Objectives("M28"), inputs,
            _service.Rows(ProjectDictionaries.WpCam, "M28"), _service.Rows(ProjectDictionaries.ScheduleBudget, "M28")).Rows.Single(r => r.WbsElement == wbs);

        // Nowy WP bez CAM – ERROR (CAM wymagany), nic nie zapisano.
        var missing = _service.SaveStructureEdit("M28", inputs, Row("4D06WP000001"), new Dictionary<string, string?> { [StructureEdits.Wp] = "true" });
        Assert.False(missing.Saved);
        Assert.Contains("CAM", missing.Message);
        Assert.Empty(_service.Rows(ProjectDictionaries.WpCam, "M28"));

        var assigned = _service.SaveStructureEdit("M28", inputs, Row("4D06WP000001"),
            new Dictionary<string, string?> { [StructureEdits.Wp] = "true", [StructureEdits.Cam] = "e123456" });
        Assert.True(assigned.Saved, assigned.Message);
        var wp = Assert.Single(_service.Rows(ProjectDictionaries.WpCam, "M28"));
        Assert.Equal(("AC-CAB.6.38.01", "AC-CAB.6.38.01", "e123456"), (wp["Element P1S"], wp["WP"], wp["CAM"]));
        Assert.Equal([new PersonOption("e123456", "e123456")], _service.PersonOptions(_service.Rows(ProjectDictionaries.WpCam, "M28")));   // CAM spoza słownika Osoby

        Assert.True(_service.SaveStructureEdit("M28", inputs, Row("4D06WP000001"),
            new Dictionary<string, string?> { [StructureEdits.BacHours] = "12,5", [StructureEdits.Start] = "2026-01-05" }).Saved);
        var budget = Assert.Single(_service.Rows(ProjectDictionaries.ScheduleBudget, "M28"));
        Assert.Equal(("AC-CAB.6.38.01", "12.5", "2026-01-05"), (budget["WP"], budget["BAC HOURS"], budget["Baseline Start"]));
        Assert.Equal(12.5m, Row("4D06WP000001").BacHours);

        var negative = _service.SaveStructureEdit("M28", inputs, Row("4D06WP000001"), new Dictionary<string, string?> { [StructureEdits.BacHours] = "-1" });
        Assert.False(negative.Saved);
        Assert.Contains("ujemny", negative.Message);

        // Odznaczenie WP – przypisanie i budżet WP usunięte.
        Assert.True(_service.SaveStructureEdit("M28", inputs, Row("4D06WP000001"), new Dictionary<string, string?> { [StructureEdits.Wp] = "false" }).Saved);
        Assert.Empty(_service.Rows(ProjectDictionaries.WpCam, "M28"));
        Assert.Empty(_service.Rows(ProjectDictionaries.ScheduleBudget, "M28"));

        var renamed = _service.SaveStructureEdit("M28", inputs, Row("4D06WP000002"),
            new Dictionary<string, string?> { [StructureEdits.Name] = "technolodzy – zmiana", [StructureEdits.P1s] = "AC-CAB.6.38.02.01" });
        Assert.True(renamed.Saved, renamed.Message);
        var node = _store.Objectives("M28").Nodes.Single(n => n.WbsElement == "4D06WP000002");
        Assert.Equal(("technolodzy – zmiana", "AC-CAB.6.38.02.01", 2), (node.Name, node.LegacyWbs, node.Version));
        Assert.False(_service.SaveStructureEdit("M28", inputs, Row("4D06WP000002"), new Dictionary<string, string?> { [StructureEdits.Name] = " " }).Saved);
    }

    [SqlFact]
    public void Project_dictionaries_are_edited_in_table_and_cost_category_changes_stay_in_project()
    {
        Assert.True(_store.Create("M28", "M28", ProjectTypes.Internal, Objectives()).Success);
        var context = _service.Context("M28", _store.Objectives("M28"), null, _service.Mapping());
        static Dictionary<string, string?> Category(string element, string category) =>
            new() { ["Numer elementu kosztowego"] = element, ["Opis"] = "opis", ["Obszar"] = "A", ["Cost Category"] = category };
        var globalSpec = GlobalDictionaries.Get(GlobalDictionaries.CostCategory);
        var dictionaries = new DictionaryService(_dictionaries, new SqlJournal(_database!.Sql, _services.Clock, _services.User));
        Assert.Equal(SaveStatus.Saved, dictionaries.Save(globalSpec,
            [new DictRow(null, null, Category("57100000", "Material")), new DictRow(null, null, Category("61000000", "Labor"))], [], confirmWarnings: true).Status);

        // Cost Category projektu: na początku same pozycje globalne (dziedziczone).
        var rows = _service.EditableRows(GlobalDictionaries.CostCategory, "M28");
        Assert.All(rows, r => Assert.True(r.Inherited));
        Assert.Equal(2, rows.Count);

        // Zmiana jednej linii – zapis jako zmiana projektu (wiersz bez RowId), słownik globalny bez zmian.
        var changed = rows.Single(r => r.Row["Numer elementu kosztowego"] == "0061000000").Row;
        var values = changed.Values.ToDictionary(p => p.Key, p => p.Value);
        values["Cost Category"] = "Subcontract";
        var saved = _service.SaveDictionary(GlobalDictionaries.CostCategory, context, [new DictRow(null, null, values)], [], "M28", confirmWarnings: false);
        Assert.Equal(SaveStatus.Saved, saved.Status);
        rows = _service.EditableRows(GlobalDictionaries.CostCategory, "M28");
        Assert.Equal([("0057100000", "Material", true), ("0061000000", "Subcontract", false)],
            rows.Select(r => (r.Row["Numer elementu kosztowego"], r.Row["Cost Category"], r.Inherited)));
        Assert.Equal(["Material", "Labor"], _dictionaries.Current(GlobalDictionaries.CostCategory).Select(r => r.Values["Cost Category"]));

        // Usunięcie zmiany projektu – wraca pozycja globalna.
        var own = rows.Single(r => !r.Inherited).Row;
        Assert.Equal(SaveStatus.Saved, _service.SaveDictionary(GlobalDictionaries.CostCategory, context, [], [own], "M28").Status);
        rows = _service.EditableRows(GlobalDictionaries.CostCategory, "M28");
        Assert.Equal([("0057100000", "Material"), ("0061000000", "Labor")], rows.Select(r => (r.Row["Numer elementu kosztowego"], r.Row["Cost Category"])));
        Assert.All(rows, r => Assert.True(r.Inherited));

        // Wykluczenia: dodanie, zmiana, usunięcie.
        static Dictionary<string, string?> Exclusion(string ce, string description) =>
            new() { ["Cost Element"] = ce, ["WBS Element"] = null, ["Partner object"] = null, ["Opis"] = description };
        Assert.Equal(SaveStatus.Saved, _service.SaveDictionary(ProjectDictionaries.Exclusions, context,
            [new DictRow(null, null, Exclusion("57100000", "rozliczenie")), new DictRow(null, null, Exclusion("61000000", "IC"))], [], "M28").Status);
        var exclusions = _service.EditableRows(ProjectDictionaries.Exclusions, "M28").Select(r => r.Row).ToList();
        Assert.Equal(2, exclusions.Count);
        var editedValues = exclusions[0].Values.ToDictionary(p => p.Key, p => p.Value);
        editedValues["Opis"] = "rozliczenie – zmiana";
        var edited = exclusions[0] with { Values = editedValues };
        Assert.Equal(SaveStatus.Saved, _service.SaveDictionary(ProjectDictionaries.Exclusions, context, [edited], [exclusions[1]], "M28").Status);
        var left = Assert.Single(_service.Rows(ProjectDictionaries.Exclusions, "M28"));
        Assert.Equal(("0057100000", "rozliczenie – zmiana"), (left["Cost Element"], left["Opis"]));

        // Błąd (brak opisu) – nic nie zapisano.
        var rejected = _service.SaveDictionary(ProjectDictionaries.Exclusions, context, [new DictRow(null, null, Exclusion("70000000", ""))], [], "M28");
        Assert.Equal(SaveStatus.Rejected, rejected.Status);
        Assert.Single(_service.Rows(ProjectDictionaries.Exclusions, "M28"));
    }
}
