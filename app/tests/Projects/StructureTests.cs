using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Struktura projektu na ekranie Projekt (docs/performance-objectives.md, rozdz. 4.2): rozwinięcie P1S, sumy, dokładanie, edycja.</summary>
public sealed class StructureTests
{
    private static PoTree Objectives() => PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx")).Tree;

    private static P1sElement P1s(string pspnr, string parent, string wbs, string name = "") =>
        new(pspnr, parent, null, wbs, "AC-CAB", "AC-CAB", "kabina", name, "", "", "", "", "X", "");

    /// <summary>
    /// Raport: 4D06WP.RA → AC-CAB.6.38; korekta 4D06WP000004 → AC-CAB.9.01. LOG.WBS: AC-CAB → AC-CAB.6.38 →
    /// AC-CAB.6.38.07 („montaż”) → XYZ-1; AC-CAB → AC-CAB.9.01.
    /// </summary>
    private static MappingInputs Inputs(bool withReport = true) => new(
        withReport
            ? new ReportInfo(DateTimeOffset.Now, "PZL\\anowak",
                [new ReportEntry(2, "CES", "70002", "4D06WP.RA", "4D06WP", "100", "AC-CAB.6.38", "")])
            : null,
        withReport
            ? [new CorrectionRow(1, 10, 1, CorrectionKinds.Element, "4D06WP000004", "200", "AC-CAB.9.01", null, "zmiana", DateTime.Today, null,
                DateTimeOffset.Now, "PZL\\anowak", null, null)]
            : [],
        [P1s("1", "", "AC-CAB"), P1s("100", "1", "AC-CAB.6.38"), P1s("101", "100", "AC-CAB.6.38.07", "montaż"), P1s("102", "101", "XYZ-1"), P1s("200", "1", "AC-CAB.9.01")],
        null);

    private static DictRow Row(params (string Column, string? Value)[] values) => new(null, null, values.ToDictionary(v => v.Column, v => v.Value));

    private static DictRow Wp(string element, string wp, string cam = "Anna Nowak") => Row(("Element P1S", element), ("WP", wp), ("CAM", cam), ("Cost Category", null));

    private static DictRow Budget(string wp, string? hours, string? material = null, string? start = null, string? end = null, string? bac = null) =>
        Row(("WP", wp), ("BAC HOURS", hours), ("BAC MATERIAL", material), ("BAC", bac), ("Baseline Start", start), ("Baseline Koniec", end));

    private static StructureRow Of(ProjectStructure structure, string idOrWbs) =>
        structure.Rows.Single(r => r.WbsElement == idOrWbs || r.Id == idOrWbs);

    private static IReadOnlyList<string> ChildrenOf(ProjectStructure structure, StructureRow row) =>
        structure.Rows.Where(r => r.ParentId == row.Id).Select(r => r.WbsElement ?? r.P1s ?? r.Name).ToList();

    [Fact]
    public void P1s_tree_expands_under_deepest_objective_and_stops_at_other_objectives()
    {
        var structure = ProjectService.Structure(Objectives(), Inputs(), [], []);

        // AC-CAB.6.38 to Legacy WBS 4D06WP.RA i 4D06WP.01 – jego dziecko w LOG.WBS trafia pod głębszy węzeł (.01).
        var deeper = Of(structure, "4D06WP.01");
        Assert.Contains("AC-CAB.6.38.07", ChildrenOf(structure, deeper));
        var assembly = Of(structure, "p1s:AC-CAB.6.38.07");
        Assert.Equal((GridRowKind.P1s, "montaż", 3), (assembly.Kind, assembly.Name, assembly.Depth));
        Assert.Equal(["XYZ-1"], ChildrenOf(structure, assembly));
        Assert.DoesNotContain("AC-CAB.6.38.07", ChildrenOf(structure, Of(structure, "4D06WP.RA")));
        // AC-CAB ma w LOG.WBS dzieci AC-CAB.6.38 i AC-CAB.9.01 – oba są kodami węzłów nakładki, więc nie powtarzają się pod korzeniem.
        Assert.DoesNotContain(structure.Rows, r => r.ParentId == Of(structure, "4D06WP").Id && r.Kind == GridRowKind.P1s);
        // Cel mapowania inny niż Legacy WBS – osobny wiersz pod węzłem.
        var target = Of(structure, "p1s:AC-CAB.9.01");
        Assert.Equal((Of(structure, "4D06WP000004").Id, "cel mapowania CES ↔ P1S"), (target.ParentId, target.Note));
        Assert.Single(structure.Rows, r => r.P1s == "XYZ-1");
        Assert.Equal("AC-CAB.6.38.03.01", Of(structure, "4D06WP000004").P1s);   // wiersz węzła – Legacy WBS
    }

