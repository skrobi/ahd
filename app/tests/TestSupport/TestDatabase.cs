using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;
using Xunit;

namespace PzlEv.Tests.TestSupport;

/// <summary>
/// Baza MS SQL do testów magazynów: ciąg połączenia w zmiennej środowiskowej PZLEV_TEST_SQL (np. lokalny SQL Server),
/// a bez niej – baza aplikacji z wzoru pzl-ev.json, tylko gdy Env = TEST (serwer i baza z Environments.TEST.Sql, konto
/// AD użytkownika – te same poświadczenia co aplikacja); baza niedostępna = testy SQL pominięte z powodem. Każda instancja zakłada tabele migracjami (sql/mssql; dane startowe NNN_dane_* tylko
/// z presets: true) z własną, losową sygnaturą w schemacie
/// FINOP i usuwa je po teście – testy nie dotykają tabel aplikacji i mogą działać równolegle. Zakładanie i usuwanie
/// tabel (DDL) jest szeregowane w procesie testów – równoległe DDL w jednym schemacie kończyły się zakleszczeniem.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public const string Variable = "PZLEV_TEST_SQL";

    /// <summary>„0” – testy SQL na bazie z pzl-ev.json wyłączone (build.cmd bez parametru sql – na zdalnej bazie TEST trwają długo).</summary>
    public const string Enabled = "PZLEV_SQL_TESTS";
    public const string Schema = "FINOP";

    private static readonly object Ddl = new();

    public TestDatabase(bool presets = false)
    {
        var connection = ConnectionString ?? throw new InvalidOperationException(Unavailable);
        Sql = new SqlDatabase(new SqlSettings("", "", Schema, $"T{Guid.NewGuid():N}"[..9] + "_", ConnectionString: connection), "test");
        lock (Ddl)
            SqlMigrations.Apply(Sql, presets);
    }

    private static readonly Lazy<(string? Connection, string Reason)> Source = new(Resolve);

    /// <summary>Ciąg połączenia bazy testowej; null – testy SQL pominięte (powód: Unavailable).</summary>
    public static string? ConnectionString => Source.Value.Connection;

    /// <summary>Skąd jest baza testowa albo dlaczego jej nie ma.</summary>
    public static string Unavailable => Source.Value.Reason;

    private static (string?, string) Resolve()
    {
        if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } value)
            return (value, $"zmienna {Variable}");

        if (Environment.GetEnvironmentVariable(Enabled) == "0")
            return (null, "Testy SQL pominięte (build.cmd bez parametru sql) – uruchom build.cmd sql albo dotnet test (app/README.md)");

        // Baza aplikacji (wzór pzl-ev.json w katalogu testów) – tylko środowisko TEST, nigdy PROD.
        AppConfig config;
        try
        {
            config = AppConfigLoader.Load(AppContext.BaseDirectory);
        }
        catch (InvalidOperationException ex)
        {
            return (null, $"Brak bazy testowej – ustaw zmienną {Variable} (app/README.md); pzl-ev.json: {ex.Message}");
        }
        if (config.Environment != "TEST")
            return (null, $"Brak bazy testowej – pzl-ev.json ma Env = {config.Environment} (testy używają tylko TEST); ustaw zmienną {Variable} (app/README.md)");
        // Krótki limit tylko na sprawdzenie dostępności – testy łączą się z domyślnym limitem (baza TEST bywa obciążona).
        var connection = new SqlDatabase(config.Sql, "test").ConnectionString;
        try
        {
            using var probe = new SqlConnection(new SqlConnectionStringBuilder(connection) { ConnectTimeout = 5 }.ConnectionString);
            probe.Open();
            return (connection, $"baza aplikacji z pzl-ev.json (TEST: {config.Sql.Describe})");
        }
        catch (Exception ex)   // SqlException; poza domeną także błąd logowania kontem AD
        {
            return (null, $"Baza aplikacji z pzl-ev.json (TEST: {config.Sql.Describe}) niedostępna – {ex.Message}; inna baza: zmienna {Variable} (app/README.md)");
        }
    }

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
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
            FROM sys.procedures o JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = @schema AND LEFT(o.name, LEN(@prefix)) = @prefix;
            SELECT @sql += N'DROP FUNCTION ' + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';'
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('IF', 'FN', 'TF') AND s.name = @schema AND LEFT(o.name, LEN(@prefix)) = @prefix;
            SELECT @sql += N'DROP SEQUENCE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(q.name) + N';'
            FROM sys.sequences q JOIN sys.schemas s ON s.schema_id = q.schema_id
            WHERE s.name = @schema AND LEFT(q.name, LEN(@prefix)) = @prefix;
            EXEC sp_executesql @sql;
            """,
            new { schema = Schema, prefix = Sql.Settings.TablePrefix });
    }
}

/// <summary>Test na bazie testowej (TestDatabase): pomijany, gdy nie ma bazy testowej (powód w komunikacie).</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (TestDatabase.ConnectionString is null)
            Skip = TestDatabase.Unavailable;
    }
}
