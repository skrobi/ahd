using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Słownik projektu „Parametry projektu” (zakładka Konfiguracja): pole kwoty ACTUALS – ACWP, raport kosztów, przebieg.</summary>
public sealed class ProjectSettingsTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-settings-").FullName;
    private TestDatabase? _database;

    private static DictRow Setting(string name, string? value) =>
        new(null, null, new Dictionary<string, string?> { ["Parametr"] = name, ["Wartość"] = value, ["Opis"] = null });

    [Fact]
    public void Actuals_amount_field_is_validated_and_defaults_to_object_currency()
    {
        var spec = ProjectDictionaries.For(ProjectDictionaries.Settings, new ProjectDictionaryContext(new P1sScope([], []), new Dictionary<string, string>(), new HashSet<string>(), null, null, null));

        Assert.Empty(DictionaryValidator.Validate(spec, [Setting(ProjectSettings.ActualsAmount, "ValueRepCur")]));
        var issue = Assert.Single(DictionaryValidator.Validate(spec, [Setting(ProjectSettings.ActualsAmount, "Kwota")]));
        Assert.Equal(CheckLevel.Error, issue.Level);
        Assert.Contains("ValueObjCrcy, ValueRepCur", issue.Message);

        Assert.Equal("ValueObjCrcy", ProjectDictionaries.ActualsAmount([]));
        Assert.Equal("ValueRepCur", ProjectDictionaries.ActualsAmount([Setting(ProjectSettings.ActualsAmount, "valuerepcur")]));
        Assert.Equal("ValueObjCrcy", ProjectSettings.ActualsAmountOf("Kwota"));
        Assert.Equal(["ValueObjCrcy", "ValueRepCur"], ProjectService.Lookups([], [])[ProjectDictionaries.Settings].Select(o => o.Value));

        // Zakładka Konfiguracja: Źródła danych i Parametry projektu; pozostałe słowniki – zakładka „Słowniki projektu”.
        Assert.Equal([ProjectDictionaries.Sources, ProjectDictionaries.Settings],
            ProjectDictionaries.Items.Where(i => ProjectDictionaries.Configuration.Contains(i.Code)).Select(i => i.Code));
        Assert.Contains(ProjectDictionaries.Items, i => i.Code == ProjectDictionaries.Settings && i.Stored && i.RequiredFor.Count == 0);
    }

    [SqlFact]
    public void Amount_field_of_project_drives_costs_and_run_and_change_skips_comparison()
    {
        _database = new TestDatabase(presets: true);
        var app = _services.App(_root, _database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        Directory.CreateDirectory(app.Config.ImportFolder);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Path.Combine(app.Config.ImportFolder, "ACTUALS_PAF_01.csv"));
        Assert.Equal(FileDecisions.Imported, Assert.Single(new ImportService(new SqlImportStore(_database.Sql), app).Run().Files).Decision);
        var store = new SqlProjectStore(_database.Sql, _services.Clock, _services.User);
        var service = new ProjectService(store, new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, [.. GlobalDictionaries.Tables, .. ProjectDictionaries.Tables]),
            app.Journal, new ProjectFolders(Path.Combine(_root, "Projekty")), null);
        var tree = new PoTree();
        tree.AddElement(null, "2DI473", "PAF2", null);
        Assert.True(store.Create("PAF", "PAF", ProjectTypes.Internal, tree).Success);
        var runs = new SqlRunStore(_database.Sql);
        var run = new RunService(runs, app);

        // Bez parametru – waluta obiektu (PLN).
        Assert.Equal("ValueObjCrcy", service.ActualsAmount("PAF"));
        Assert.Equal(store.CostsByElement("PAF", "ValueObjCrcy"), service.CostsByElement("PAF"));
        run.Execute("PAF");
        var first = Assert.Single(runs.Pins("PAF", 10));
        Assert.Equal("ValueObjCrcy", first.AmountField);
        Assert.All(runs.Balance(first.PinId), r => Assert.Equal("PLN", r.Currency));

        // Parametr projektu: waluta raportu (USD) – ACWP, przebieg; zmiana pola – bez porównania z poprzednim przebiegiem.
        var context = service.Context("PAF", tree, null, P1sInputs.None);
        Assert.Equal(SaveStatus.Saved, service.SaveDictionary(ProjectDictionaries.Settings, context, [Setting(ProjectSettings.ActualsAmount, "ValueRepCur")], [], "PAF").Status);
        Assert.Equal("ValueRepCur", service.ActualsAmount("PAF"));
        Assert.Equal(store.CostsByElement("PAF", "ValueRepCur"), service.CostsByElement("PAF"));
        Assert.NotEqual(service.CostsByElement("PAF", "ValueObjCrcy"), service.CostsByElement("PAF"));

        _services.Clock.Advance(TimeSpan.FromDays(7));
        run.Execute("PAF");
        var second = runs.Pins("PAF", 10)[0];
        Assert.Equal("ValueRepCur", second.AmountField);
        var balance = runs.Balance(second.PinId);
        Assert.All(balance, r => Assert.Equal("USD", r.Currency));
        Assert.Equal(261.54m + 1000m + 500m + 89.58m, balance.Sum(r => r.Amount));   // Val.in rep.cur. wierszy 2DI473 pliku testowego
        var issues = runs.Issues(second.PinId);
        Assert.Contains(issues, i => i.CheckName == RunService.Pinned && i.Level == CheckLevel.Warning && i.Message.StartsWith("Pole kwoty ACTUALS zmienione (ValueObjCrcy → ValueRepCur"));
        Assert.DoesNotContain(issues, i => i.CheckName == "Największe odchylenia raport do raportu" && i.Details is not null);   // bez porównania
        Assert.Equal(CheckLevel.Pass, Assert.Single(issues, i => i.CheckName == "Waluta inna niż waluta kosztów").Level);          // waluta kosztów – przeważająca

        // Waluta transakcji nie jest zestawem walutowym – procedury jej nie przyjmują (migracja 042).
        Assert.Contains("nie ma pola kwoty ValueTranCurr", Assert.ThrowsAny<Exception>(() => store.CostsByElement("PAF", "ValueTranCurr")).Message);
        Assert.Contains("nie ma pola kwoty ValueTranCurr", Assert.ThrowsAny<Exception>(() => store.CostReport("PAF", "ValueTranCurr")).Message);
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