    [Fact]
    public void Legacy_wbs_alone_expands_without_mapping_report()
    {
        var structure = ProjectService.Structure(Objectives(), Inputs(withReport: false), [], []);

        Assert.Contains("AC-CAB.6.38.07", ChildrenOf(structure, Of(structure, "4D06WP.01")));
        Assert.Contains("AC-CAB.9.01", ChildrenOf(structure, Of(structure, "4D06WP")));   // bez korekty – zwykły element pod AC-CAB
        Assert.Equal("AC-CAB.6.38", ProjectService.Scope(Objectives(), Inputs(withReport: false)).RootOf("XYZ-1"));
    }

    [Fact]
    public void Scope_assigns_log_wbs_element_to_nearest_legacy_wbs()
    {
        var tree = new PoTree();
        var parent = tree.AddElement(null, "4D06WP.RA", "kabina", "AC-CAB.6.38");
        tree.AddElement(parent.Key, "4D06WP000001", "montaż", "AC-CAB.6.38.07");

        Assert.Equal("AC-CAB.6.38.07", ProjectService.Scope(tree, Inputs(withReport: false)).RootOf("XYZ-1"));
    }

    [Fact]
    public void Wp_budget_and_gaps_roll_up_subtree()
    {
        DictRow[] wpCam = [Wp("XYZ-1", "WP-1"), Wp("AC-CAB.6.38.01", "WP-2"), Wp("AC-CAB.6.38.03.01.05", "WP-3"), Wp("ZZZ", "WP-4")];
        DictRow[] schedule = [Budget("WP-1", "10", "100", "2026-01-01", "2026-06-30", "1000"), Budget("WP-2", "5", null, "2025-12-01", "2026-03-31", "250.5")];

        var structure = ProjectService.Structure(Objectives(), Inputs(), wpCam, schedule);
        Assert.Equal((1000m, 1250.5m, 1250.5m), (Of(structure, "p1s:XYZ-1").Bac, Of(structure, "4D06WP.RA").Bac, structure.Summary.Bac));   // BAC – suma poddrzewa

        var leaf = Of(structure, "p1s:XYZ-1");
        Assert.Equal(("WP-1", "Anna Nowak", 10m, 100m, true), (leaf.Wp, leaf.Cam, leaf.BacHours, leaf.BacMaterial, leaf.OwnsBudget));
        var ra = Of(structure, "4D06WP.RA");
        Assert.Equal(["WP-1", "WP-2", "WP-3"], ra.Wps);   // .02 leży pod .RA
        Assert.Equal((15m, "2025-12-01", "2026-06-30"), (ra.BacHours, ra.Start, ra.Finish));
        Assert.False(ra.OwnsBudget);
        Assert.True(Of(structure, "4D06WP000001").OwnsBudget);   // Legacy WBS węzła ma własny WP
        Assert.Equal("element nakładki bez WP", Of(structure, "4D06WP000002").Gap);
        Assert.Equal("WP bez budżetu", Of(structure, "4D06WP.02").Gap);   // WP-3 bez harmonogramu
        // Element spoza LOG.WBS – pod najdłuższy pasujący kod; bez pasującego kodu – grupa spoza struktury.
        var outsideLog = Of(structure, "p1s:AC-CAB.6.38.03.01.05");
        Assert.Equal((Of(structure, "4D06WP000004").Id, "spoza LOG.WBS"), (outsideLog.ParentId, outsideLog.Note));
        Assert.Equal(StructureBuilder.OutsideId, Of(structure, "p1s:ZZZ").ParentId);
        Assert.Equal(["WP-1", "WP-2", "WP-3"], Of(structure, "4D06WP").Wps);
        Assert.Equal((4, 1, 15m, 100m, "2025-12-01", "2026-06-30"),
            (structure.Summary.Wps, structure.Summary.Cams, structure.Summary.BacHours, structure.Summary.BacMaterial, structure.Summary.Start, structure.Summary.Finish));
        Assert.Equal(2, structure.Summary.WpsWithoutBudget);
    }

