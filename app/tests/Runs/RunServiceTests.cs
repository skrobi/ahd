using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Przebieg projektu na bazie: tydzień z kalendarza, przypięcie danych ACTUALS, kontrole, porównanie z poprzednim przebiegiem.</summary>
public sealed class RunServiceTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-runs-").FullName;
    private TestDatabase? _database;

    [SqlFact]
    public void Run_pins_project_data_checks_it_and_compares_with_previous_week()
    {
        _database = new TestDatabase(presets: true);
        var app = _services.App(_root, _database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        var import = new ImportService(new SqlImportStore(_database.Sql), app);
        Directory.CreateDirectory(app.Config.ImportFolder);
        var file = Path.Combine(app.Config.ImportFolder, "ACTUALS_PAF_01.csv");
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), file);
        Assert.Equal(FileDecisions.Imported, Assert.Single(import.Run().Files).Decision);
        var tree = new PoTree();
        tree.AddElement(null, "2DI473", "PAF2", null);   // Project definition danych testowych
        Assert.True(new SqlProjectStore(_database.Sql, _services.Clock, _services.User).Create("PAF", "PAF", ProjectTypes.Internal, tree).Success);
        var store = new SqlRunStore(_database.Sql);
        var service = new RunService(store, app);

        var first = service.Execute("PAF");

        Assert.Equal("R-PAF-2026-10-T40", first.Code);   // 2026-10-02 – tydzień 40, okres 2026-10
        var pin = Assert.Single(store.Pins("PAF", 10));
        Assert.Equal((4L, first.Status), (pin.Rows, pin.Status));   // 4 z 6 wierszy pliku – Project definition 2DI473
        Assert.Equal(4, store.Balance(pin.PinId).Sum(r => r.Rows));
        var issues = store.Issues(pin.PinId);
        Assert.Contains(issues, i => i.CheckName == "Import" && i.Level == CheckLevel.Warning);              // projekt bez źródeł danych
        Assert.Contains(issues, i => i.CheckName == RunChecks.NetZero && i.Level == CheckLevel.Error && i.Element == "2DI473001001");   // dane bez rozliczenia
        Assert.DoesNotContain(issues, i => i.CheckName == RunChecks.NewCostElements && i.Level == CheckLevel.Error);   // cost elementy projektu są w słowniku (numery 10-cyfrowe)
        Assert.Equal(pin.PinId, store.Latest()["PAF"].PinId);

        // Tydzień później: nowa wersja pliku z dodatkowym wierszem – porównanie z przebiegiem tygodnia 40.
        _services.Clock.Advance(TimeSpan.FromDays(7));
        File.AppendAllText(file, "2DI473;2DI473001003;51105550;x;x;PLN;10,00;PLN;10,00;USD;2,50;0,000;;;;;;;;2026;2026-03-31;3;\r\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(FileDecisions.Imported, Assert.Single(import.Run().Files).Decision);

        var second = service.Execute("PAF");

        Assert.Equal("R-PAF-2026-10-T41", second.Code);
        var next = store.Pins("PAF", 10)[0];
        Assert.Equal(5L, next.Rows);
        var compared = store.Issues(next.PinId);
        Assert.Contains(compared, i => i.Message.StartsWith("Porównanie z przebiegiem R-PAF-2026-10-T40 #1"));
        Assert.DoesNotContain(compared, i => i.CheckName == RunChecks.Volume && i.Level != CheckLevel.Pass);
        Assert.Contains(compared, i => i.CheckName == RunChecks.Deviations && i.Element == "2DI473001003" && i.Amount == 10m);
        Assert.Contains(compared, i => i.CheckName == RunChecks.ClosedPeriods && i.Element == "2DI473001003");   // 2026/03 zmieniony po zamknięciu

        // Przebieg projektu u innej osoby – drugi nie startuje.
        var other = _services.App(_root, _database.Sql);
        using (other.Locks.TryAcquire(RunService.ProjectLock("PAF"), out _))
            Assert.StartsWith("Przebieg projektu PAF trwa", service.Execute("PAF").Summary);
    }

    [Fact]
    public void Previous_period_stays_open_during_settlement_days()
    {
        Assert.Equal(((2026, 9), (2026, 10)), RunService.Periods("2026-10", new DateTime(2026, 10, 2)));
        Assert.Equal(((2026, 10), (2026, 10)), RunService.Periods("2026-10", new DateTime(2026, 10, 9)));
        Assert.Equal(((2025, 12), (2026, 1)), RunService.Periods("2026-01", new DateTime(2026, 1, 5)));
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
