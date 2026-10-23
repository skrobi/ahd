using ClosedXML.Excel;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Data;
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

    /// <summary>Wiersz „Kursy walut”: waluta, obowiązuje od – do, kurs (kurs CAS – opcjonalny).</summary>
    private static DictRow Fx(string currency, string from, string? to, string? rate, string? cas = null) =>
        Row(("Waluta", currency), ("Od", from), ("Do", to), ("Kurs", rate), ("Kurs CAS", cas));

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

    /// <summary>Kod wydzielenia elementu LOG.WBS bez kropkowego prefiksu węzła (XYZ-1 pod AC-CAB.6.38.07) – w zakresie elementu.</summary>
    [Fact]
    public void Split_code_of_log_wbs_element_is_in_scope_of_its_element()
    {
        var scope = ProjectService.Scope(Objectives(), Inputs());

        Assert.Equal("AC-CAB.6.38", scope.RootOf("XYZ-1.QUALITY.DJ"));
        Assert.Null(scope.RootOf("XYZ-1.QUALITY"));       // jeden człon za elementem – nie wydzielenie
        Assert.Null(scope.RootOf("ZZZ.PAINT.W20"));       // element spoza zakresu
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
        // Przypisanie elementów (łączenie źródeł w przebiegu): każdy element kosztów raz – WP albo powód braku.
        Assert.Equal(
            [("4D06WP.02", "WP-3", null), ("4D06WP.RA", null, CostGaps.Ambiguous), ("4D06WP000001", "WP-2", null), ("4D06WP000002", null, CostGaps.NoWp),
             ("XXX-SPOZA", null, CostGaps.Outside)],
            structure.CostTargets!.OrderBy(t => t.WbsElement, StringComparer.OrdinalIgnoreCase).Select(t => (t.WbsElement, t.Wp, t.Gap)));
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
        Assert.Equal((75m, 50m), (leaf.PercentComplete, wp.PercentComplete));   // zaawansowanie: EV ÷ TECH Hours × 100
        Assert.Null(Of(structure, "4D06WP000002").PercentComplete);              // bez godzin TECH – puste
        Assert.Equal(50m, wp.PvHours);      // BAC Hours (AHD) 100 × 5/10 dni roboczych
        Assert.Equal(5000m, wp.PvCost);     // BAC Cost 10 000 × 0,5
        Assert.Equal(5000m, wp.EvCost);     // BAC Cost × EV/BAC = 10 000 × 50/100
        Assert.Equal((100m, 50m, 5000m), (Of(structure, "4D06WP.RA").OpsBacHours, Of(structure, "4D06WP.RA").PvHours, Of(structure, "4D06WP.RA").EvCost));
        Assert.Null(Of(structure, "4D06WP000002").OpsBacHours);   // element bez danych produkcyjnych – puste, nie 0
        Assert.Null(StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, schedule).Rows.First().PvHours);
    }

    [Fact]
    public void Actual_dates_entered_for_wp_override_production_dates()
    {
        // WP-1 na AC-CAB.6.38.07 (PSPNR 101, pod nim XYZ-1 – 102): produkcja z otwartym zleceniem; WP-2 na AC-CAB.6.38.01 bez produkcji.
        DictRow[] wpCam = [Wp("AC-CAB.6.38.07", "WP-1"), Wp("AC-CAB.6.38.01", "WP-2")];
        var production = new ProductionData(new Dictionary<string, ProductionValues>
        {
            ["101"] = new("101", 10, 60, 20, null, null, new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30)),
            ["102"] = new("102", 5, 40, 30, null, null, new DateOnly(2026, 1, 15), null),   // operacja niezamknięta
        }, DateTimeOffset.Now);
        DictRow[] Schedule(string? start1, string? end1, string? start2, string? end2) =>
        [
            Row(("WP", "WP-1"), ("BAC HOURS", "1"), ("Actual Start", start1), ("Actual Koniec", end1)),
            Row(("WP", "WP-2"), ("BAC HOURS", "1"), ("Actual Start", start2), ("Actual Koniec", end2)),
        ];

        // Bez wpisanych dat – z produkcji: start najwcześniejszy, koniec pusty (zlecenie otwarte); WP-2 – puste.
        var structure = StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, Schedule(null, null, null, null), production: production);
        var wp1 = Of(structure, "p1s:AC-CAB.6.38.07");
        Assert.Equal(("2026-01-15", (string?)null, false, false), (wp1.ActualStart, wp1.ActualFinish, wp1.ActualStartManual, wp1.ActualFinishManual));
        Assert.Equal(((string?)null, (string?)null), (Of(structure, "4D06WP000001").ActualStart, Of(structure, "4D06WP000001").ActualFinish));

        // Wpisany koniec WP-1 (prace zakończone, operacja niezamknięta) i daty WP-2 podane przez CAM.
        structure = StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, Schedule(null, "2026-05-10", "2026-03-01", "2026-03-20"), production: production);
        wp1 = Of(structure, "p1s:AC-CAB.6.38.07");
        Assert.Equal(("2026-01-15", "2026-05-10", false, true), (wp1.ActualStart, wp1.ActualFinish, wp1.ActualStartManual, wp1.ActualFinishManual));
        var wp2 = Of(structure, "4D06WP000001");   // węzeł nakładki z jedynym WP-2 pod nim – daty WP-2
        Assert.Equal(("2026-03-01", "2026-03-20", true, true), (wp2.ActualStart, wp2.ActualFinish, wp2.ActualStartManual, wp2.ActualFinishManual));
        var root = Of(structure, "4D06WP");          // kilka WP – najwcześniejszy start, najpóźniejszy koniec, gdy wszystkie WP zakończone
        Assert.Equal(("2026-01-15", "2026-05-10", false), (root.ActualStart, root.ActualFinish, root.ActualFinishManual));

        // Wpisany sam start WP-2 (bez końca i bez produkcji) – WP trwa, wiersz nadrzędny bez daty końca.
        structure = StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, Schedule(null, "2026-05-10", "2026-03-01", null), production: production);
        Assert.Null(Of(structure, "4D06WP").ActualFinish);
    }

    [Fact]
    public void Actual_dates_go_to_schedule_dictionary_and_are_validated()
    {
        var (working, _) = StructureEdits.Schedule([Budget("WP-1", "10")], "WP-1",
            new Dictionary<string, string?> { [StructureEdits.ActualStart] = "2026-03-01", [StructureEdits.ActualFinish] = "2026-03-20" });
        Assert.Equal(("2026-03-01", "2026-03-20", "10"), (working[0]["Actual Start"], working[0]["Actual Koniec"], working[0]["BAC HOURS"]));
        (working, _) = StructureEdits.Schedule(working, "WP-1", new Dictionary<string, string?> { [StructureEdits.ActualFinish] = "" });
        Assert.Null(working[0]["Actual Koniec"]);   // wyczyszczenie – data z produkcji

        var spec = ProjectDictionaries.For(ProjectDictionaries.ScheduleBudget,
            new ProjectDictionaryContext(new P1sScope([], []), new Dictionary<string, string>(), new HashSet<string>(), new HashSet<string> { "WP-1" }));
        var issues = PzlEv.Shared.Utils.Dictionaries.DictionaryValidator.Validate(spec,
            [Row(("WP", "WP-1"), ("BAC HOURS", "1"), ("Actual Start", "2026-04-01"), ("Actual Koniec", "2026-03-01"))]);
        Assert.Contains(issues, i => i.Message.Contains("Actual Start jest późniejszy niż Actual Koniec"));
    }

    /// <summary>Wskaźniki EVM wiersza WP: godziny i koszty z produkcji, ACTUALS i budżetu; ETC z produkcji albo wpisane; ETC Cost ze stawki MPK.</summary>
    /// <summary>
    /// Estymacja CAM na strukturze (ProjectService.WithCamEstimates): wymagany WP (z CAM-em, niezakończony) – kolor statusu, brak i szkic żółte
    /// dopiero po terminie cyklu; WP bez CAM albo zakończony – tylko opis zapisanej estymacji, bez koloru; wiersz bez WP – bez zmian.
    /// </summary>
    [Fact]
    public void Cam_estimates_color_work_package_rows_by_status_and_deadline()
    {
        var built = StructureBuilder.Build(Objectives(), Inputs().P1s, [Wp("AC-CAB.6.38.07", "WP-1")], []);
        var template = Of(built, "p1s:AC-CAB.6.38.07") with { Status = "w toku" };
        var plain = built.Rows.First(r => r.Wp is null);
        StructureRow WpRow(string wp, string? cam = "0412", string status = "w toku") => template with { Id = wp, Wp = wp, Cam = cam, Status = status };
        var structure = new ProjectStructure([plain, WpRow("A"), WpRow("S"), WpRow("R"), WpRow("D"), WpRow("M"), WpRow("F", status: "zakończony"), WpRow("N", cam: null)],
            built.Summary);
        static CamReviewItem Item(string wp, string status) => new("M28", wp, "2026-10", wp, "0412", false, status, 0, 0, "PLN", null, null, null,
            new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), "e1", new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero), "fin", null);
        IReadOnlyList<CamReviewItem> saved = [Item("a", CamStatuses.Approved), Item("S", CamStatuses.Sent), Item("R", CamStatuses.Returned), Item("D", CamStatuses.Draft),
            Item("F", CamStatuses.Approved)];
        var cycle = new CamCycle("2026-10", new DateOnly(2026, 10, 30));

        var before = ProjectService.WithCamEstimates(structure, cycle, saved, new DateOnly(2026, 10, 25));
        var after = ProjectService.WithCamEstimates(structure, cycle, saved, new DateOnly(2026, 11, 2));

        static string Tones(ProjectStructure s) => string.Join(" ", s.Rows.Select(r => r.CamTone ?? "-"));
        Assert.Equal("- ok info crit - - - -", Tones(before));            // przed terminem: szkic i brak bez koloru
        Assert.Equal("- ok info crit warn warn - -", Tones(after));       // po terminie – żółte
        Assert.Equal("Zatwierdzona 2026-10-21 · cykl 2026-10", before.Rows[1].CamEstimate);            // WP bez względu na wielkość liter
        Assert.Equal(("Brak – po terminie · cykl 2026-10", "Zatwierdzona 2026-10-21 (niewymagana)"), (after.Rows[5].CamEstimate, after.Rows[6].CamEstimate));
        Assert.Null(after.Rows[7].CamEstimate);                                                          // WP bez CAM, bez estymacji
        Assert.Same(plain, after.Rows[0]);
    }

    [Fact]
    public void Evm_columns_of_wp_use_production_actuals_budget_and_mpk_rate()
    {
        // WP-1 na AC-CAB.6.38.07 (PSPNR 101, pod nim XYZ-1 – 102); węzeł nakładki 4D06WP000001 (AC-CAB.6.38.01) – WP-2 bez MPK.
        DictRow[] wpCam =
        [
            Row(("Element P1S", "AC-CAB.6.38.07"), ("WP", "WP-1"), ("CAM", "Anna Nowak"), ("Cost Category", null), ("MPK", "W30")),
            Wp("AC-CAB.6.38.01", "WP-2"),
        ];
        DictRow[] schedule =
        [
            Row(("WP", "WP-1"), ("BAC HOURS", "100"), ("BAC", "10000"), ("Baseline Start", "2026-10-05"), ("Baseline Koniec", "2026-10-16"),
                ("Forecast Koniec", "2026-11-30")),
            Row(("WP", "WP-2"), ("BAC HOURS", "1"), ("ETC HOURS", "8"), ("ETC MATERIAL", "50")),
        ];
        var production = new ProductionData(new Dictionary<string, ProductionValues>
        {
            ["101"] = new("101", AcHours: 40, BacHours: 60, EvHours: 30, ActualMaterial: 100, ActualStart: new DateOnly(2026, 10, 6), EtcHours: 20,
                MaterialValue: 400, MaterialFee: 40, Parts: 5, PartsDelivered: 2, NextDeliveryYear: 2027,
                PlanStart: new DateOnly(2026, 10, 1), PlanFinish: new DateOnly(2027, 2, 1), EtcByYear: new Dictionary<int, decimal> { [2026] = 15, [2027] = 5 }),
            ["102"] = new("102", AcHours: 10, BacHours: 40, EvHours: 20, ActualMaterial: null, EtcHours: 10,
                PlanStart: new DateOnly(2026, 9, 1), PlanFinish: new DateOnly(2026, 12, 1), EtcByYear: new Dictionary<int, decimal> { [2026] = 10 }),
        }, DateTimeOffset.Now);
        var costs = new Dictionary<string, decimal> { ["4D06WP000001"] = 900m };
        var rates = new EtcRates(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["W30"] = 150m }, 2026, "PLN", 1, 4m, null);
        var structure = StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, schedule, costs, production, new DateOnly(2026, 10, 11),
            labor: new Dictionary<string, decimal> { ["4D06WP000001"] = 600m }, material: new Dictionary<string, decimal> { ["4D06WP000001"] = 200m },
            rates: rates);

        var wp1 = Of(structure, "p1s:AC-CAB.6.38.07");
        Assert.Equal((100m, 50m, 50m, 50m), (wp1.OpsBacHours, wp1.EvHours, wp1.AcHours, wp1.PvHours));   // TECH 60+40, BCWP 30+20, CATS 40+10; BCWS 100 × 5/10
        Assert.Equal((1m, 1m, 30m, 80m), (wp1.SpiHours, wp1.CpiHours, wp1.EtcHours, wp1.EacHours));     // ETC z produkcji 20+10; EAC 50+30
        // Materiały vAPD w USD przeliczone na walutę projektu (PLN, kurs 4): wartość 400 → 1600, fee 40 → 160, dostarczone 100 → 400.
        Assert.Equal((1600m, 160m, 400m, 1200m, 25m, 5, 2, 2027),
            (wp1.MaterialValue, wp1.MaterialFee, wp1.ActualMaterial, wp1.MaterialEtc, wp1.PercentDelivered, wp1.Parts, wp1.PartsDelivered, wp1.NextDeliveryYear));
        // ETC Cost: 30 h × 150 PLN + 1200 PLN (300 USD × 4) = 5700; BCWS / BCWP z BAC Cost 10 000 × 0,5.
        Assert.Equal((5700m, 5000m, 5000m), (wp1.EtcCost, wp1.PvCost, wp1.EvCost));
        Assert.Equal(("2026-11-30", "w toku", "W30"), (wp1.ForecastFinish, wp1.Status, wp1.Mpk));
        Assert.False(wp1.EtcHoursManual);
        // ETC z produkcji po planowanym roku (R) i plan zleceń (DATA_PLAN) – suma poddrzewa WP (101 i 102).
        Assert.Equal([(2026, 25m), (2027, 5m)], wp1.ProductionEtcByYear!.OrderBy(x => x.Key).Select(x => (x.Key, x.Value)));
        Assert.Equal(("2026-09-01", "2027-02-01"), (wp1.PlanStart, wp1.PlanFinish));

        // WP-2: ETC wpisane (8 h, 50 PLN – waluta projektu), bez MPK – ETC Cost pusty i brak „WP bez MPK”; ACWP węzła: Labor 600, Material 200, Other 100.
        var wp2Row = Of(structure, "4D06WP000001");   // węzeł nakładki z kodem AC-CAB.6.38.01 – WP-2 w wierszu węzła
        Assert.Equal((8m, 50m, true, true), (wp2Row.EtcHours, wp2Row.MaterialEtc, wp2Row.EtcHoursManual, wp2Row.MaterialEtcManual));
        Assert.Null(wp2Row.EtcCost);
        Assert.Contains("WP bez MPK", wp2Row.Gap);
        var node = Of(structure, "4D06WP000001");
        Assert.Equal((900m, 600m, 200m, 100m), (node.Acwp, node.AcwpLabor, node.AcwpMaterial, node.AcwpOther));
        Assert.Null(node.Eac);   // WP-2 bez ETC Cost

        // Wskaźniki kosztowe z wartości wiersza.
        var row = wp1 with { Acwp = 4000m };
        Assert.Equal((0m, 1000m, 1m, 1.25m, 9700m, 300m, 40m), (row.Sv, row.Cv, row.Spi, row.Cpi, row.Eac, row.Vac, row.PercentSpent));
    }

    [Fact]
    public void Etc_rates_use_mpk_rate_with_overhead_of_current_year_and_usd_rate_for_project_currency()
    {
        DictRow[] departments =
        [
            Row(("MPK", "W30"), ("Year", "2026"), ("Labor Rate", "100"), ("Overhead", "1.5")),
            Row(("MPK", "W30"), ("Year", "2025"), ("Labor Rate", "90"), ("Overhead", "1.4")),
            Row(("MPK", "W50"), ("Year", "2026"), ("Labor Rate", "80"), ("Overhead", null)),
        ];
        DictRow[] fx = [Fx("USD", "2026-09-01", "2026-09-30", "3.9"), Fx("USD", "2026-10-01", "2026-10-31", "4"), Fx("USD", "2026-11-01", null, "5")];
        var today = new DateOnly(2026, 10, 15);

        var pln = ProjectService.EtcRatesOf(departments, fx, "PLN", null, today);
        Assert.Equal((150m, 80m, "PLN", 1m, 4m), (pln.RateByMpk["w30"], pln.RateByMpk["W50"], pln.Currency, pln.LaborFactor, pln.MaterialFactor));   // kurs z bieżącego okresu
        var usd = ProjectService.EtcRatesOf(departments, fx, "USD", null, today);
        Assert.Equal(("USD", 0.25m, 1m), (usd.Currency, usd.LaborFactor, usd.MaterialFactor));
        // Waluta spoza PLN / USD – kurs z „Kursy walut”: praca PLN → EUR, materiały USD → EUR.
        var eur = ProjectService.EtcRatesOf(departments, [.. fx, Fx("EUR", "2026-10-01", null, "4.25")], "EUR", null, today);
        Assert.Equal((Math.Round(1 / 4.25m, 10), Math.Round(4m / 4.25m, 10)), (Math.Round(eur.LaborFactor!.Value, 10), Math.Round(eur.MaterialFactor!.Value, 10)));
        Assert.Contains("brak kursu EUR", ProjectService.EtcRatesOf(departments, fx, "EUR", null, today).Problem);
        Assert.Contains("kilka walut", ProjectService.EtcRatesOf(departments, fx, null, "kilka walut w kolumnie Report currency: EUR, PLN", today).Problem);
        Assert.Contains("brak kursu USD", ProjectService.EtcRatesOf(departments, [], "PLN", null, today).Problem);
        // Przeliczniki kwot w innej walucie na walutę projektu: USD → PLN 4; PLN → USD 0,25; waluta projektu – 1; brak kursu – null.
        Assert.Equal((4m, 1m, (decimal?)null), (pln.Factor("USD"), pln.Factor("PLN"), pln.Factor("EUR")));
        Assert.Equal((0.25m, 1m), (usd.Factor("PLN"), usd.Factor(null)));
    }

    /// <summary>Kwoty „Harmonogram i budżet” wpisane w innej walucie (kolumna Waluta) – przeliczone na walutę projektu; brak kursu – puste i brak.</summary>
    [Fact]
    public void Budget_amounts_in_other_currency_are_converted_to_project_currency()
    {
        DictRow[] wpCam =
        [
            Row(("Element P1S", "AC-CAB.6.38.07"), ("WP", "WP-1"), ("CAM", "Anna Nowak"), ("Cost Category", null), ("MPK", "W30")),
            Row(("Element P1S", "AC-CAB.6.38.01"), ("WP", "WP-2"), ("CAM", "Anna Nowak"), ("Cost Category", null), ("MPK", "W30")),
        ];
        DictRow[] schedule =
        [
            Row(("WP", "WP-1"), ("BAC HOURS", "100"), ("BAC MATERIAL", "50"), ("BAC", "1000"), ("ETC HOURS", "0"), ("ETC MATERIAL", "10"), ("Waluta", "USD")),
            Row(("WP", "WP-2"), ("BAC HOURS", "1"), ("BAC", "700"), ("Waluta", "EUR")),
        ];
        var rates = new EtcRates(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["W30"] = 150m }, 2026, "PLN", 1, 4m, null,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 4m, ["PLN"] = 1m });
        var structure = StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, schedule, statusDate: new DateOnly(2026, 10, 11), rates: rates);

        var wp1 = Of(structure, "p1s:AC-CAB.6.38.07");
        Assert.Equal((100m, 200m, 4000m, 40m, 40m), (wp1.BacHours, wp1.BacMaterial, wp1.Bac, wp1.MaterialEtc, wp1.EtcCost));   // godziny bez przeliczenia
        var wp2 = Of(structure, "4D06WP000001");
        Assert.Equal(0m, wp2.Bac);
        Assert.Null(wp2.EtcCost);
        Assert.Contains("budżet w EUR – brak kursu EUR", wp2.Gap);
    }

    /// <summary>Waluta kwot „Harmonogram i budżet” przy zapisie: kwoty zmienione – waluta projektu, bez zmian – waluta wiersza.</summary>
    [Fact]
    public void Saving_budget_amounts_stamps_project_currency()
    {
        static DictRow Stored(params (string, string?)[] values) => Row(values) with { RowId = 1, Version = 1 };
        // Nowy wiersz z kwotą – waluta projektu; bez kwot – pusta; waluta nieznana – bez zmian.
        Assert.Equal("PLN", ProjectDictionaries.StampCurrency(Row(("WP", "WP-1"), ("BAC", "10")), null, "PLN")["Waluta"]);
        Assert.Null(ProjectDictionaries.StampCurrency(Row(("WP", "WP-1"), ("BAC HOURS", "10"), ("Waluta", "USD")), null, "PLN")["Waluta"]);
        Assert.Null(ProjectDictionaries.StampCurrency(Row(("WP", "WP-1"), ("BAC", "10")), null, null)["Waluta"]);
        // Zmiana daty – kwoty bez zmian: waluta wiersza zostaje.
        var stored = Stored(("WP", "WP-1"), ("BAC", "100"), ("BAC MATERIAL", "50"), ("Baseline Start", null), ("Waluta", "USD"));
        var dateOnly = ProjectDictionaries.StampCurrency(Row(("WP", "WP-1"), ("BAC", "100"), ("BAC MATERIAL", "50"), ("Baseline Start", "2026-01-05"), ("Waluta", "USD")),
            stored, "PLN", c => c == "USD" ? 4m : null);
        Assert.Equal(("USD", "100", "50"), (dateOnly["Waluta"], dateOnly["BAC"], dateOnly["BAC MATERIAL"]));
        // Zmiana BAC w walucie projektu – niezmienione kwoty wiersza przeliczone z USD (kurs 4), cały wiersz w PLN.
        var changed = ProjectDictionaries.StampCurrency(Row(("WP", "WP-1"), ("BAC", "500"), ("BAC MATERIAL", "50"), ("Waluta", "USD")),
            stored, "PLN", c => c == "USD" ? 4m : null);
        Assert.Equal(("PLN", "500", "200"), (changed["Waluta"], changed["BAC"], changed["BAC MATERIAL"]));
        // Brak kursu – wiersz zostaje w swojej walucie.
        Assert.Equal("USD", ProjectDictionaries.StampCurrency(Row(("WP", "WP-1"), ("BAC", "500"), ("BAC MATERIAL", "50"), ("Waluta", "USD")),
            stored, "PLN", _ => null)["Waluta"]);
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

    /// <summary>
    /// Węzeł wirtualny z kodem elementu LOG.WBS (nie wydzielenie) – zastępuje element w drzewie: ma własne godziny elementu,
    /// nie tylko jego poddrzewa (wcześniej godziny samego elementu znikały ze struktury).
    /// </summary>
    [Fact]
    public void Virtual_node_with_log_wbs_element_code_keeps_the_element_own_hours()
    {
        var tree = Objectives();
        var node = tree.AddVirtual(null, "Grupa 9.01");
        node.LegacyWbs = "AC-CAB.9.01";
        var production = new ProductionData(new Dictionary<string, ProductionValues>
        {
            ["200"] = new("200", 1, 10, 4, null),   // AC-CAB.9.01
        }, DateTimeOffset.Now);

        var structure = StructureBuilder.Build(tree, Inputs().P1s, [], [], production: production);

        var row = Of(structure, $"po:{node.Key}");
        Assert.Equal((10m, 4m, 1m), (row.OpsBacHours, row.EvHours, row.AcHours));
        Assert.Null(row.Gap);
        Assert.Equal(10m, structure.Rows.Where(r => r.ParentId is null).Sum(r => r.OpsBacHours ?? 0));   // element liczony raz
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
            (Cell("BAC Hours"), Cell("BAC Material"), Cell("BAC Cost"), Cell("Baseline Start"), Cell("Baseline Finish")));
        Assert.StartsWith(new string(' ', structure.Rows.Single(r => r.Id == "p1s:XYZ-1").Depth * 2), (string)Cell("WBS Name")!);
        // Wiersz bez WP w poddrzewie – budżet pusty (nie 0).
        var empty = rows[structure.Rows.ToList().FindIndex(r => r.Wps.Count == 0)];
        Assert.Null(empty[header.IndexOf("BAC Hours")]);
    }

    /// <summary>Koszt CAS po narzutach ma pełną precyzję obliczeń – w pobranej strukturze ACWP zaokrąglony do 2 miejsc (zgłoszenie 2026-10-16).</summary>
    [Fact]
    public void Structure_export_rounds_acwp_to_cents()
    {
        var costs = new Dictionary<string, decimal> { ["4D06WP000001"] = 85095.893454848608m };
        var structure = ProjectService.Structure(Objectives(), Inputs(), [], [], costs, labor: costs, dlh: new Dictionary<string, decimal> { ["4D06WP000001"] = 12.345m });
        var rows = ProjectService.StructureRows(structure, []).ToList();
        var row = rows[structure.Rows.ToList().FindIndex(r => r.WbsElement == "4D06WP000001")];
        var header = ProjectService.StructureHeaders.ToList();
        Assert.Equal((85095.89m, 85095.89m, 12.345m), (row[header.IndexOf("ACWP")], row[header.IndexOf("ACWP Hours")], row[header.IndexOf("Actual DLH")]));
        Assert.Null(row[header.IndexOf("ACWP Other")]);   // 0 – puste
        Assert.EndsWith("095,89)", structure.Rows.Single(r => r.WbsElement == "4D06WP000001").Gap);   // „koszt bez WP (…)” na ekranie
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
            Cell("AC-CAB.9.01", "BAC Hours").Value = 20;
            Cell("AC-CAB.9.01", "BAC Cost").Value = "1 500,50";
            Cell("AC-CAB.9.01", "Baseline Start").Value = new DateTime(2026, 2, 1);
            Cell("XYZ-1", "BAC Hours").Value = 12;             // zmiana budżetu istniejącego WP; reszta bez zmian
            Cell("XYZ-1", "BAC Material").Value = "abc";                // błędna liczba – komórka pominięta
            Cell(other.P1s!, "BAC Hours").Value = 5;           // bez WP – pominięte
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
        Assert.Contains(result.Issues, i => i.Level == CheckLevel.Warning && i.Message.StartsWith($"BAC Hours przy elemencie {other.P1s} bez WP"));
        Assert.Contains(result.Issues, i => i.Level == CheckLevel.Warning && i.Message.StartsWith("Elementu P1S NIE-MA nie ma w strukturze"));
        Assert.Equal((1, 1), (result.Count(StructureEdits.Wp), result.Count(StructureEdits.Cam)));
    }

    /// <summary>
    /// Pobrana struktura wczytana bez zmian: pusty budżet (w eksporcie 0) i budżet w walucie bez kursu (w eksporcie 0) – bez zapisu,
    /// budżet w innej walucie (w eksporcie przeliczony) – bez zapisu; zmieniona wartość – zapis.
    /// </summary>
    [Fact]
    public void Structure_file_exported_and_read_back_keeps_empty_and_foreign_currency_budgets()
    {
        DictRow[] wpCam =
        [
            Row(("Element P1S", "AC-CAB.6.38.07"), ("WP", "WP-1"), ("CAM", "Anna Nowak"), ("Cost Category", null), ("MPK", "W30")),
            Row(("Element P1S", "AC-CAB.6.38.01"), ("WP", "WP-2"), ("CAM", "Anna Nowak"), ("Cost Category", null), ("MPK", "W30")),
        ];
        DictRow[] schedule =
        [
            Row(("WP", "WP-1"), ("BAC HOURS", "100"), ("BAC", "1000"), ("Waluta", "USD")),   // BAC MATERIAL pusty
            Row(("WP", "WP-2"), ("BAC HOURS", "1"), ("BAC", "700"), ("Waluta", "EUR")),      // brak kursu EUR
        ];
        var rates = new EtcRates(new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["W30"] = 150m }, 2026, "PLN", 1, 4m, null,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 4m, ["PLN"] = 1m });
        var structure = StructureBuilder.Build(Objectives(), Inputs().P1s, wpCam, schedule, statusDate: new DateOnly(2026, 10, 11), rates: rates);
        var persons = new List<LookupOption> { new("Anna Nowak", "Anna Nowak") };
        var path = Path.Combine(Directory.CreateTempSubdirectory("pzl-ev-structure-").FullName, "M28_struktura.xlsx");
        ExcelTableWriter.WriteTemplate(path, [("Struktura", ProjectService.StructureSheetColumns(persons), ProjectService.StructureRows(structure, persons))]);

        Assert.Empty(StructureImport.Read(TabularFileReader.Read(path, "Struktura"), structure, schedule).Edits);

        using (var workbook = new XLWorkbook(path))
        {
            var sheet = workbook.Worksheet("Struktura");
            var header = ProjectService.StructureHeaders.ToList();
            var row = sheet.RowsUsed().Single(r => r.Cell(header.IndexOf("Legacy Element (P1S)") + 1).GetString() == "AC-CAB.6.38.07").RowNumber();
            sheet.Cell(row, header.IndexOf("BAC Material") + 1).Value = 25;
            workbook.Save();
        }
        var changes = StructureImport.Read(TabularFileReader.Read(path, "Struktura"), structure, schedule).Edits.Single().Changes;
        Assert.Equal(["BacMaterial"], changes.Keys);
        Assert.Equal("25", changes[StructureEdits.BacMaterial]);
    }

    /// <summary>Actual Start / Finish z pliku: data z produkcji (pokazana w eksporcie) – bez nadpisania; wpisana inna – nadpisanie WP.</summary>
    [Fact]
    public void Structure_file_actual_dates_override_only_when_different_from_production()
    {
        DictRow[] wpCam = [Row(("Element P1S", "XYZ-1"), ("WP", "WP-1"), ("CAM", "e123456"), ("Cost Category", null))];
        DictRow[] schedule = [Budget("WP-1", "10", "100", null, null, "1000")];
        var production = new ProductionData(new Dictionary<string, ProductionValues>
        {
            ["102"] = new("102", 5, 40, 30, null, null, new DateOnly(2026, 1, 15), null),   // XYZ-1 – operacja niezamknięta
        }, DateTimeOffset.Now);
        var structure = ProjectService.Structure(Objectives(), Inputs(), wpCam, schedule, production: production);
        var persons = new List<LookupOption> { new("e123456", "Anna Nowak") };
        var path = Path.Combine(Directory.CreateTempSubdirectory("pzl-ev-structure-").FullName, "M28_struktura.xlsx");
        ExcelTableWriter.WriteTemplate(path, [("Struktura", ProjectService.StructureSheetColumns(persons), ProjectService.StructureRows(structure, persons))]);

        Assert.Empty(StructureImport.Read(TabularFileReader.Read(path, "Struktura"), structure, schedule).Edits);   // start z produkcji – bez zmian

        using (var workbook = new XLWorkbook(path))
        {
            var sheet = workbook.Worksheet("Struktura");
            var header = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            var row = sheet.RowsUsed().Single(r => r.Cell(header.IndexOf("Legacy Element (P1S)") + 1).GetString() == "XYZ-1").RowNumber();
            sheet.Cell(row, header.IndexOf("Actual Finish") + 1).Value = new DateTime(2026, 5, 10);
            workbook.Save();
        }
        var changes = StructureImport.Read(TabularFileReader.Read(path, "Struktura"), structure, schedule).Edits.Single().Changes;
        Assert.Equal(["ActualFinish"], changes.Keys);
        Assert.Equal("2026-05-10", changes[StructureEdits.ActualFinish]);
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
