using System.Diagnostics;
using System.Text;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace PzlEv.Tests.Import;

/// <summary>
/// Ręczny test wydajności importu dużego pliku ACTUALS (wiersze wzorcowe powtórzone): czas, szczytowa pamięć procesu,
/// liczba wierszy i sumy w CAN_Row. Uruchamiany tylko ze zmienną PZLEV_PERF_ROWS (np. 2000000) i bazą testową.
/// </summary>
public sealed class ImportPerformanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-perf-").FullName;
    private TestDatabase? _database;

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [PerfFact]
    public void Large_actuals_file_is_imported_streaming()
    {
        var rows = PerfFactAttribute.Rows!.Value / 6 * 6;
        var services = new TestServices();
        _database = new TestDatabase(presets: true);
        var app = services.App(_root, _database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, services.Clock, services.User), app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        var store = new SqlImportStore(_database.Sql);

        var sample = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Encoding.UTF8);
        Directory.CreateDirectory(app.Config.ImportFolder);
        var path = Path.Combine(app.Config.ImportFolder, "ACTUALS_PAF_PERF.csv");
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            writer.WriteLine(sample[0]);
            for (var i = 0; i < rows / 6; i++)
                foreach (var line in sample.Skip(1))
                    writer.WriteLine(line.Replace("FV/2026/03/", $"FV/{i}/"));   // każda partia inna
        }
        var size = new FileInfo(path).Length;

        var watch = Stopwatch.StartNew();
        var run = new ImportService(store, app).Run();
        watch.Stop();

        var file = Assert.Single(run.Files);
        Assert.True(file.Decision == FileDecisions.Imported, file.Description);
        Assert.Equal(rows, file.Rows);
        var stored = store.FindByHash(Assert.Single(store.Seen(run.BatchId)).Sha256!)!;
        Assert.Equal(rows, stored.CanonicalRows);
        Assert.Contains($"suma Value in Obj. Crcy {PzlEv.Shared.Utils.Files.PolishNumber.ToDisplay(10574.11m * (rows / 6))}", file.Description);
        output.WriteLine($"{rows:#,0} wierszy, plik {size / 1048576.0:0.0} MB: {watch.Elapsed.TotalSeconds:0.0} s " +
                         $"({rows / watch.Elapsed.TotalSeconds:#,0} wierszy/s), szczytowa pamięć procesu {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576:#,0} MB");
    }
}

/// <summary>Test wydajności: pomijany bez bazy testowej albo bez zmiennej PZLEV_PERF_ROWS (liczba wierszy).</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public const string Variable = "PZLEV_PERF_ROWS";

    public PerfFactAttribute()
    {
        if (TestDatabase.ConnectionString is null || Rows is null)
            Skip = $"Test wydajności – ustaw {TestDatabase.Variable} i {Variable} (np. 2000000)";
    }

    public static int? Rows => int.TryParse(Environment.GetEnvironmentVariable(Variable), out var rows) && rows >= 6 ? rows : null;
}
