using Dapper;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>Dziennik zdarzeń w bazie (META_Journal).</summary>
public sealed class SqlJournal(SqlDatabase db, IClock clock, ICurrentUser user) : IJournal
{
    private readonly string _table = db.Table("meta.Journal");

    public void Add(string area, string message, string? scope = null)
    {
        using var connection = db.Open();
        connection.Execute($"INSERT INTO {_table} (At, UserName, Area, Scope, Message) VALUES (@At, @User, @Area, @Scope, @Message)",
            new { At = clock.Now, User = user.Account, Area = area, Scope = scope, Message = message });
    }

    public IReadOnlyList<JournalEntry> Recent(int count)
    {
        using var connection = db.Open();
        return connection.Query<JournalRow>($"SELECT TOP (@count) Id, At, UserName, Area, Scope, Message FROM {_table} ORDER BY Id DESC", new { count })
            .Select(r => new JournalEntry(r.Id, r.At, r.UserName, r.Area, r.Scope, r.Message))
            .ToList();
    }

    private sealed class JournalRow
    {
        public long Id { get; set; }
        public DateTimeOffset At { get; set; }
        public string UserName { get; set; } = "";
        public string Area { get; set; } = "";
        public string? Scope { get; set; }
        public string Message { get; set; } = "";
    }
}
