using Dapper;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;
using Xunit;

namespace PzlEv.Tests.TestSupport;

/// <summary>
/// Baza MS SQL do testów magazynów: ciąg połączenia w zmiennej środowiskowej PZLEV_TEST_SQL (np. baza TEST albo
/// lokalny SQL Server). Każda instancja zakłada tabele migracjami (sql/mssql; dane startowe NNN_dane_* tylko
/// z presets: true) z własną, losową sygnaturą w schemacie
/// FINOP i usuwa je po teście – testy nie dotykają tabel aplikacji i mogą działać równolegle. Zakładanie i usuwanie
/// tabel (DDL) jest szeregowane w procesie testów – równoległe DDL w jednym schemacie kończyły się zakleszczeniem.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public const string Variable = "PZLEV_TEST_SQL";
    public const string Schema = "FINOP";

    private static readonly object Ddl = new();

    public TestDatabase(bool presets = false)
    {
        var connection = ConnectionString ?? throw new InvalidOperationException($"Brak zmiennej {Variable} – testy SQL wyłączone");
        Sql = new SqlDatabase(new SqlSettings("", "", Schema, $"T{Guid.NewGuid():N}"[..9] + "_", ConnectionString: connection), "test");
        lock (Ddl)
            SqlMigrations.Apply(Sql, presets);
    }

    public static string? ConnectionString => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } value ? value : null;

    public SqlDatabase Sql { get; }

    public void Dispose()
    {
        lock (Ddl)
            DropTables();
    }

    private void DropTables()
    {
        using var connection = Sql.Open();
        connection.Execute(
            """
            DECLARE @sql NVARCHAR(MAX) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
            FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND LEFT(t.name, LEN(@prefix)) = @prefix;
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND LEFT(t.name, LEN(@prefix)) = @prefix;
            SELECT @sql += N'DROP SEQUENCE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(q.name) + N';'
            FROM sys.sequences q JOIN sys.schemas s ON s.schema_id = q.schema_id
            WHERE s.name = @schema AND LEFT(q.name, LEN(@prefix)) = @prefix;
            EXEC sp_executesql @sql;
            """,
            new { schema = Schema, prefix = Sql.Settings.TablePrefix });
    }
}

/// <summary>Test na bazie testowej (TestDatabase): pomijany, gdy nie ustawiono PZLEV_TEST_SQL.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (TestDatabase.ConnectionString is null)
            Skip = $"Brak bazy testowej – ustaw zmienną {TestDatabase.Variable} (app/README.md)";
    }
}
