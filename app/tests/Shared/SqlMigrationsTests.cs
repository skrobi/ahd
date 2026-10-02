using Dapper;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Import.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Migracje schematu (sql/mssql) i nazwy obiektów z konfiguracji.</summary>
public sealed class SqlMigrationsTests
{
    private static readonly SqlDatabase Prod = new(new SqlSettings("pzltestdb.intl.lmco.com", "PZLTEST", "FINOP", "PZLEV_"), "0.8.0");

    [Fact]
    public void Logical_names_map_to_schema_and_signature()
    {
        Assert.Equal("[FINOP].[PZLEV_META_ImportBatch]", Prod.Table("meta.ImportBatch"));
        Assert.Equal("[FINOP].[PZLEV_DICT_FxRate]", Prod.Table("dict.FxRate"));
        Assert.Contains("Integrated Security=True", Prod.ConnectionString);
        Assert.Contains("Application Name=\"PZL-EV 0.8.0\"", Prod.ConnectionString);
    }

    [Fact]
    public void Scripts_are_embedded_numbered_and_parameterised()
    {
        var scripts = SqlMigrations.All();
        Assert.Equal([1, 2, 3, 4], scripts.Select(s => s.Number));
        Assert.Equal([false, true, false, false], scripts.Select(s => s.IsPresets));   // 002_dane_startowe – dane startowe
        Assert.Equal(4, SqlMigrations.Required);

        var batches = SqlMigrations.Batches(scripts[0].Text, "FINOP", "PZLEV_").ToList();
        Assert.True(batches.Count > 5);
        Assert.Contains(batches, b => b.Contains("CREATE TABLE [FINOP].[PZLEV_META_Project]"));
        Assert.All(scripts.SelectMany(s => SqlMigrations.Batches(s.Text, "FINOP", "PZLEV_")),
            b => Assert.False(b.Contains("$(Schema)") || b.Contains("$(Prefix)")));
    }

    [SqlFact]
    public void Migrations_create_stage_1_tables_and_skip_executed_scripts()
    {
        using var database = new TestDatabase();   // bez danych startowych

        Assert.Equal(["002_dane_startowe.sql"], SqlMigrations.Pending(database.Sql).Select(s => s.Name));
        Assert.Empty(SqlMigrations.Apply(database.Sql, presets: false));
        Assert.Equal(["002_dane_startowe.sql"], SqlMigrations.Apply(database.Sql));   // 001 wykonana – pominięta
        Assert.Empty(SqlMigrations.Apply(database.Sql));                              // wszystko wykonane
        Assert.Empty(SqlMigrations.Pending(database.Sql));
        var status = SqlMigrations.Status(database.Sql);
        Assert.All(status, s => Assert.NotNull(s.AppliedAt));
        Assert.Equal(4, SqlMigrations.CurrentVersion(database.Sql));
        using var connection = database.Sql.Open();
        var tables = connection.Query<string>(
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = @schema AND LEFT(t.name, LEN(@prefix)) = @prefix",
            new { schema = TestDatabase.Schema, prefix = database.Sql.Settings.TablePrefix }).ToList();
        Assert.Equal(21, tables.Count);   // 20 z migracji 001 + META_Parser (004)
        Assert.Contains(database.Sql.Settings.TablePrefix + "META_PerformanceObjective", tables);
        Assert.Contains(database.Sql.Settings.TablePrefix + "DICT_ScheduleBudget", tables);

        // 004: parser ACTUALS w bazie, CAN_Actuals – tabela parsera (kolumny dopuszczają NULL, nowe pola)
        var actuals = Assert.Single(new SqlSourceConfigStore(database.Sql, new TestServices().Clock, new TestUser()).Parsers());
        Assert.Equal(("ACTUALS", "Actuals", 26), (actuals.Code, actuals.Table, actuals.Fields.Count));
        Assert.Equal(new ParserField("CostElement", "Cost Element", FieldTypes.Text, 10, 10), actuals.Fields.Single(f => f.Field == "CostElement"));
        var columns = connection.Query<(string Name, bool Nullable)>(
            "SELECT name, is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(@table)", new { table = database.Sql.Table("can.Actuals") }).ToList();
        Assert.All(actuals.Fields, f => Assert.Contains(columns, c => c.Name == f.Field && c.Nullable));
    }

    [SqlFact]
    public void Migration_started_by_two_people_at_once_runs_the_script_once()
    {
        using var database = new TestDatabase();   // bez danych startowych – 002 do wykonania
        var results = new IReadOnlyList<string>[2];

        Parallel.For(0, 2, i => results[i] = SqlMigrations.Apply(database.Sql));   // blokada sp_getapplock

        Assert.Equal(["002_dane_startowe.sql"], results.SelectMany(r => r));
        using var connection = database.Sql.Open();
        Assert.Equal(2, connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {database.Sql.Table("meta.SourceDefinition")}"));
    }

    [SqlFact]
    public void Migration_003_removes_files_stored_despite_failed_validation()
    {
        using var database = new TestDatabase();
        var clock = new TestServices().Clock;
        var store = new SqlImportStore(database.Sql);
        var batch = store.BeginBatch(clock.Now, @"PZL\test", "PC-1", "0.11.0");
        long Register(char hash, string name, string canonicalStatus)
        {
            var sha = new string(hash, 64);
            var id = store.RegisterFile(new SourceFileRow(0, sha, "RABIT", name, "ACTUALS_PAF", 10, clock.Now, "csv", null, "utf-8", ";",
                ["A"], "sygnatura", 1, batch, clock.Now, @"PZL\test", "w toku", 0, null), [["1"]])!.Value;
            store.CompleteCanonical(id, canonicalStatus, null, null);
            store.RecordSeen(new SourceFileSeenRow(0, batch, "RABIT", name, 10, clock.Now, sha, FileDecisions.Imported, "ACTUALS_PAF", 1, "zapis wersji 0.11"));
            return id;
        }
        var layout = Register('a', "ACTUALS_PAF2_B6_AC1.xlsx", "brak – sygnatura kolumn niezgodna z definicją");
        var values = Register('b', "ACTUALS_PAF2_B6_AC2.xlsx", "brak – 3 błędów wartości");
        var rawOnly = Register('c', "FORECAST_PAF.xlsx", "brak – źródło bez parsera (tylko wiersze surowe)");
        Assert.NotNull(store.LastSettled("RABIT", "ACTUALS_PAF2_B6_AC1.xlsx"));
        using (var connection = database.Sql.Open())   // baza sprzed migracji 003
            connection.Execute($"DELETE FROM {database.Sql.Table("meta.SchemaVersion")} WHERE Version = 3");

        Assert.Equal(["003_usuniecie_plikow_niezgodnych.sql"], SqlMigrations.Apply(database.Sql, presets: false));

        Assert.Null(store.File(layout));
        Assert.Null(store.File(values));
        Assert.Empty(store.RawRows(layout));
        Assert.NotNull(store.File(rawOnly));
        Assert.Single(store.RawRows(rawOnly));
        Assert.Null(store.LastSettled("RABIT", "ACTUALS_PAF2_B6_AC1.xlsx"));    // kolejny import pobierze plik ponownie
        Assert.NotNull(store.LastSettled("RABIT", "FORECAST_PAF.xlsx"));
        Assert.Equal(3, store.Seen(batch).Count);                              // historia decyzji zostaje
        Assert.Contains(new SqlJournal(database.Sql, clock, new TestUser()).Recent(3), e => e.Message ==
            "Migracja 003 – usunięte pliki niezgodne z definicją źródła: 2 (wiersze surowe: 2); kolejny import pobierze je ponownie");
    }
}
