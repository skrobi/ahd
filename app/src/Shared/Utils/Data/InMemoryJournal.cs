using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

/// <summary>Wpis trafia do pliku stanu przy najbliższym Commit (operacja modułu albo zamknięcie aplikacji).</summary>
public sealed class InMemoryJournal(InMemoryDatabase db, IClock clock, ICurrentUser user) : IJournal
{
    public const string Table = "meta.Zdarzenie";

    public void Add(string area, string message, string? scope = null)
    {
        db.Write(() => db.Table<JournalEntry>(Table).Add(
            new JournalEntry(db.NextId(Table), clock.Now, user.Account, area, scope, message)));
    }

    public IReadOnlyList<JournalEntry> Recent(int count) =>
        db.Read(() => db.Table<JournalEntry>(Table).OrderByDescending(e => e.Id).Take(count).ToList());
}
