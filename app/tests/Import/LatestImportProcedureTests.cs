using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Utils.Data;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Import;

/// <summary>
/// Procedura CAN_LatestImport (migracja 011): dane kanoniczne parsera z najnowszej wersji każdego pliku, pola parsera
/// pod własnymi nazwami; import w kilku partiach, stan na moment, filtr źródła.
/// </summary>
public sealed class LatestImportProcedureTests : IDisposable
{
    private const string ExtraRow = "2DI473;2DI473001003;51105550;x;x;PLN;1,00;PLN;1,00;USD;0,25;0,000;;;;;;;;2026;2026-03-31;3;\r\n";

    private readonly TestServices _services = new();
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-latest-").FullName;
    private TestDatabase? _database;
    private AppServices _app = null!;
    private ImportService _import = null!;

    private void Use()
    {
        _database = new TestDatabase(presets: true);
        _app = _services.App(_root, _database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), _app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        _import = new ImportService(new SqlImportStore(_database.Sql), _app);
    }

    /// <summary>Plik wzorcowy (6 wierszy) pod nazwą; extra – dopisane wiersze; modified – data raportu.</summary>
    private void Write(string name, int extra, DateTime modified)
    {
        Directory.CreateDirectory(_app.Config.ImportFolder);
        var target = Path.Combine(_app.Config.ImportFolder, name);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), target, overwrite: true);
        for (var i = 0; i < extra; i++)
            File.AppendAllText(target, ExtraRow);
        File.SetLastWriteTimeUtc(target, modified);
    }

    private List<IDictionary<string, object?>> Latest(string parser, string? sourceCode = null, DateTimeOffset? asOf = null)
    {
        using var connection = _database!.Sql.Open();
        return connection.Query(_database.Sql.Table("can.LatestImport"), new { Parser = parser, SourceCode = sourceCode, AsOf = asOf },
                commandType: CommandType.StoredProcedure)
            .Select(r => (IDictionary<string, object?>)r)
            .ToList();
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [SqlFact]
    public void Returns_newest_version_of_each_file_across_import_batches_with_parser_field_names()
    {
        Use();
        Write("ACTUALS_PAF_01.csv", 0, new DateTime(2026, 10, 6, 23, 0, 0, DateTimeKind.Utc));
        var first = _import.Run();
        DateTimeOffset afterFirst;
        using (var connection = _database!.Sql.Open())
            afterFirst = connection.ExecuteScalar<DateTimeOffset>($"SELECT MAX(ImportedAt) FROM {_database.Sql.Table("meta.SourceFile")}");
        _services.Clock.Advance(TimeSpan.FromHours(1));

        Write("ACTUALS_PAF_01.csv", 1, new DateTime(2026, 10, 7, 23, 0, 0, DateTimeKind.Utc));   // nowa wersja – partia nocna 1
        var second = _import.Run();
        Write("ACTUALS_PAF_02.csv", 2, new DateTime(2026, 10, 7, 23, 30, 0, DateTimeKind.Utc));  // inny plik – partia nocna 2
        var third = _import.Run();

        var rows = Latest("ACTUALS");

        Assert.Equal(7 + 8, rows.Count);
        Assert.Equal(7, rows.Count(r => (string)r["FileName"]! == "ACTUALS_PAF_01.csv"));
        Assert.All(rows.Where(r => (string)r["FileName"]! == "ACTUALS_PAF_01.csv"), r => Assert.Equal(second.BatchId, (long)r["BatchId"]!));
        Assert.All(rows.Where(r => (string)r["FileName"]! == "ACTUALS_PAF_02.csv"), r => Assert.Equal(third.BatchId, (long)r["BatchId"]!));
        Assert.All(rows, r => Assert.Equal("ACTUALS_PAF", r["SourceCode"]));
        Assert.Contains("ValueObjCrcy", rows[0].Keys);                                             // pole parsera, nie slot
        Assert.DoesNotContain("N01", rows[0].Keys);
        Assert.Equal(10574.11m + 1m, rows.Where(r => (string)r["FileName"]! == "ACTUALS_PAF_01.csv").Sum(r => (decimal)r["ValueObjCrcy"]!));

        var before = Latest("ACTUALS", asOf: afterFirst);                                          // stan po pierwszym imporcie
        Assert.Equal(6, before.Count);
        Assert.All(before, r => Assert.Equal(first.BatchId, (long)r["BatchId"]!));

        Assert.Empty(Latest("ACTUALS", sourceCode: "ACTUALS_CES"));
        Assert.Equal(15, Latest("ACTUALS", sourceCode: "ACTUALS_PAF").Count);
    }

    [SqlFact]
    public void Unknown_parser_is_an_error()
    {
        Use();

        var error = Assert.Throws<SqlException>(() => Latest("NIEZNANY"));

        Assert.Equal(50011, error.Number);
        Assert.Contains("NIEZNANY", error.Message);
    }
}
