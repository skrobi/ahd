using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Tests.TestSupport;

/// <summary>Usługi wspólne do testów: baza w pamięci bez pliku stanu, zegar testowy, użytkownik testowy.</summary>
public sealed class TestServices
{
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(2)));

    public TestUser User { get; } = new();

    public InMemoryDatabase Database { get; } = new();

    public InMemoryJournal Journal => new(Database, Clock, User);

    public InMemoryProblemLog Problems => new(Database, Clock);

    /// <summary>Usługi aplikacji z korzeniem folderów w katalogu tymczasowym testu.</summary>
    public AppServices App(string networkRoot) =>
        new(AppConfigLoader.Defaults() with { NetworkRoot = networkRoot, InMemoryStatePath = Path.Combine(networkRoot, "state.json") },
            Clock, User, Database, Journal, Problems, "test", new FileOperationLock(Path.Combine(networkRoot, "00_Global", "RABIT"), Clock, User));

    /// <summary>Plik danych wzorcowych (app/testdata) skopiowany do katalogu testów.</summary>
    public static string TestData(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "testdata", .. parts]);
}
