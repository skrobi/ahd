using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Import.Data;

/// <summary>Import w pamięci. Stan zapisywany do pliku na końcu importu (FinishBatch).</summary>
public sealed class InMemoryImportStore(InMemoryDatabase db) : IImportStore
{
    public IReadOnlyList<SourceDefinitionRow> ActiveDefinitions() =>
        db.Read(() => db.Table<SourceDefinitionRow>(DbTables.SourceDefinition).Where(d => d.IsCurrent && d.Active).ToList());

    public IReadOnlyList<SourceLocationRow> ActiveLocations() =>
        db.Read(() => db.Table<SourceLocationRow>(DbTables.SourceLocation).Where(l => l.IsCurrent && l.Active).OrderBy(l => l.Name).ToList());

    public long BeginBatch(DateTimeOffset at, string user, string machine, string appVersion) =>
        db.Write(() =>
        {
            var id = db.NextId(DbTables.ImportBatch);
            db.Table<ImportBatchRow>(DbTables.ImportBatch).Add(new ImportBatchRow(id, at, null, user, machine, appVersion, 0, 0, 0, 0, 0, 0, "w toku"));
            return id;
        });

    public SourceFileSeenRow? LastSettled(string location, string fileName) =>
        db.Read(() => db.Table<SourceFileSeenRow>(DbTables.SourceFileSeen)
            .Where(s => s.Location == location && s.FileName == fileName && FileDecisions.Settled.Contains(s.Decision))
            .MaxBy(s => s.Id));

    public SourceFileRow? FindByHash(string sha256) =>
        db.Read(() => db.Table<SourceFileRow>(DbTables.SourceFile).FirstOrDefault(f => f.Sha256 == sha256));

    public long? RegisterFile(SourceFileRow file, IReadOnlyList<string?[]> rows) =>
        db.Write<long?>(() =>
        {
            var files = db.Table<SourceFileRow>(DbTables.SourceFile);
            if (files.Any(f => f.Sha256 == file.Sha256))
                return null;
            var id = db.NextId(DbTables.SourceFile);
            files.Add(file with { Id = id });
            var raw = db.Table<RawRowRecord>(DbTables.RawRow);
            for (var i = 0; i < rows.Count; i++)
                raw.Add(new RawRowRecord(id, i + 1, rows[i]));
            return id;
        });

    public void CompleteCanonical(long fileId, string status, IReadOnlyList<ActualsRow> rows, int? parserVersion) =>
        db.Write(() =>
        {
            var files = db.Table<SourceFileRow>(DbTables.SourceFile);
            var index = files.FindIndex(f => f.Id == fileId);
            files[index] = files[index] with { CanonicalStatus = status, CanonicalRows = rows.Count, ParserVersion = parserVersion };
            db.Table<ActualsRow>(DbTables.Actuals).AddRange(rows);
        });

    public void RecordSeen(SourceFileSeenRow seen) =>
        db.Write(() => db.Table<SourceFileSeenRow>(DbTables.SourceFileSeen).Add(seen with { Id = db.NextId(DbTables.SourceFileSeen) }));

    public void FinishBatch(long batchId, DateTimeOffset at, int files, int imported, int skipped, int duplicates, int unrecognized, int errors, string status)
    {
        db.Write(() =>
        {
            var batches = db.Table<ImportBatchRow>(DbTables.ImportBatch);
            var index = batches.FindIndex(b => b.Id == batchId);
            batches[index] = batches[index] with
            {
                FinishedAt = at, Files = files, Imported = imported, Skipped = skipped, Duplicates = duplicates,
                Unrecognized = unrecognized, Errors = errors, Status = status,
            };
        });
        db.Commit();
    }

    public IReadOnlyList<ImportBatchRow> Batches(int count) =>
        db.Read(() => db.Table<ImportBatchRow>(DbTables.ImportBatch).OrderByDescending(b => b.Id).Take(count).ToList());

    public IReadOnlyList<SourceFileSeenRow> Seen(long batchId) =>
        db.Read(() => db.Table<SourceFileSeenRow>(DbTables.SourceFileSeen).Where(s => s.BatchId == batchId).OrderBy(s => s.Id).ToList());

    public SourceFileRow? File(long fileId) =>
        db.Read(() => db.Table<SourceFileRow>(DbTables.SourceFile).FirstOrDefault(f => f.Id == fileId));

    public IReadOnlyList<RawRowRecord> RawRows(long fileId) =>
        db.Read(() => db.Table<RawRowRecord>(DbTables.RawRow).Where(r => r.FileId == fileId).ToList());

    public IReadOnlyList<ActualsRow> Actuals(long fileId) =>
        db.Read(() => db.Table<ActualsRow>(DbTables.Actuals).Where(r => r.FileId == fileId).ToList());
}
