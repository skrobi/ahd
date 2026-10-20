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
    private const string NetZero = "Netowanie do 0 w okresach zamkniętych";

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
        // Definicja wbudowana: import, przypięcie – zakończone; walidacja ACTUALS – błąd netowania zatrzymuje przebieg, dalsze etapy czekają.
        Assert.Equal([RunStepStatuses.Done, RunStepStatuses.Done, RunStepStatuses.Failed, RunStepStatuses.Waiting, RunStepStatuses.Waiting],
            store.Steps(pin.PinId).Select(s => s.Status));
        Assert.Equal(RunStatuses.Blocked, pin.Status);
        Assert.Equal("Potwierdzenie wymaga komentarza.", service.Confirm(pin.PinId, 3, " ").Summary);
        Assert.Equal(RunStepStatuses.Failed, store.Steps(pin.PinId)[2].Status);   // ponowne wykonanie bez poprawy danych – nadal błąd
        Assert.Equal(RunStatuses.Blocked, service.Rerun(pin.PinId, 3).Status);
        var issues = store.Issues(pin.PinId);
        Assert.Contains(issues, i => i.CheckName == "Import" && i.Level == CheckLevel.Warning);              // projekt bez źródeł danych
        Assert.Contains(issues, i => i.CheckName == NetZero && i.Level == CheckLevel.Error && i.Element == "2DI473001001");   // dane bez rozliczenia
        Assert.Equal(CheckLevel.Pass, Assert.Single(issues, i => i.CheckName == "Cost element spoza słownika Cost Category").Level);   // cost elementy projektu są w słowniku
        Assert.Equal(RunRules.ActualsStarters.Count, issues.Where(i => i.StepNo == 3).Select(i => i.CheckName).Distinct().Count());   // każda reguła etapu – wynik
        Assert.True(store.HasEv(pin.PinId));
        Assert.Equal(pin.PinId, store.Latest()["PAF"].PinId);

        // Test reguły na programie (bez zapisu wyników): wiersze zapytania, opis wyniku; błąd SQL z linią zapytania reguły.
        var templates = new RunTemplateService(new PzlEv.Shared.Utils.Dictionaries.DictionaryService(new PzlEv.Shared.Utils.Dictionaries.SqlDictionaryStore(_database.Sql,
            _services.Clock, _services.User, PzlEv.Shared.Utils.Dictionaries.GlobalDictionaries.Tables), app.Journal), store);
        var test = templates.TestRule(RunRules.Starter(NetZero)!, "PAF");
        Assert.Null(test.Result.Error);
        Assert.Contains(test.Descriptions, d => d.StartsWith("2DI473001001: saldo"));
        Assert.StartsWith("Uruchomienie R-PAF-2026-10-T40 #1", test.Note);
        Assert.StartsWith("linia 3: ", templates.TestRule(new RunRule("x", CheckLevel.Error, true, "SELECT ElementWBS\nFROM biezace\nWHERE Brak = 1"), "PAF").Result.Error);
        Assert.Equal(["Brak"], templates.TestRule(new RunRule("x", CheckLevel.Error, true, "SELECT ElementWBS FROM biezace", "{Brak} {elementwbs}"), "PAF").UnknownColumns);

        // Linie ACTUALS wiersza wyniku: zakres z kolumn wiersza (netowanie – element WBS), stan plików z przypięcia.
        var net = issues.First(i => i.CheckName == NetZero && i.Element == "2DI473001001");
        var (scope, description) = RunOverview.ActualsScope(net, _ => [])!.Value;
        Assert.Equal("ElementWBS = 2DI473001001", description);
        var asOf = store.Steps(pin.PinId)[1].FinishedAt!.Value;
        var lines = store.ActualsLines(scope, asOf, "ValueObjCrcy", 500);
        var balance = store.Balance(pin.PinId).Where(r => r.WbsElement == "2DI473001001").ToList();
        Assert.Equal((balance.Sum(r => r.Rows), balance.Sum(r => r.Amount)), (lines.Count, lines.Sum));
        Assert.All(lines.Rows, l => Assert.Equal("2DI473001001", l.WbsElement));
        var program = store.ActualsLines(new RunActualsScope(pin.PinId, null, null, null, null), asOf, "ValueObjCrcy", 1);
        Assert.Equal((4, 1), (program.Count, program.Rows.Count));   // elementy WBS programu (4 z 6 wierszy pliku), pierwsza linia wg |kwoty|
        var byElement = store.ActualsLines(new RunActualsScope(pin.PinId, null, [lines.Rows[0].CostElement!], lines.Rows[0].FiscalYear, lines.Rows[0].Period),
            asOf, "ValueObjCrcy", 500);
        Assert.Contains(byElement.Rows, l => l.RowNumber == lines.Rows[0].RowNumber);

        // Akceptacja błędu przez analityka (komentarz): etap zakończony z błędami w wynikach, komentarz w etapie, dalsze etapy wykonane.
        service.Confirm(pin.PinId, 3, "znany błąd netowania");
        var accepted = store.Steps(pin.PinId);
        Assert.Equal(RunStepStatuses.Done, accepted[2].Status);
        Assert.True(accepted[2].Errors > 0);
        Assert.EndsWith("błędy zaakceptowane (" + accepted[2].Errors + "): znany błąd netowania", accepted[2].Comment);
        Assert.NotEqual(RunStepStatuses.Waiting, accepted[3].Status);

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
        Assert.Equal(CheckLevel.Pass, Assert.Single(compared, i => i.CheckName == "Mniej wierszy niż w poprzednim przebiegu").Level);
        Assert.Contains(compared, i => i.CheckName == "Największe odchylenia raport do raportu" && i.Element == "2DI473001003"
            && RunRules.ReadDetails(i.Details).Single(d => d.Column == "Zmiana").Number == 10m);
        Assert.Contains(compared, i => i.CheckName == "Zmiana salda okresu zamkniętego" && i.Element == "2DI473001003");   // 2026/03 zmieniony po zamknięciu

        // Przebieg projektu u innej osoby – drugi nie startuje.
        var other = _services.App(_root, _database.Sql);
        using (other.Locks.TryAcquire(RunService.ProjectLock("PAF"), out _))
            Assert.StartsWith("Przebieg programu PAF trwa", service.Execute("PAF").Summary);

        // Zatwierdzenie ACTUALS: struktura na danych zatwierdzonego przebiegu – stan z przypięcia; import po zatwierdzeniu nie zmienia
        // kosztów struktury do kolejnego zatwierdzenia. Szablon bez klocka „Zatwierdzenie ACTUALS” – zakończona walidacja ACTUALS
        // (T40 #1 – błędy zaakceptowane; T41 #1 – zatrzymany na błędzie, niezatwierdzony).
        var projects = new SqlProjectStore(_database.Sql, _services.Clock, _services.User);
        Assert.Equal((pin.Code, pin.PinNo), (projects.ApprovedActuals("PAF")!.RunCode, projects.ApprovedActuals("PAF")!.PinNo));
        var approval = new RunDefinition("A – zatwierdzenie ACTUALS", RunRules.AllTypes, [.. RunDefinitions.StandardSteps.Take(3), RunDefinitions.ActualsApproval(4)]);
        Assert.Equal(PzlEv.Shared.Models.Dictionaries.SaveStatus.Saved, templates.Save(approval, isActive: true).Status);   // domyślny – pierwszy alfabetycznie
        service.Execute("PAF");
        var approving = store.Pins("PAF", 1)[0];
        service.Confirm(approving.PinId, 3, "znany błąd netowania");
        Assert.Equal(RunStepStatuses.NeedsAction, store.Steps(approving.PinId)[3].Status);   // czeka na zatwierdzenie danych
        Assert.Equal(pin.Code, projects.ApprovedActuals("PAF")!.RunCode);                     // z klockiem – liczy się tylko zatwierdzenie
        Assert.Equal(RunStatuses.Errors, service.Confirm(approving.PinId, 4, "dane ACTUALS sprawdzone").Status);
        var approved = projects.ApprovedActuals("PAF")!;
        Assert.Equal((approving.Code, approving.PinNo, store.Steps(approving.PinId)[1].FinishedAt), (approved.RunCode, approved.PinNo, (DateTimeOffset?)approved.AsOf));
        var atApproval = projects.CostsAndHoursByElement("PAF", "ValueObjCrcy", "TotalQuantity", approved.AsOf).Amounts;

        _services.Clock.Advance(TimeSpan.FromHours(1));
        File.AppendAllText(file, "2DI473;2DI473001003;51105550;x;x;PLN;10,00;PLN;10,00;USD;2,50;0,000;;;;;;;;2026;2026-03-31;3;\r\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(2));
        Assert.Equal(FileDecisions.Imported, Assert.Single(import.Run().Files).Decision);

        Assert.Equal(atApproval, projects.CostsAndHoursByElement("PAF", "ValueObjCrcy", "TotalQuantity", approved.AsOf).Amounts);   // stan zatwierdzony
        Assert.Equal(atApproval["2DI473001003"] + 10m, projects.CostsAndHoursByElement("PAF", "ValueObjCrcy", "TotalQuantity").Amounts["2DI473001003"]);   // ostatni import
        Assert.Equal(approved, projects.ApprovedActuals("PAF"));
    }

    /// <summary>
    /// Projekt CAS: przebieg liczy koszty po narzutach CAS (koszt brutto = ACWP struktury), kontrola netowania do 0 – na danych
    /// źródłowych (saldo okresów zamkniętych bez narzutów).
    /// </summary>
    [SqlFact]
    public void Cas_project_run_checks_costs_after_cas_burdens()
    {
        _database = new TestDatabase(presets: true);
        var app = _services.App(_root, _database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        Directory.CreateDirectory(app.Config.ImportFolder);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Path.Combine(app.Config.ImportFolder, "ACTUALS_PAF_01.csv"));
        Assert.Equal(FileDecisions.Imported, Assert.Single(new ImportService(new SqlImportStore(_database.Sql), app).Run().Files).Decision);
        var tree = new PoTree();
        tree.AddElement(null, "2DI473", "PAF2", null);
        var projects = new SqlProjectStore(_database.Sql, _services.Clock, _services.User);
        Assert.True(projects.Create("PAF", "PAF", ProjectTypes.Cas, tree).Success);
        var dictionaries = new PzlEv.Shared.Utils.Dictionaries.SqlDictionaryStore(_database.Sql, _services.Clock, _services.User,
            [.. PzlEv.Shared.Utils.Dictionaries.GlobalDictionaries.Tables, .. PzlEv.Modules.Projects.Services.ProjectDictionaries.Tables]);
        var project = new PzlEv.Modules.Projects.Services.ProjectService(projects, dictionaries, app.Journal,
            new PzlEv.Modules.Projects.Services.ProjectFolders(Path.Combine(_root, "Projekty")), null);
        var store = new SqlRunStore(_database.Sql);
        var service = new RunService(store, app, processes: () => new PzlEv.Modules.Projects.Services.ProjectProcessSource(project));

        var outcome = service.Execute("PAF");

        var pin = Assert.Single(store.Pins("PAF", 10));
        var acwp = project.CostsAndHoursByElement("PAF", "ValueObjCrcy").Amounts.Values.Sum();
        Assert.True(acwp > 8884.51m);                                                    // 1 254,51 + 4 800 + 2 400 + 430 z narzutami
        Assert.Equal(Math.Round(acwp, 2), Math.Round(pin.GrossCost!.Value, 2));
        Assert.Equal(8884.51m, pin.Balance);                                             // saldo okresów zamkniętych – dane źródłowe
        var issues = store.Issues(pin.PinId);
        Assert.Contains(issues, i => i.CheckName == "Koszty po narzutach CAS" && i.Level == CheckLevel.Pass && i.Message.Contains("w PLN"));
        Assert.Contains(issues, i => i.CheckName == NetZero && i.Level == CheckLevel.Error
            && i.Message.Replace('\u00a0', ' ').Contains("saldo programu 8 884,51"));   // Walidacja ACTUALS – dane źródłowe (przed narzutami)
        var ev = store.RunRule("SELECT SUM(Kwota) AS Koszt FROM ev", new RunRuleContext(pin.PinId, null, "PAF", (2026, 9), (2026, 10), "PLN", "CAS"));
        Assert.Equal(Math.Round(acwp, 2), Math.Round((decimal)ev.Rows.Single()[0]!, 2));   // tabela ev – koszty po narzutach CAS
        Assert.NotEqual(RunStatuses.Failed, outcome.Status);
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
