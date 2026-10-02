using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

public sealed class InMemoryProblemLog(InMemoryDatabase db, IClock clock) : IProblemLog
{
    public const string Table = "meta.Problem";

    public void Add(string area, string check, Issue issue, string? reference = null)
    {
        db.Write(() => db.Table<ProblemRecord>(Table).Add(new ProblemRecord(
            db.NextId(Table), clock.Now, issue.Level, check, area, issue.Element, issue.Message, reference, Resolved: false)));
    }

    public IReadOnlyList<ProblemRecord> ByReference(string reference) =>
        db.Read(() => db.Table<ProblemRecord>(Table).Where(p => p.Reference == reference).ToList());

    public IReadOnlyList<ProblemRecord> Open() =>
        db.Read(() => db.Table<ProblemRecord>(Table).Where(p => !p.Resolved).ToList());
}
