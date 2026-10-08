using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
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

    private List<IDictionary<string, object?>> Latest(string parser, string? sourceCode = null, DateTimeOffset? asOf = null, string? project = null)
    {
        using var connection = _database!.Sql.Open();
        return connection.Query(_database.Sql.Table("can.LatestImport"), new { Parser = parser, SourceCode = sourceCode, AsOf = asOf, Project = project },
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

    [SqlFact]
    public void Project_filter_returns_only_project_definitions_of_its_objectives()
    {
        Use();
        Write("ACTUALS_PAF_01.csv", 0, new DateTime(2026, 10, 6, 23, 0, 0, DateTimeKind.Utc));
        _import.Run();
        var tree = new PoTree();
        var root = tree.Add(new PoNode { Key = tree.NewKey(), WbsElement = "4D03GZ", Name = "projekt", ProjectDefinition = "4D03GZ" });
        tree.AddElement(root.Key, "4D03GZ000001", "element", null);
        Assert.True(new SqlProjectStore(_database!.Sql, _services.Clock, _services.User).Create("M28", "M28", ProjectTypes.Internal, tree).Success);

        var rows = Latest("ACTUALS", project: "M28");

        Assert.Equal(2, rows.Count);   // oba wiersze 4D03GZ (także element spoza nakładki – filtr po Project definition)
        Assert.All(rows, r => Assert.Equal("4D03GZ", r["ProjectDefinition"]));
        Assert.Equal(6, Latest("ACTUALS").Count);
        Assert.Equal(50013, Assert.Throws<SqlException>(() => Latest("ACTUALS", project: "BRAK")).Number);
    }

    /// <summary>Raport kosztów projektu (migracja 013): Project definition nakładki × Cost Element, wykluczenia, słownik, okres i lata.</summary>
    [SqlFact]
    public void Project_cost_report_groups_by_project_definition_and_cost_element()
    {
        Use();
        Write("ACTUALS_PAF_01.csv", 0, new DateTime(2026, 10, 6, 23, 0, 0, DateTimeKind.Utc));
        _import.Run();
        var tree = new PoTree();
        var group = tree.AddVirtual(null, "SWBS 2DI473");
        var project = tree.Add(new PoNode { Key = tree.NewKey(), ParentKey = group.Key, WbsElement = "2DI473", Name = "Projekt 2DI473", ProjectDefinition = "2DI473" });
        tree.Add(new PoNode { Key = tree.NewKey(), ParentKey = project.Key, WbsElement = "2DI473001001", Name = "element", ProjectDefinition = "2DI473" });
        Assert.True(new SqlProjectStore(_database!.Sql, _services.Clock, _services.User).Create("M28", "M28", ProjectTypes.Internal, tree).Success);
        using var connection = _database.Sql.Open();
        connection.Execute($"INSERT INTO {_database.Sql.Table("dict.Exclusion")} (RowId, Version, Project, WbsElement, Description, RecordedAt, RecordedBy) " +
                           "VALUES (1, 1, 'M28', '2DI473001002', N'poza raportem', SYSDATETIMEOFFSET(), 'test')");

        var rows = connection.Query(_database.Sql.Table("rep.ProjectCosts"), new { Project = "M28" }, commandType: CommandType.StoredProcedure)
            .Select(r => (IDictionary<string, object?>)r).ToList();

        Assert.Equal(["Grouping", "Project definition", "Project definition description", "Cost Element", "Cost Elem. Descr.", "Cost grouping", "Period 03/2026", "2026"],
            rows[0].Keys);
        Assert.Equal(2, rows.Count);   // 4D03GZ spoza nakładki; wiersze WBS 2DI473001002 wykluczone
        Assert.Equal(("SWBS 2DI473", "2DI473", "Projekt 2DI473", "0051105550", "PZL Mat Consump", "Direct Materials", 1254.51m, 1254.51m),
            ((string)rows[0]["Grouping"]!, (string)rows[0]["Project definition"]!, (string)rows[0]["Project definition description"]!, (string)rows[0]["Cost Element"]!,
             (string)rows[0]["Cost Elem. Descr."]!, (string)rows[0]["Cost grouping"]!, (decimal)rows[0]["Period 03/2026"]!, (decimal)rows[0]["2026"]!));
        Assert.Equal(("0092212550", 4800m), ((string)rows[1]["Cost Element"]!, (decimal)rows[1]["2026"]!));

        var (columns, report) = new SqlProjectStore(_database.Sql, _services.Clock, _services.User).CostReport("M28", "ValueObjCrcy");   // eksport z aplikacji
        Assert.Equal(rows[0].Keys, columns);
        Assert.Equal(1254.51m, report[0][7]);

        var usd = connection.Query(_database.Sql.Table("rep.ProjectCosts"), new { Project = "M28", Value = "ValueRepCur" }, commandType: CommandType.StoredProcedure)
            .Select(r => (IDictionary<string, object?>)r).ToList();
        Assert.NotEqual(1254.51m, (decimal)usd[0]["2026"]!);
        Assert.Equal(50014, Assert.Throws<SqlException>(() =>
            connection.Query(_database.Sql.Table("rep.ProjectCosts"), new { Project = "M28", Value = "Brak" }, commandType: CommandType.StoredProcedure).ToList()).Number);
    }

    /// <summary>Migracja 014: element CES dodany ręcznie (bez Project definition) – Project definition z jego WBS elementu.</summary>
    [SqlFact]
    public void Manually_added_top_element_without_project_definition_still_selects_its_costs()
    {
        Use();
        Write("ACTUALS_PAF_01.csv", 0, new DateTime(2026, 10, 6, 23, 0, 0, DateTimeKind.Utc));
        _import.Run();
        var tree = new PoTree();
        tree.AddElement(null, "4D03GZ", "projekt dodany ręcznie", null);
        Assert.True(new SqlProjectStore(_database!.Sql, _services.Clock, _services.User).Create("M29", "M29", ProjectTypes.Internal, tree).Success);

        Assert.Equal(2, Latest("ACTUALS", project: "M29").Count);
        var (columns, rows) = new SqlProjectStore(_database.Sql, _services.Clock, _services.User).CostReport("M29", "ValueObjCrcy");
        Assert.Equal(["0051105550", "9221X550"], rows.Select(r => (string)r[3]!));
        Assert.All(rows, r => Assert.Equal(("projekt dodany ręcznie", "4D03GZ", "projekt dodany ręcznie"), ((string)r[0]!, (string)r[1]!, (string)r[2]!)));
        Assert.Equal(-230.40m, rows[0][columns.ToList().IndexOf("2026")]);
    }

    /// <summary>Migracja 016: ACWP po elemencie CES – suma całego ostatniego importu, bez wykluczeń, tylko elementy projektu.</summary>
    [SqlFact]
    public void Project_costs_by_ces_element_sum_whole_latest_import_without_exclusions()
    {
        Use();
        Write("ACTUALS_PAF_01.csv", 0, new DateTime(2026, 10, 6, 23, 0, 0, DateTimeKind.Utc));
        _import.Run();
        var tree = new PoTree();
        var project = tree.Add(new PoNode { Key = tree.NewKey(), WbsElement = "2DI473", Name = "Projekt 2DI473", ProjectDefinition = "2DI473" });
        tree.Add(new PoNode { Key = tree.NewKey(), ParentKey = project.Key, WbsElement = "2DI473001001", Name = "element", ProjectDefinition = "2DI473" });
        var store = new SqlProjectStore(_database!.Sql, _services.Clock, _services.User);
        Assert.True(store.Create("M28", "M28", ProjectTypes.Internal, tree).Success);
        using var connection = _database.Sql.Open();
        connection.Execute($"INSERT INTO {_database.Sql.Table("dict.Exclusion")} (RowId, Version, Project, CostElement, Description, RecordedAt, RecordedBy) " +
                           "VALUES (1, 1, 'M28', '0057120550', N'delegacje poza analizą', SYSDATETIMEOFFSET(), 'test')");

        var costs = store.CostsByElement("M28", "ValueObjCrcy");

        // 2DI473001001: 1 254,51 + 4 800; 2DI473001002 (spoza nakładki, ale projektu): 2 400 bez wykluczonej delegacji (430); 4D03GZ – inny projekt.
        Assert.Equal(2, costs.Count);
        Assert.Equal(6054.51m, costs["2DI473001001"]);
        Assert.Equal(2400m, costs["2di473001002"]);
        Assert.Equal(50014, Assert.Throws<SqlException>(() => store.CostsByElement("M28", "Brak")).Number);
    }
}
