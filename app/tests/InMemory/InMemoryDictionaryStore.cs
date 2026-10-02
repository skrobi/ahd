using PzlEv.Modules.MasterData.Data;
using PzlEv.Modules.MasterData.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Tests.InMemory;

/// <summary>Magazyn słowników w pamięci (tabela dict.Entry) – do testów (aplikacja: SqlDictionaryStore).</summary>
public sealed class InMemoryDictionaryStore(InMemoryDatabase db, IClock clock, ICurrentUser user) : IDictionaryStore
{
    public const string Table = "dict.Entry";

    public IReadOnlyList<DictionaryEntryRow> Current(string dictionary, string? project = null) =>
        db.Read(() => Rows().Where(r => r.Dictionary == dictionary && r.Project == project && r.IsCurrent).OrderBy(r => r.RowId).ToList());

    public IReadOnlyList<DictionaryEntryRow> AsOf(string dictionary, DateTimeOffset moment, string? project = null) =>
        db.Read(() => Rows().Where(r => r.Dictionary == dictionary && r.Project == project && r.ValidAt(moment)).OrderBy(r => r.RowId).ToList());

    public IReadOnlyList<DictionaryEntryRow> History(long rowId) =>
        db.Read(() => Rows().Where(r => r.RowId == rowId).OrderBy(r => r.Version).ToList());

    public StoreResult Save(string dictionary, string? project, IReadOnlyList<RowChange> changes)
    {
        var result = db.Write(() =>
        {
            var rows = Rows();
            var current = rows.Where(r => r.Dictionary == dictionary && r.Project == project && r.IsCurrent).ToDictionary(r => r.RowId);

            // 1. Sprawdzenie (bez zmian w danych): wersje wierszy i unikalność kluczy po zapisie.
            foreach (var change in changes.Where(c => c.Kind != RowChangeKind.Added))
            {
                if (change.RowId is not { } id || !current.TryGetValue(id, out var existing))
                    return StoreResult.Rejected($"Wiersz {change.Key} został w międzyczasie usunięty – odśwież dane.");
                if (existing.Version != change.ExpectedVersion)
                    return StoreResult.Rejected($"Wiersz {change.Key} zmienił {existing.RecordedBy} ({existing.RecordedAt:yyyy-MM-dd HH:mm}) – odśwież dane.");
            }
            var keysAfter = current.Values
                .Where(r => !changes.Any(c => c.Kind != RowChangeKind.Added && c.RowId == r.RowId))
                .Select(r => r.Key)
                .Concat(changes.Where(c => c.Kind != RowChangeKind.Removed).Select(c => c.Key))
                .GroupBy(k => k)
                .FirstOrDefault(g => g.Count() > 1);
            if (keysAfter is not null)
                return StoreResult.Rejected($"Klucz {keysAfter.Key} już istnieje w słowniku – odśwież dane.");

            // 2. Zapis.
            var now = clock.Now;
            int added = 0, updated = 0, removed = 0;
            foreach (var change in changes)
            {
                switch (change.Kind)
                {
                    case RowChangeKind.Added:
                        rows.Add(new DictionaryEntryRow(db.NextId(Table), db.NextId(Table + ".RowId"), 1, dictionary, project,
                            change.Key, Copy(change.Values), now, user.Account, null, null));
                        added++;
                        break;
                    case RowChangeKind.Updated:
                    {
                        var existing = current[change.RowId!.Value];
                        Supersede(rows, existing, now);
                        rows.Add(existing with
                        {
                            Id = db.NextId(Table), Version = existing.Version + 1, Key = change.Key, Values = Copy(change.Values),
                            RecordedAt = now, RecordedBy = user.Account, SupersededAt = null, SupersededBy = null,
                        });
                        updated++;
                        break;
                    }
                    case RowChangeKind.Removed:
                        Supersede(rows, current[change.RowId!.Value], now);
                        removed++;
                        break;
                }
            }
            return new StoreResult(true, null, added, updated, removed);
        });
        if (result.Success)
            db.Commit();
        return result;
    }

    private List<DictionaryEntryRow> Rows() => db.Table<DictionaryEntryRow>(Table);

    private void Supersede(List<DictionaryEntryRow> rows, DictionaryEntryRow existing, DateTimeOffset now)
    {
        var index = rows.FindIndex(r => r.Id == existing.Id);
        rows[index] = existing with { SupersededAt = now, SupersededBy = user.Account };
    }

    private static Dictionary<string, string?> Copy(IReadOnlyDictionary<string, string?>? values) =>
        values is null ? new() : new Dictionary<string, string?>(values);
}
