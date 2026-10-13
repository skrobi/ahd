using ClosedXML.Excel;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Files;
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
    /// LOG.WBS: AC-CAB → AC-CAB.6.38 → AC-CAB.6.38.07 („montaż”) → XYZ-1; AC-CAB → AC-CAB.9.01.
    /// </summary>
    private static P1sInputs Inputs() => new(
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
        // AC-CAB.6.38 jest kodem węzła nakładki – nie powtarza się pod korzeniem; AC-CAB.9.01 (bez węzła) – zwykły element pod AC-CAB.
        Assert.Equal(["AC-CAB.9.01"], structure.Rows.Where(r => r.ParentId == Of(structure, "4D06WP").Id && r.Kind == GridRowKind.P1s).Select(r => r.P1s));
        Assert.Single(structure.Rows, r => r.P1s == "XYZ-1");
        Assert.Equal("AC-CAB.6.38.03.01", Of(structure, "4D06WP000004").P1s);   // wiersz węzła – Legacy WBS
    }

    [Fact]
    public void Legacy_wbs_expands_p1s_subtrees()
    {
        var structure = ProjectService.Structure(Objectives(), Inputs(), [], []);

        Assert.Contains("AC-CAB.6.38.07", ChildrenOf(structure, Of(structure, "4D06WP.01")));
        Assert.Contains("AC-CAB.9.01", ChildrenOf(structure, Of(structure, "4D06WP")));   // bez korekty – zwykły element pod AC-CAB
        Assert.Equal("AC-CAB.6.38", ProjectService.Scope(Objectives(), Inputs()).RootOf("XYZ-1"));
    }

    [Fact]
    public void Scope_assigns_log_wbs_element_to_nearest_legacy_wbs()
    {
        var tree = new PoTree();
        var parent = tree.AddElement(null, "4D06WP.RA", "kabina", "AC-CAB.6.38");
        tree.AddElement(parent.Key, "4D06WP000001", "montaż", "AC-CAB.6.38.07");

        Assert.Equal("AC-CAB.6.38.07", ProjectService.Scope(tree, Inputs()).RootOf("XYZ-1"));
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
    public void Actual_dlh_of_ces_elements_goes_to_wp_like_acwp()
    {
        DictRow[] wpCam = [Wp("XYZ-1", "WP-1"), Wp("AC-CAB.6.38.01", "WP-2"), Wp("AC-CAB.6.38.03.01.05", "WP-3")];
        DictRow[] schedule = [Budget("WP-1", "10"), Budget("WP-2", "5"), Budget("WP-3", "1")];
        var costs = new Dictionary<string, decimal> { ["4D06WP000001"] = 100m, ["4D06WP.02"] = 7m };
        var dlh = new Dictionary<string, decimal>
        {
            ["4D06WP000001"] = 40m,    // WP na kodzie P1S węzła (WP-2)
            ["4d06wp.02"] = 3m,        // jedyny WP pod elementem (WP-3), wielkość liter bez znaczenia
            ["4D06WP000002"] = 8m,     // bez WP i bez kosztu – godziny na węźle, bez braku „koszt bez WP”
        };

        var structure = ProjectService.Structure(Objectives(), Inputs(), wpCam, schedule, costs, dlh: dlh);

        Assert.Equal(40m, Of(structure, "4D06WP000001").ActualDlh);
        Assert.Equal(3m, Of(structure, "p1s:AC-CAB.6.38.03.01.05").ActualDlh);   // wiersz WP-3: godziny przypisane do WP
        Assert.Equal(8m, Of(structure, "4D06WP000002").ActualDlh);
        Assert.DoesNotContain("koszt bez WP", Of(structure, "4D06WP000002").Gap ?? "");
        Assert.Equal(51m, Of(structure, "4D06WP").ActualDlh);                     // suma poddrzewa nakładki
        Assert.Equal(100m, Of(structure, "4D06WP000001").Acwp);                   // ACWP bez zmian
        Assert.Equal(0m, ProjectService.Structure(Objectives(), Inputs(), wpCam, schedule, costs).Rows.Sum(r => r.ActualDlh));
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
        // WP niżej nie przeszkadza – nowy WP wiersza ma własny budżet (np. węzeł spinający nad wydzieleniami).
        Assert.True(StructureEdits.CanEditWithNewWp(Of(structure, "p1s:AC-CAB.6.38.07"), StructureEdits.Start));   // WP-1 niżej (XYZ-1)
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
        var structure = StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, schedule,
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
        Assert.Null(StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, schedule).Rows.First().PvHours);
    }

    [Fact]
    public void Split_code_names_the_element_and_the_work_center_group()
    {
        var index = ProductionSplit.Index(Inputs().P1s);
        var split = ProductionSplit.Parse("AC-CAB.6.38.07.PAINT.W20", index)!;
        Assert.Equal(("AC-CAB.6.38.07", "101", "PAINT", "W20"), (split.Element, split.Pspnr, split.Name, split.Cplgr));
        Assert.Equal("XYZ-1", ProductionSplit.Parse("xyz-1.QUALITY.DJ", index)!.Element);   // wielkość liter bez znaczenia
        Assert.Null(ProductionSplit.Parse("AC-CAB.6.38.07", index));                         // element LOG.WBS – nie wydzielenie
        Assert.Null(ProductionSplit.Parse("AC-CAB.6.38.07.PAINT", index));                   // brak CPLGR
        Assert.Null(ProductionSplit.Parse("AC-CAB.6.38.07.PAINT.W20.X", index));             // za dużo członów
        Assert.Null(ProductionSplit.Parse("ZZZ.PAINT.W20", index));                          // element spoza LOG.WBS
    }

    [Fact]
    public void Split_node_has_own_hours_wp_and_budget_and_parent_wp_keeps_the_rest()
    {
        // WP-R na AC-CAB.6.38 (węzeł 4D06WP.RA – „spinający”), pod nim węzeł wirtualny Paint z kodem wydzielenia i własnym WP.
        var tree = Objectives();
        var paintNode = tree.AddVirtual(tree.Nodes.Single(n => n.WbsElement == "4D06WP.RA").Key, "Paint");
        paintNode.LegacyWbs = "AC-CAB.6.38.PAINT.W20";
        DictRow[] wpCam = [Wp("AC-CAB.6.38", "WP-R"), Wp("AC-CAB.6.38.PAINT.W20", "WP-P", cam: "e777")];
        DictRow[] schedule = [Budget("WP-R", "100", null, null, null, "1000"), Budget("WP-P", "50", null, null, null, "500")];
        var production = new ProductionData(new Dictionary<string, ProductionValues>
        {
            ["100"] = new("100", 6, 60, 30, 100, null, new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 25)),            // AC-CAB.6.38 bez Paint
            ["102"] = new("102", 4, 40, 20, null, null, new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 20)),           // XYZ-1 – pod WP-R
            [ProductionData.VirtualKey("AC-CAB.6.38.PAINT.W20")] = new("100", 5, 55, 22, null, "AC-CAB.6.38.PAINT.W20", new DateOnly(2026, 3, 5), null),
        }, DateTimeOffset.Now);
        var structure = StructureBuilder.Build(tree, Inputs().P1s, wpCam, schedule, production: production);

        var paint = Of(structure, $"po:{paintNode.Key}");
        Assert.Equal((true, "AC-CAB.6.38.PAINT.W20", "WP-P", "e777"), (paint.IsVirtual, paint.P1s, paint.Wp, paint.Cam));
        Assert.Contains("wydzielenie z AC-CAB.6.38", paint.Note);
        Assert.Equal((55m, 22m, 5m, "2026-03-05", (string?)null), (paint.OpsBacHours, paint.EvHours, paint.AcHours, paint.ActualStart, paint.ActualFinish));
        Assert.Equal((50m, 500m, "WP-P"), (paint.BacHours, paint.Bac, paint.BudgetWp));

        // WP spinający: własne godziny i budżet (bez Paint, z XYZ-1 bez WP), edytowalny mimo WP-P pod nim.
        var ra = Of(structure, "4D06WP.RA");
        Assert.Equal((100m, 50m, 10m, 100m), (ra.OpsBacHours, ra.EvHours, ra.AcHours, ra.ActualMaterial));
        Assert.Equal((100m, 1000m, "WP-R"), (ra.BacHours, ra.Bac, ra.BudgetWp));
        Assert.Equal(["WP-P", "WP-R"], ra.Wps);
        Assert.True(StructureEdits.CanEdit(ra, StructureEdits.Bac));
        Assert.Equal(200m, paint.EvCost);   // 500 × 22/55
        Assert.Equal(500m, ra.EvCost);      // 1000 × 50/100

        // Węzeł wyżej bez własnego WP – suma obu WP i całego poddrzewa.
        var root = Of(structure, "4D06WP");
        Assert.Equal((155m, 150m, 1500m), (root.OpsBacHours, root.BacHours, root.Bac));
    }

    [Fact]
    public void Split_code_outside_log_wbs_is_a_gap()
    {
        var tree = Objectives();
        var node = tree.AddVirtual(null, "Zły kod");
        node.LegacyWbs = "AC-CAB.6.38.PAINT";
        var structure = ProjectService.Structure(tree, Inputs(), [], []);
        Assert.Contains("kod wydzielenia", Of(structure, $"po:{node.Key}").Gap);
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

    /// <summary>Pobrana struktura uzupełniona w Excelu i wczytana: tylko wyróżnione kolumny, puste komórki bez zmiany.</summary>
    [Fact]
    public void Structure_file_filled_in_excel_gives_wp_cam_and_budget_changes()
    {
        DictRow[] wpCam = [Row(("Element P1S", "XYZ-1"), ("WP", "WP-1"), ("CAM", "e123456"), ("Cost Category", "Production"))];
        DictRow[] schedule = [Budget("WP-1", "10", "100", "2026-01-01", "2026-06-30", "1000")];
        var structure = ProjectService.Structure(Objectives(), Inputs(), wpCam, schedule);
        var persons = new List<LookupOption> { new("e123456", "Anna Nowak"), new("e777", "Jan Kowalski") };
        var path = Path.Combine(Directory.CreateTempSubdirectory("pzl-ev-structure-").FullName, "M28_struktura.xlsx");
        ExcelTableWriter.WriteTemplate(path, [("Struktura", ProjectService.StructureSheetColumns(persons), ProjectService.StructureRows(structure, persons))]);
        var other = structure.Rows.First(r => r.P1s is not null && r.Wp is null && r.P1s != "AC-CAB.9.01");

        using (var workbook = new XLWorkbook(path))
        {
            var sheet = workbook.Worksheet("Struktura");
            var header = ProjectService.StructureHeaders.ToList();
            Assert.Equal(XLColor.FromHtml("#FFF2CC"), sheet.Cell(1, header.IndexOf("WP") + 1).Style.Fill.BackgroundColor);   // do uzupełnienia
            IXLCell Cell(string p1s, string column) =>
                sheet.Cell(sheet.RowsUsed().Single(r => r.Cell(header.IndexOf("Legacy Element (P1S)") + 1).GetString() == p1s).RowNumber(), header.IndexOf(column) + 1);
            Cell("AC-CAB.9.01", "WP").Value = "x";                      // nowy WP z CAM, budżetem i datami
            Cell("AC-CAB.9.01", "CAM").Value = "e777";
            Cell("AC-CAB.9.01", "BAC Hours baseline").Value = 20;
            Cell("AC-CAB.9.01", "BAC Cost").Value = "1 500,50";
            Cell("AC-CAB.9.01", "Baseline Start").Value = new DateTime(2026, 2, 1);
            Cell("XYZ-1", "BAC Hours baseline").Value = 12;             // zmiana budżetu istniejącego WP; reszta bez zmian
            Cell("XYZ-1", "BAC Material").Value = "abc";                // błędna liczba – komórka pominięta
            Cell(other.P1s!, "BAC Hours baseline").Value = 5;           // bez WP – pominięte
            Cell(other.P1s!, "ACWP").Value = 999;                       // kolumna niewczytywana
            var last = sheet.LastRowUsed()!.RowNumber() + 1;
            sheet.Cell(last, header.IndexOf("Legacy Element (P1S)") + 1).Value = "NIE-MA";
            sheet.Cell(last, header.IndexOf("WP") + 1).Value = "x";
            workbook.Save();
        }

        var result = StructureImport.Read(TabularFileReader.Read(path, "Struktura"), structure, schedule);

        var added = result.Edits.Single(e => e.Row.P1s == "AC-CAB.9.01").Changes;
        Assert.Equal(("true", "e777", "20", "1500.5", "2026-02-01"),
            (added[StructureEdits.Wp], added[StructureEdits.Cam], added[StructureEdits.BacHours], added[StructureEdits.Bac], added[StructureEdits.Start]));
        Assert.False(added.ContainsKey(StructureEdits.Finish));      // pusta komórka – bez zmiany
        var changed = result.Edits.Single(e => e.Row.P1s == "XYZ-1").Changes;
        Assert.Equal(["BacHours"], changed.Keys);                    // CAM, Cost Category, pozostały budżet – bez zmian
        Assert.Equal("12", changed[StructureEdits.BacHours]);
        Assert.Equal(2, result.Edits.Count);
        Assert.Contains(result.Issues, i => i.Level == CheckLevel.Error && i.Message.StartsWith("BAC Material „abc”"));
        Assert.Contains(result.Issues, i => i.Level == CheckLevel.Warning && i.Message.StartsWith($"BAC Hours baseline przy elemencie {other.P1s} bez WP"));
        Assert.Contains(result.Issues, i => i.Level == CheckLevel.Warning && i.Message.StartsWith("Elementu P1S NIE-MA nie ma w strukturze"));
        Assert.Equal((1, 1), (result.Count(StructureEdits.Wp), result.Count(StructureEdits.Cam)));
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
