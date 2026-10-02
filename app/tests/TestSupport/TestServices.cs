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
}
