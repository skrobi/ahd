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
        var script = Assert.Single(SqlMigrations.All());
        Assert.Equal(1, script.Number);
        Assert.Equal(1, SqlMigrations.Required);

        var batches = SqlMigrations.Batches(script.Text, "FINOP", "PZLEV_").ToList();

        Assert.True(batches.Count > 5);
        Assert.DoesNotContain(batches, b => b.Contains("$(Schema)") || b.Contains("$(Prefix)"));
        Assert.Contains(batches, b => b.Contains("CREATE TABLE [FINOP].[PZLEV_META_Project]"));
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Migrations_create_stage_1_tables_once(string store)
    {
        if (store != TestStores.Sql)
            return;   // tylko na bazie testowej
        using var database = new TestDatabase();

        Assert.Equal(1, SqlMigrations.CurrentVersion(database.Sql));
        Assert.Empty(SqlMigrations.Apply(database.Sql));
        using var connection = database.Sql.Open();
        var tables = connection.Query<string>(
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = @schema AND LEFT(t.name, LEN(@prefix)) = @prefix",
            new { schema = TestDatabase.Schema, prefix = database.Sql.Settings.TablePrefix }).ToList();
        Assert.Equal(20, tables.Count);
        Assert.Contains(database.Sql.Settings.TablePrefix + "META_PerformanceObjective", tables);
        Assert.Contains(database.Sql.Settings.TablePrefix + "DICT_ScheduleBudget", tables);
    }
}
