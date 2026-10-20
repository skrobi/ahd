using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Słownik projektu „Źródła danych” i import źródeł projektu (kontrakt ISourceImport modułu Import).</summary>
public sealed class ProjectSourcesTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-sources-").FullName;
    private TestDatabase? _database;

    /// <summary>Kontrakt jak w module Import (SourceImport), bez okna logowania.</summary>
    private sealed class TestImport(ImportService service) : ISourceImport
    {
        public IReadOnlyList<SourceDefinitionRow> Definitions() => service.Definitions();

        public Task<bool> SignIn() => Task.FromResult(true);

        public SourceImportResult Import(IReadOnlySet<string> sources, IProgress<string>? progress, CancellationToken cancellation)
        {
            var run = service.Run(null, cancellation, sources);
            return new SourceImportResult(run.BatchId, run.Count(FileDecisions.Imported), run.Count(FileDecisions.Error), run.NotStarted ?? run.Summary, run.Issues);
        }
    }

    private static DictRow Source(string code) => new(null, null, new Dictionary<string, string?> { ["Definicja źródła"] = code, ["Opis"] = null });

    [Fact]
    public void Sources_dictionary_warns_about_unknown_definition_and_offers_active_definitions()
    {
        var context = new ProjectDictionaryContext(new P1sScope([], []), new Dictionary<string, string>(), new HashSet<string>(), null, null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ACTUALS_PAF" });
        var spec = ProjectDictionaries.For(ProjectDictionaries.Sources, context);

        Assert.Empty(DictionaryValidator.Validate(spec, [Source("ACTUALS_PAF")]));
        var issue = Assert.Single(DictionaryValidator.Validate(spec, [Source("ACTUALS_X")]));
        Assert.Equal(CheckLevel.Warning, issue.Level);
        Assert.Contains("ACTUALS_X nie istnieje albo jest nieaktywna", issue.Message);
        Assert.Contains(ProjectDictionaries.Items, i => i.Code == ProjectDictionaries.Sources && i.Stored);
        var sources = new List<LookupOption> { new("ACTUALS_PAF", "ACTUALS_PAF (ACTUALS_PAF)") };
        Assert.Same(sources, ProjectService.Lookups([], [], sources)[ProjectDictionaries.Sources]);
    }

    [SqlFact]
    public void Project_imports_only_files_of_its_sources()
    {
        _database = new TestDatabase(presets: true);
        var app = _services.App(_root, _database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        Assert.True(config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS_PROGRAM1", "ACTUALS_PROGRAM1_", "Koszty programu 1", ActualsLayout.Parser, true)).Success);
        var imports = new SqlImportStore(_database.Sql);
        var store = new SqlProjectStore(_database.Sql, _services.Clock, _services.User);
        var service = new ProjectService(store, new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, [.. GlobalDictionaries.Tables, .. ProjectDictionaries.Tables]),
            app.Journal, new ProjectFolders(Path.Combine(_root, "Projekty")), null, () => new TestImport(new ImportService(imports, app)));
        var tree = PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx")).Tree;
        Assert.True(store.Create("M28", "M28", ProjectTypes.Internal, tree).Success);

        var context = service.Context("M28", tree, null, P1sInputs.None);
        Assert.Contains("ACTUALS_PROGRAM1", context.Sources!);
        Assert.Contains(service.SourceLookups(), o => o.Value == "ACTUALS_PROGRAM1" && o.Label == "ACTUALS_PROGRAM1 (ACTUALS_PROGRAM1_)");
        Assert.Equal(SaveStatus.Saved, service.SaveDictionary(ProjectDictionaries.Sources, context, [Source("ACTUALS_PROGRAM1")], [], "M28").Status);
        Assert.Equal(["ACTUALS_PROGRAM1"], service.ProjectSources("M28"));

        Directory.CreateDirectory(app.Config.ImportFolder);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Path.Combine(app.Config.ImportFolder, "ACTUALS_PAF_01.csv"));
        var program = Path.Combine(app.Config.ImportFolder, "ACTUALS_PROGRAM1_01.csv");
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), program);
        File.AppendAllText(program, "2DI473;2DI473001003;51105550;x;x;PLN;1,00;PLN;1,00;USD;0,25;0,000;;;;;;;;2026;2026-03-31;3;\r\n");

        var result = service.Imports!.Import(service.ProjectSources("M28"), null, CancellationToken.None);

        Assert.Equal((1, 0), (result.Imported, result.Errors));
        Assert.Equal(["ACTUALS_PROGRAM1_01.csv"], imports.Seen(result.BatchId).Select(s => s.FileName));
        Assert.Equal(1, service.Imports.Import(service.ProjectSources("M28"), null, CancellationToken.None) is { Imported: 0, Errors: 0 } again
            ? imports.Seen(again.BatchId).Count(s => s.Decision == FileDecisions.Skipped) : -1);   // kolejny import – plik bez zmian pominięty
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
