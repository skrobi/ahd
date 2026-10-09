using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Gotowość projektu (F02, F4.5), baza analityczna (F01, krok 5) i foldery projektu.</summary>
public sealed class ReadinessAndAnalyticTests
{
    private static readonly Issue FoldersOk = new(CheckLevel.Pass, "ok");
    private static readonly Issue CamAccess = Issue.Warning("IT");
    private static readonly Issue P1sSide = new(CheckLevel.Pass, "P1S");

    private static PoTree Objectives() => PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx")).Tree;

    private static Dictionary<string, int> Rows(int wp, int schedule) =>
        new() { [ProjectDictionaries.WpCam] = wp, [ProjectDictionaries.ScheduleBudget] = schedule };

    [Fact]
    public void Project_without_wp_is_not_ready()
    {
        var checks = ProjectReadiness.Check(ProjectTypes.Internal, Objectives(), Rows(0, 0), [], P1sSide, FoldersOk, CamAccess);
        Assert.False(ProjectReadiness.IsReady(checks));
        Assert.Contains(checks, c => c.Level == CheckLevel.Error && c.Element == "WP i CAM");
        Assert.Contains(checks, c => c.Level == CheckLevel.Error && c.Element == "Harmonogram i budżet");
    }

    [Fact]
    public void Project_with_dictionaries_is_ready_with_warnings_only()
    {
        var checks = ProjectReadiness.Check(ProjectTypes.Sac, Objectives(), Rows(4, 3), ["Jan Obcy"], P1sSide, FoldersOk, CamAccess);
        Assert.True(ProjectReadiness.IsReady(checks));
        Assert.Contains(checks, c => c.Level == CheckLevel.Warning && c.Message.Contains("Jan Obcy"));
    }

