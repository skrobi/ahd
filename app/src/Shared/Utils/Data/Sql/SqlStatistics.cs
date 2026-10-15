namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// Statystyka poleceń SQL sesji (SqlProfiler): polecenie = baza + tekst; liczba wykonań, łączny, średni i najdłuższy czas,
/// błędy. Podsumowanie – polecenia o największym łącznym czasie (co najbardziej spowalnia aplikację) i polecenia z błędem.
/// </summary>
public sealed class SqlStatistics
{
    private const int MaxText = 200;

    private sealed class Entry
    {
        public int Count;
        public int Errors;
        public double TotalMs;
        public double MaxMs;
        public string? LastError;
        public readonly HashSet<string> Operations = [];
    }

    private readonly Dictionary<(string? Database, string Sql), Entry> _entries = [];

    public void Add(SqlProfile profile)
    {
        lock (_entries)
        {
            var key = (profile.Database, profile.Sql);
            if (!_entries.TryGetValue(key, out var entry))
                _entries[key] = entry = new Entry();
            var ms = profile.Elapsed.TotalMilliseconds;
            entry.Count++;
            entry.TotalMs += ms;
            entry.MaxMs = Math.Max(entry.MaxMs, ms);
            if (profile.Error is not null)
            {
                entry.Errors++;
                entry.LastError = profile.Error;
            }
            if (profile.Operation is not null && entry.Operations.Count < 5)
                entry.Operations.Add(profile.Operation);
        }
    }

    /// <summary>Linie podsumowania: łącznie, top polecenia według łącznego czasu, polecenia z błędem (brak poleceń – pusto).</summary>
    public IReadOnlyList<string> Summary(int top)
    {
        lock (_entries)
        {
            if (_entries.Count == 0)
                return [];
            var lines = new List<string>
            {
                $"SQL – podsumowanie sesji: {_entries.Values.Sum(e => e.Count)} poleceń ({_entries.Count} różnych), " +
                $"błędy {_entries.Values.Sum(e => e.Errors)}, łącznie {_entries.Values.Sum(e => e.TotalMs):0} ms; najdłużej łącznie:",
            };
            lines.AddRange(_entries.OrderByDescending(e => e.Value.TotalMs).Take(top).Select(e =>
                $"  {e.Value.TotalMs,8:0} ms · {e.Value.Count}× · śr. {e.Value.TotalMs / e.Value.Count:0} ms · max {e.Value.MaxMs:0} ms" +
                $" [{e.Key.Database}]{Steps(e.Value)}: {Short(e.Key.Sql)}"));
            var failed = _entries.Where(e => e.Value.Errors > 0).OrderByDescending(e => e.Value.Errors).ToList();
            if (failed.Count > 0)
            {
                lines.Add("SQL – polecenia z błędem:");
                lines.AddRange(failed.Take(top).Select(e =>
                    $"  {e.Value.Errors}× z {e.Value.Count} [{e.Key.Database}]{Steps(e.Value)}: {Short(e.Key.Sql)} – {e.Value.LastError}"));
            }
            return lines;
        }
    }

    private static string Steps(Entry entry) => entry.Operations.Count == 0 ? "" : $" ({string.Join(", ", entry.Operations)})";

    private static string Short(string sql) => sql.Length > MaxText ? sql[..MaxText] + "…" : sql;
}
