using Dapper;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>Rejestr problemów w bazie (META_Problem).</summary>
public sealed class SqlProblemLog(SqlDatabase db, IClock clock) : IProblemLog
{
    private readonly string _table = db.Table("meta.Problem");

    public void Add(string area, string check, Issue issue, string? reference = null)
    {
        using var connection = db.Open();
        connection.Execute(
            $"INSERT INTO {_table} (At, Level, CheckName, Area, Element, Message, Reference, Resolved) VALUES (@At, @Level, @Check, @Area, @Element, @Message, @Reference, 0)",
            new { At = clock.Now, Level = issue.Level.ToString(), Check = check, Area = area, issue.Element, issue.Message, Reference = reference });
    }

    public IReadOnlyList<ProblemRecord> ByReference(string reference) => Query("WHERE Reference = @reference ORDER BY Id", new { reference });

    public IReadOnlyList<ProblemRecord> Open() => Query("WHERE Resolved = 0 ORDER BY Id", null);

    private List<ProblemRecord> Query(string where, object? parameters)
    {
        using var connection = db.Open();
        return connection.Query<ProblemRow>($"SELECT Id, At, Level, CheckName, Area, Element, Message, Reference, Resolved FROM {_table} {where}", parameters)
            .Select(r => new ProblemRecord(r.Id, r.At, Enum.Parse<CheckLevel>(r.Level), r.CheckName, r.Area, r.Element, r.Message, r.Reference, r.Resolved))
            .ToList();
    }

    private sealed class ProblemRow
    {
        public long Id { get; set; }
        public DateTimeOffset At { get; set; }
        public string Level { get; set; } = "";
        public string CheckName { get; set; } = "";
        public string Area { get; set; } = "";
        public string? Element { get; set; }
        public string Message { get; set; } = "";
        public string? Reference { get; set; }
        public bool Resolved { get; set; }
    }
}
