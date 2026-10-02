using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Tests.TestSupport;

/// <summary>Usługi wspólne do testów: baza w pamięci bez pliku stanu, zegar testowy, użytkownik testowy.</summary>
public sealed class TestServices
{
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(2)));

    public TestUser User { get; } = new();

    public InMemoryDatabase Database { get; } = new();

    public InMemoryJournal Journal => new(Database, Clock, User);

    public InMemoryProblemLog Problems => new(Database, Clock);

    /// <summary>Usługi aplikacji z korzeniem folderów w katalogu tymczasowym testu; sql – dziennik i problemy w bazie testowej.</summary>
    public AppServices App(string networkRoot, SqlDatabase? sql = null)
    {
        var config = AppConfigLoader.Defaults() with
        {
            NetworkRoot = networkRoot,
            InMemoryStatePath = Path.Combine(networkRoot, "state.json"),
            DataMode = sql is null ? DataMode.InMemory : DataMode.Sql,
        };
        var locks = new FileOperationLock(Path.Combine(networkRoot, "00_Global", "RABIT"), Clock, User);
        return sql is null
            ? new AppServices(config, Clock, User, Database, Journal, Problems, "test", locks)
            : new AppServices(config, Clock, User, Database, new SqlJournal(sql, Clock, User), new SqlProblemLog(sql, Clock), "test", locks) { Sql = sql };
    }

    /// <summary>Plik danych wzorcowych (app/testdata) skopiowany do katalogu testów.</summary>
    public static string TestData(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "testdata", .. parts]);
}