    [Fact]
    public void Actual_cost_of_ces_elements_goes_to_wp_or_to_gaps()
    {
        DictRow[] wpCam = [Wp("XYZ-1", "WP-1"), Wp("AC-CAB.6.38.01", "WP-2"), Wp("AC-CAB.6.38.03.01.05", "WP-3")];
        DictRow[] schedule = [Budget("WP-1", "10"), Budget("WP-2", "5"), Budget("WP-3", "1")];
        var costs = new Dictionary<string, decimal>
        {
            ["4D06WP000001"] = 100m,   // WP na kodzie P1S węzła (WP-2)
            ["4d06wp000002"] = 30m,    // bez WP (wielkość liter bez znaczenia)
            ["4D06WP.RA"] = 50m,       // trzy WP pod elementem – niejednoznaczny
            ["4D06WP.02"] = 7m,        // jedyny WP pod elementem (WP-3)
            ["XXX-SPOZA"] = 5m,        // element CES projektu spoza nakładki
        };

        var structure = ProjectService.Structure(Objectives(), Inputs(), wpCam, schedule, costs);

        Assert.Equal(100m, Of(structure, "4D06WP000001").Acwp);
        Assert.Contains("koszt bez WP (30)", Of(structure, "4D06WP000002").Gap);
        Assert.Contains("koszt niejednoznaczny (50) – 3 WP", Of(structure, "4D06WP.RA").Gap);
        Assert.Equal(7m, Of(structure, "p1s:AC-CAB.6.38.03.01.05").Acwp);   // wiersz WP-3: koszt przypisany do WP
        Assert.Equal((187m, 80m, 5m, true), (structure.Summary.Acwp, structure.Summary.AcwpWithoutWp, structure.Summary.AcwpOutside, structure.Summary.HasCosts));
        Assert.False(ProjectService.Structure(Objectives(), Inputs(), wpCam, schedule).Summary.HasCosts);
    }

    [Fact]
    public void Editable_cells_depend_on_row()
    {
        var structure = ProjectService.Structure(Objectives(), Inputs(), [Wp("XYZ-1", "WP-1")], [Budget("WP-1", "10")]);
        var node = Of(structure, "4D06WP.01");
        var leaf = Of(structure, "p1s:XYZ-1");

        Assert.True(StructureEdits.CanEdit(node, StructureEdits.Name));
        Assert.True(StructureEdits.CanEdit(node, StructureEdits.P1s));
        Assert.True(StructureEdits.CanEdit(node, StructureEdits.Wp));
        Assert.True(StructureEdits.CanEdit(leaf, StructureEdits.CostCategory));
        Assert.True(StructureEdits.CanEdit(node, StructureEdits.BacHours));   // jeden WP w poddrzewie (WP-1)
        var two = ProjectService.Structure(Objectives(), Inputs(), [Wp("XYZ-1", "WP-1"), Wp("AC-CAB.6.38.01", "WP-2")], []);
        Assert.False(StructureEdits.CanEdit(Of(two, "4D06WP.RA"), StructureEdits.BacHours));   // suma kilku WP
        Assert.False(StructureEdits.CanEdit(leaf, StructureEdits.Name));
        Assert.True(StructureEdits.CanEdit(leaf, StructureEdits.BacHours));
        // Wiersz bez własnego WP z jednym WP pod sobą – budżet i daty edytowalne (zmiana trafia do tego WP).
        var parent = Of(structure, "p1s:AC-CAB.6.38.07");
        Assert.Equal(("WP-1", true, true), (parent.BudgetWp, StructureEdits.CanEdit(parent, StructureEdits.Start), StructureEdits.CanEdit(parent, StructureEdits.Bac)));
        Assert.True(StructureEdits.CanEdit(leaf, StructureEdits.Bac));
        Assert.False(StructureEdits.CanEdit(Of(ProjectService.Structure(Objectives(), Inputs(), [], []), "p1s:AC-CAB.6.38.07"), StructureEdits.Start));   // bez WP w poddrzewie
        // WP zaznaczany w tym wierszu (wklejenie WP z budżetem): budżet edytowalny, gdy w poddrzewie nie ma innych WP.
        var empty = ProjectService.Structure(Objectives(), Inputs(), [], []);
        Assert.True(StructureEdits.CanEditWithNewWp(Of(empty, "p1s:AC-CAB.6.38.07"), StructureEdits.Start));
        Assert.False(StructureEdits.CanEditWithNewWp(Of(empty, "p1s:AC-CAB.6.38.07"), StructureEdits.Cam));
        Assert.False(StructureEdits.CanEditWithNewWp(Of(structure, "p1s:AC-CAB.6.38.07"), StructureEdits.Start));   // WP-1 niżej (XYZ-1)
        Assert.False(StructureEdits.CanEditWithNewWp(node, StructureEdits.BacHours));   // WP w poddrzewie
        Assert.False(StructureEdits.CanEditWithNewWp(leaf, StructureEdits.BacHours));   // ma już WP
    }