    [Fact]
    public void Actuals_freshness_shows_rabit_data_date_import_and_last_check()
    {
        Assert.Equal("Dane RABIT: brak zaimportowanych plików ACTUALS", new ActualsFreshness([]).Summary);
        Assert.Equal("Dane RABIT: nie udało się odczytać", new ActualsFreshness([], "timeout").Summary);
        DateTimeOffset At(int day, int hour) => new(2026, 10, day, hour, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, day)));
        var files = new[]
        {
            new ActualsFile("ACTUALS_PAF_01.csv", At(6, 23), At(7, 8), At(7, 23), At(8, 7)),   // ponowny raport bez zmian – dane na 7.10 23:00
            new ActualsFile("ACTUALS_CES_01.csv", At(7, 23), At(8, 7), null, null),
        };
        Assert.Equal("Dane RABIT z 2026-10-07 23:00 · import 2026-10-08 07:00 · sprawdzone 2026-10-08 07:00", new ActualsFreshness(files).Summary);
        var older = new[] { files[1], new ActualsFile("ACTUALS_PAF_02.csv", At(5, 23), At(6, 8), null, null) };
        Assert.StartsWith("Dane RABIT z 2026-10-05 23:00 – 2026-10-07 23:00", new ActualsFreshness(older).Summary);   // pliki z różnych dni
        Assert.Contains("ACTUALS_PAF_01.csv: raport RABIT 2026-10-06 23:00, import 2026-10-07 08:00; sprawdzony 2026-10-08 07:00", new ActualsFreshness(files).Details);
    }

    [Fact]
    public void Wp_without_cam_blocks_the_run()
    {
        var checks = ProjectReadiness.Check(ProjectTypes.Internal, Objectives(), Rows(4, 3), [], P1sSide, FoldersOk, CamAccess, ["WP-1", "WP-2"]);
        Assert.False(ProjectReadiness.IsReady(checks));
        Assert.Contains(checks, c => c.Level == CheckLevel.Error && c.Message.StartsWith("WP bez CAM (2)") && c.Message.Contains("WP-1, WP-2"));
    }

    [Fact]
    public void Empty_objectives_and_cas_rates_block()
    {
        var empty = ProjectReadiness.Check(ProjectTypes.Internal, new PoTree(), Rows(4, 3), [], P1sSide, FoldersOk, CamAccess);
        Assert.Contains(empty, c => c.Level == CheckLevel.Error && c.Element == "Performance Objectives");

        var cas = ProjectReadiness.Check(ProjectTypes.Cas, Objectives(), Rows(4, 3), [], P1sSide, FoldersOk, CamAccess);
        Assert.Contains(cas, c => c.Level == CheckLevel.Error && c.Message.Contains("O37"));
    }

    private static DictRow Row(params (string Column, string? Value)[] values) => new(null, null, values.ToDictionary(v => v.Column, v => v.Value));

    [Fact]
    public void Analytic_base_joins_tree_with_wp_cam_budget()
    {
        DictRow[] wp =
        [
            Row(("Element P1S", "AC-CAB.6.38.01"), ("WP", "WP-101"), ("CAM", "Anna Nowak")),
            Row(("Element P1S", "AC-CAB.6.38.03"), ("WP", "WP-103"), ("CAM", "Piotr W")),
            Row(("Element P1S", "AC-CAB.6.38.03.01"), ("WP", "WP-103"), ("CAM", "Piotr W")),
        ];
        DictRow[] plan =
        [
            Row(("WP", "WP-101"), ("BAC HOURS", "600"), ("BAC MATERIAL", "20000"), ("Baseline Start", "2026-10-01"), ("Baseline Koniec", "2027-03-31")),
            Row(("WP", "WP-103"), ("BAC HOURS", null), ("BAC MATERIAL", null), ("Baseline Start", null), ("Baseline Koniec", null)),
        ];

        var a = AnalyticBaseBuilder.Build(Objectives(), wp, plan);

        AnalyticRow R(string wbs) => a.Rows.Single(r => r.WbsElement == wbs);
        Assert.Equal(["WP-101", "WP-103"], R("4D06WP").Wps);                 // korzeń – suma poddrzewa
        Assert.Equal(600m, R("4D06WP").BacHours);
        Assert.Equal(["WP-101"], R("4D06WP000001").Wps);
        Assert.Equal(["WP-103"], R("4D06WP000003").Wps);                     // najgłębszy węzeł z Legacy WBS AC-CAB.6.38.03
        Assert.Empty(R("4D06WP.02").Wps.Except(["WP-103"]));
        Assert.Equal("element nakładki bez WP", R("4D06WP000002").Gap);
        Assert.Equal("WP bez budżetu", R("4D06WP000004").Gap);
        Assert.Equal(["4D06WP000002"], a.ElementsWithoutWp);
        Assert.Equal(["WP-103"], a.WpsWithoutBudget);
        Assert.Equal(2, a.WpCount);
        Assert.Equal(600m, a.BacHours);
        Assert.Equal(20000m, a.BacMaterial);
        var anna = a.ByCam.Single(c => c.Cam == "Anna Nowak");
        Assert.Equal((1, 600m, "2026-10-01", "2027-03-31"), (anna.Wps, anna.BacHours, anna.Start, anna.Finish));
    }

    [Fact]
    public void Folders_are_checked_and_created()
    {
        var root = Path.Combine(Path.GetTempPath(), "pzlev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var folders = new ProjectFolders(Path.Combine(root, "Projekty"));
            Assert.DoesNotContain(folders.CheckBeforeCreate("M28"), i => i.Level == CheckLevel.Error);
            Assert.Equal(CheckLevel.Warning, folders.CheckStructure("M28").Level);

            Assert.Equal(4, folders.Create("M28").Count);
            Assert.Equal(CheckLevel.Pass, folders.CheckStructure("M28").Level);
            Assert.Contains(folders.CheckBeforeCreate("M28"), i => i.Level == CheckLevel.Error && i.Message.Contains("już istnieje"));

            Directory.Delete(Path.Combine(root, "Projekty", "M28", "EV"));
            Assert.Contains("EV", folders.CheckStructure("M28").Message);

            var missingRoot = new ProjectFolders(Path.Combine(root, "brak", "Projekty"));
            Assert.Contains(missingRoot.CheckBeforeCreate("M28"), i => i.Level == CheckLevel.Error && i.Message.Contains("niedostępny"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
