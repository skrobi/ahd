using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Tests.TestSupport;

/// <summary>Usługi wspólne do testów: zegar testowy, użytkownik testowy, usługi aplikacji na bazie testowej.</summary>
public sealed class TestServices
{
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(2)));

    public TestUser User { get; } = new();

    /// <summary>Usługi aplikacji na bazie testowej (dziennik i problemy w bazie), korzeń folderów w katalogu testu.</summary>
    public AppServices App(string networkRoot, SqlDatabase sql)
    {
        var config = new AppConfig("TEST", networkRoot, sql.Settings);
        var locks = new FileOperationLock(Path.Combine(networkRoot, "00_Global", "RABIT"), Clock, User);
        return new AppServices(config, Clock, User, sql, new SqlJournal(sql, Clock, User), new SqlProblemLog(sql, Clock), "test", locks);
    }

    /// <summary>Plik danych wzorcowych (app/testdata) skopiowany do katalogu testów.</summary>
    public static string TestData(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "testdata", .. parts]);
}