    [Fact]
    public void Wp_cam_and_schedule_rows_change_add_and_remove()
    {
        DictRow[] wpCam = [Wp("XYZ-1", "WP-1") with { RowId = 1, Version = 1 }];

        // Zaznaczenie WP – kodem WP jest kod elementu P1S.
        var (added, _) = StructureEdits.WpCam(wpCam, "AC-CAB.6.38.07", new Dictionary<string, string?> { [StructureEdits.Wp] = "true", [StructureEdits.Cam] = "e123456" });
        Assert.Equal(2, added.Count);
        Assert.Equal(("AC-CAB.6.38.07", "AC-CAB.6.38.07", "e123456"), (added[1]["Element P1S"], added[1]["WP"], added[1]["CAM"]));

        // Zmiana CAM – dotychczasowy kod WP zostaje.
        var (changed, none) = StructureEdits.WpCam(wpCam, "xyz-1", new Dictionary<string, string?> { [StructureEdits.Cam] = "e123456" });
        Assert.Equal((1L, "WP-1", "e123456"), (changed[0].RowId!.Value, changed[0]["WP"], changed[0]["CAM"]));
        Assert.Empty(none);

        // Wybór CAM w wierszu bez WP zaznacza WP.
        var (camOnly, _) = StructureEdits.WpCam(wpCam, "AC-CAB.6.38.07", new Dictionary<string, string?> { [StructureEdits.Cam] = "e123456" });
        Assert.Equal("AC-CAB.6.38.07", camOnly[1]["WP"]);

        // Cost Category (kategoria WBS projektu) – zapisywana w „WP i CAM” przy WP elementu; odczyt w strukturze.
        var (categorized, _) = StructureEdits.WpCam(wpCam, "XYZ-1", new Dictionary<string, string?> { [StructureEdits.CostCategory] = "Production" });
        Assert.Equal(("WP-1", "Production"), (categorized[0]["WP"], categorized[0]["Cost Category"]));
        Assert.Equal("Production", Of(ProjectService.Structure(Objectives(), Inputs(), categorized, []), "p1s:XYZ-1").CostCategory);

        // Odznaczenie WP – przypisanie usunięte.
        var (cleared, removed) = StructureEdits.WpCam(wpCam, "XYZ-1", new Dictionary<string, string?> { [StructureEdits.Wp] = "false" });
        Assert.Empty(cleared);
        Assert.Equal(1L, Assert.Single(removed).RowId);

        DictRow[] schedule = [Budget("WP-1", "10", "5") with { RowId = 3, Version = 1 }];
        var (budget, _) = StructureEdits.Schedule(schedule, "wp-1", new Dictionary<string, string?> { [StructureEdits.BacHours] = "12,5", [StructureEdits.Bac] = "900" });
        Assert.Equal(("12,5", "5", "900"), (budget[0]["BAC HOURS"], budget[0]["BAC MATERIAL"], budget[0]["BAC"]));

        var (renamed, kept) = StructureEdits.ScheduleAfterWpChange(schedule, "WP-1", "WP-9");
        Assert.Equal(("WP-9", "10"), (renamed[0]["WP"], renamed[0]["BAC HOURS"]));
        Assert.Empty(kept);
        var (dropped, gone) = StructureEdits.ScheduleAfterWpChange([.. schedule, Budget("WP-9", "1")], "WP-1", "WP-9");
        Assert.Equal(["WP-9"], dropped.Select(r => r["WP"]));
        Assert.Single(gone);
    }

