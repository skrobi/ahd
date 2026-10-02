using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Tests.InMemory;

namespace PzlEv.Tests.TestSupport;

/// <summary>Usługi wspólne do testów: baza w pamięci (atrapy magazynów), zegar testowy, użytkownik testowy.</summary>
public sealed class TestServices
{
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(2)));

    public TestUser User { get; } = new();

    public InMemoryDatabase Database { get; } = new();

    public InMemoryJournal Journal => new(Database, Clock, User);

    public InMemoryProblemLog Problems => new(Database, Clock);

    /// <summary>
    /// Usługi aplikacji z korzeniem folderów w katalogu tymczasowym testu: sql – baza testowa (dziennik i problemy
    /// w bazie); bez sql – dziennik i problemy w pamięci, a opis bazy tylko formalny (testy nie łączą się z nią).
    /// </summary>
    public AppServices App(string networkRoot, SqlDatabase? sql = null)
    {
        var config = new AppConfig("TEST", networkRoot, sql?.Settings ?? new SqlSettings("test", "test", "FINOP", "PZLEV_"));
        var locks = new FileOperationLock(Path.Combine(networkRoot, "00_Global", "RABIT"), Clock, User);
        return sql is null
            ? new AppServices(config, Clock, User, new SqlDatabase(config.Sql, "test"), Journal, Problems, "test", locks)
            : new AppServices(config, Clock, User, sql, new SqlJournal(sql, Clock, User), new SqlProblemLog(sql, Clock), "test", locks);
    }

    /// <summary>Plik danych wzorcowych (app/testdata) skopiowany do katalogu testów.</summary>
    public static string TestData(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "testdata", .. parts]);
}
