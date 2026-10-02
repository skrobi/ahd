using Dapper;
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
        Assert.Equal([1, 2], scripts.Select(s => s.Number));
        Assert.Equal([false, true], scripts.Select(s => s.IsPresets));   // 002_dane_startowe – dane startowe
        Assert.Equal(2, SqlMigrations.Required);

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

        Assert.Equal(1, SqlMigrations.CurrentVersion(database.Sql));
        Assert.Equal(["002_dane_startowe.sql"], SqlMigrations.Pending(database.Sql).Select(s => s.Name));
        Assert.Empty(SqlMigrations.Apply(database.Sql, presets: false));
        Assert.Equal(["002_dane_startowe.sql"], SqlMigrations.Apply(database.Sql));   // 001 wykonana – pominięta
        Assert.Empty(SqlMigrations.Apply(database.Sql));                              // wszystko wykonane
        Assert.Empty(SqlMigrations.Pending(database.Sql));
        var status = SqlMigrations.Status(database.Sql);
        Assert.All(status, s => Assert.NotNull(s.AppliedAt));
        Assert.Equal(2, SqlMigrations.CurrentVersion(database.Sql));
        using var connection = database.Sql.Open();
        var tables = connection.Query<string>(
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = @schema AND LEFT(t.name, LEN(@prefix)) = @prefix",
            new { schema = TestDatabase.Schema, prefix = database.Sql.Settings.TablePrefix }).ToList();
        Assert.Equal(20, tables.Count);
        Assert.Contains(database.Sql.Settings.TablePrefix + "META_PerformanceObjective", tables);
        Assert.Contains(database.Sql.Settings.TablePrefix + "DICT_ScheduleBudget", tables);
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
}