    [Fact]
    public void Planned_value_spreads_budget_over_working_days_of_baseline()
    {
        Assert.Equal(5, EarnedValue.WorkingDays(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11)));    // pn–nd
        Assert.Equal(10, EarnedValue.WorkingDays(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 16)));
        Assert.Equal(0, EarnedValue.WorkingDays(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 11)));   // weekend
        Assert.Equal(0m, EarnedValue.Elapsed("2026-10-05", "2026-10-16", new DateOnly(2026, 10, 1)));       // przed startem
        Assert.Equal(0.5m, EarnedValue.Elapsed("2026-10-05", "2026-10-16", new DateOnly(2026, 10, 11)));    // 5 z 10 dni roboczych
        Assert.Equal(1m, EarnedValue.Elapsed("2026-10-05", "2026-10-16", new DateOnly(2026, 11, 1)));       // po końcu
        Assert.Null(EarnedValue.Elapsed(null, "2026-10-16", new DateOnly(2026, 10, 11)));                   // brak daty
        Assert.Null(EarnedValue.Elapsed("2026-10-16", "2026-10-05", new DateOnly(2026, 10, 11)));           // zła kolejność
    }

    [Fact]
    public void Production_values_roll_up_and_give_planned_and_earned_value_of_wp()
    {
        // WP-1 na AC-CAB.6.38.07 (PSPNR 101) – pod nim XYZ-1 (102); produkcja na obu elementach.
        DictRow[] wpCam = [Wp("AC-CAB.6.38.07", "WP-1")];
        DictRow[] schedule = [Budget("WP-1", "100", null, "2026-10-05", "2026-10-16", "10000")];
        var production = new ProductionData(new Dictionary<string, ProductionValues>
        {
            ["101"] = new("101", AcHours: 10, BacHours: 60, EvHours: 20, ActualMaterial: 500),
            ["102"] = new("102", AcHours: 5, BacHours: 40, EvHours: 30, ActualMaterial: null),
        }, DateTimeOffset.Now);
        var structure = StructureBuilder.Build(Objectives(), ProjectService.Resolve(Objectives(), Inputs()), Inputs().P1s, wpCam, schedule,
            production: production, statusDate: new DateOnly(2026, 10, 11));

        var leaf = Of(structure, "p1s:XYZ-1");
        Assert.Equal((40m, 30m, 5m, (decimal?)null), (leaf.OpsBacHours, leaf.EvHours, leaf.AcHours, leaf.ActualMaterial));
        var wp = Of(structure, "p1s:AC-CAB.6.38.07");
        Assert.Equal((100m, 50m, 15m, 500m), (wp.OpsBacHours, wp.EvHours, wp.AcHours, wp.ActualMaterial));   // suma poddrzewa
        Assert.Equal(50m, wp.PvHours);      // BAC Hours (AHD) 100 × 5/10 dni roboczych
        Assert.Equal(5000m, wp.PvCost);     // BAC Cost 10 000 × 0,5
        Assert.Equal(5000m, wp.EvCost);     // BAC Cost × EV/BAC = 10 000 × 50/100
        Assert.Equal((100m, 50m, 5000m), (Of(structure, "4D06WP.RA").OpsBacHours, Of(structure, "4D06WP.RA").PvHours, Of(structure, "4D06WP.RA").EvCost));
        Assert.Null(Of(structure, "4D06WP000002").OpsBacHours);   // element bez danych produkcyjnych – puste, nie 0
        Assert.Null(StructureBuilder.Build(Objectives(), ProjectService.Resolve(Objectives(), Inputs()), Inputs().P1s, wpCam, schedule).Rows.First().PvHours);
    }

    [Fact]
    public void Structure_export_has_whole_tree_with_all_columns()
    {
        DictRow[] wpCam = [Row(("Element P1S", "XYZ-1"), ("WP", "WP-1"), ("CAM", "e123456"), ("Cost Category", "Production"))];
        DictRow[] schedule = [Budget("WP-1", "10", "100", "2026-01-01", "2026-06-30", "1000")];
        var structure = ProjectService.Structure(Objectives(), Inputs(), wpCam, schedule);
        var rows = ProjectService.StructureRows(structure, [new LookupOption("e123456", "Anna Nowak")]).ToList();

        Assert.Equal(structure.Rows.Count, rows.Count);   // także wiersze zwinięte w tabeli
        Assert.All(rows, r => Assert.Equal(ProjectService.StructureHeaders.Count, r.Count));
        var leaf = rows[structure.Rows.ToList().FindIndex(r => r.Id == "p1s:XYZ-1")];
        var header = ProjectService.StructureHeaders.ToList();
        object? Cell(string column) => leaf[header.IndexOf(column)];
        Assert.Equal(("element P1S", "WP-1", "e123456", "Anna Nowak", "Production"), (Cell("Rodzaj"), Cell("WP"), Cell("CAM"), Cell("CAM – imię i nazwisko"), Cell("Cost Category")));
        Assert.Equal((10m, 100m, 1000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)),
            (Cell("BAC Hours baseline"), Cell("BAC Material"), Cell("BAC Cost"), Cell("Baseline Start"), Cell("Baseline Finish")));
        Assert.StartsWith(new string(' ', structure.Rows.Single(r => r.Id == "p1s:XYZ-1").Depth * 2), (string)Cell("WBS Name")!);
        // Wiersz bez WP w poddrzewie – budżet pusty (nie 0).
        var empty = rows[structure.Rows.ToList().FindIndex(r => r.Wps.Count == 0)];
        Assert.Null(empty[header.IndexOf("BAC Hours baseline")]);
    }

    [Fact]
    public void Next_project_definition_is_added_without_touching_existing_nodes()
    {
        var current = Objectives();
        var group = current.AddVirtual(null, "Grupa raportowa");
        current.Nodes.Single(n => n.WbsElement == "4D06WP.02").Name = "Kabina – zmieniona nazwa";

        var imported = new PoTree();
        var existingParent = imported.Add(new PoNode { Key = imported.NewKey(), WbsElement = "4D06WP.02", Name = "Cabin", ProjectDefinition = "4D06WP" });
        imported.Add(new PoNode { Key = imported.NewKey(), ParentKey = existingParent.Key, WbsElement = "4D06WP000099", Name = "nowy", ProjectDefinition = "4D06WP" });
        var root = imported.Add(new PoNode { Key = imported.NewKey(), WbsElement = "4D06WQ", Name = "projekt produkcyjny kabiny", ProjectDefinition = "4D06WQ" });
        imported.Add(new PoNode { Key = imported.NewKey(), ParentKey = root.Key, WbsElement = "4D06WQ.RA", Name = "Kabina MY10 #65", ProjectDefinition = "4D06WQ", LegacyWbs = "AC-CAB.6.39" });

        var (tree, count, projects) = ProjectService.AddNew(current, imported);

        Assert.Equal(3, count);
        Assert.Equal(["4D06WP", "4D06WQ"], projects);
        var parent = tree.Nodes.Single(n => n.WbsElement == "4D06WP.02");
        Assert.Equal("Kabina – zmieniona nazwa", parent.Name);
        Assert.Equal(parent.Key, tree.Nodes.Single(n => n.WbsElement == "4D06WP000099").ParentKey);
        var newRoot = tree.Nodes.Single(n => n.WbsElement == "4D06WQ");
        Assert.Null(newRoot.ParentKey);
        Assert.Equal(("AC-CAB.6.39", newRoot.Key), (tree.Nodes.Single(n => n.WbsElement == "4D06WQ.RA").LegacyWbs, tree.Nodes.Single(n => n.WbsElement == "4D06WQ.RA").ParentKey));
        Assert.NotNull(tree.Find(group.Key));
        Assert.Equal(current.Nodes.Count + 3, tree.Nodes.Count);
        Assert.Equal(0, ProjectService.AddNew(tree, imported).Added);
    }
}
