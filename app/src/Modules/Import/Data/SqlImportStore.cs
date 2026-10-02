using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Modules.Import.Data;

/// <summary>
/// Import w bazie: META_ImportBatch, META_SourceFile, META_SourceFileSeen, STG_RawRow i tabele parserów (CAN_*).
/// Wiersze surowe i kanoniczne zapisywane wsadowo (SqlBulkCopy) w transakcji.
/// </summary>
public sealed class SqlImportStore(SqlDatabase db) : IImportStore
{
    private readonly string _definitions = db.Table(DbTables.SourceDefinition);
    private readonly string _locations = db.Table(DbTables.SourceLocation);
    private readonly string _batches = db.Table(DbTables.ImportBatch);
    private readonly string _files = db.Table(DbTables.SourceFile);
    private readonly string _seen = db.Table(DbTables.SourceFileSeen);
    private readonly string _raw = db.Table(DbTables.RawRow);
    private readonly string _parsers = db.Table(DbTables.Parser);

    private const string BatchColumns = "BatchId AS Id, StartedAt, FinishedAt, UserName AS [User], Machine, AppVersion, Files, Imported, Skipped, Duplicates, Unrecognized, Errors, Status";

    private const string FileColumns =
        "FileId AS Id, Sha256, Location, FileName, SourceCode, Size, ModifiedAt, FileType, Sheet, Encoding, Delimiter, Columns AS ColumnsJson, Signature, " +
        "DataRows, BatchId, ImportedAt, ImportedBy, CanonicalStatus, CanonicalRows, ParserVersion";

    private const string SeenColumns = "Id, BatchId, Location, FileName, Size, ModifiedAt, Sha256, Decision, SourceCode, DataRows, Description";

    private static readonly (string Name, Type Type)[] RawColumns = [("FileId", typeof(long)), ("RowNumber", typeof(int)), ("Data", typeof(string))];

    public IReadOnlyList<SourceDefinitionRow> ActiveDefinitions()
    {
        using var connection = db.Open();
        return connection.Query<SqlDefinitionRow>($"SELECT {SqlDefinitionRow.Columns} FROM {_definitions} WHERE SupersededAt IS NULL AND Active = 1 ORDER BY Code")
            .Select(r => r.ToRow()).ToList();
    }

    public ParserRow? ActiveParser(string code)
    {
        using var connection = db.Open();
        return connection.QuerySingleOrDefault<SqlParserRow>(
            $"SELECT {SqlParserRow.Columns} FROM {_parsers} WHERE SupersededAt IS NULL AND Active = 1 AND Code = @code", new { code })?.ToRow();
    }

    public IReadOnlyList<SourceLocationRow> ActiveLocations()
    {
        using var connection = db.Open();
        return connection.Query<SqlLocationRow>($"SELECT {SqlLocationRow.Columns} FROM {_locations} WHERE SupersededAt IS NULL AND Active = 1 ORDER BY Name")
            .Select(r => r.ToRow()).ToList();
    }

    public long BeginBatch(DateTimeOffset at, string user, string machine, string appVersion)
    {
        using var connection = db.Open();
        return connection.ExecuteScalar<long>(
            $"INSERT INTO {_batches} (StartedAt, UserName, Machine, AppVersion, Status) OUTPUT INSERTED.BatchId VALUES (@at, @user, @machine, @appVersion, @status)",
            new { at, user, machine, appVersion, status = ImportBatchStatus.Running });
    }

    public void AbandonRunning(DateTimeOffset at)
    {
        using var connection = db.Open();
        connection.Execute($"UPDATE {_batches} SET Status = @abandoned, FinishedAt = @at WHERE Status = @running",
            new { at, abandoned = ImportBatchStatus.Abandoned, running = ImportBatchStatus.Running });
    }

    public SourceFileSeenRow? LastSettled(string location, string fileName)
    {
        using var connection = db.Open();
        return connection.QuerySingleOrDefault<SeenRow>(
            $"""
            SELECT TOP (1) {SeenColumns} FROM {_seen} s
            WHERE s.FileName = @fileName AND s.Location = @location AND s.Decision IN @settled
              AND EXISTS (SELECT 1 FROM {_files} f WHERE f.Sha256 = s.Sha256)
            ORDER BY s.Id DESC
            """,
            new { location, fileName, settled = FileDecisions.Settled.ToArray() })?.ToRow();
    }

    public SourceFileRow? FindByHash(string sha256)
    {
        using var connection = db.Open();
        return connection.QuerySingleOrDefault<FileRow>($"SELECT {FileColumns} FROM {_files} WHERE Sha256 = @sha256", new { sha256 })?.ToRow();
    }

    public long? RegisterFile(SourceFileRow file, IReadOnlyList<string?[]> rows)
    {
        try
        {
            return db.InTransaction<long?>((connection, transaction) =>
            {
                var id = connection.ExecuteScalar<long>(
                    $"""
                    INSERT INTO {_files} (Sha256, Location, FileName, SourceCode, Size, ModifiedAt, FileType, Sheet, Encoding, Delimiter, Columns, Signature,
                        DataRows, BatchId, ImportedAt, ImportedBy, CanonicalStatus, CanonicalRows, ParserVersion)
                    OUTPUT INSERTED.FileId
                    VALUES (@Sha256, @Location, @FileName, @SourceCode, @Size, @ModifiedAt, @FileType, @Sheet, @Encoding, @Delimiter, @Columns, @Signature,
                        @RowCount, @BatchId, @ImportedAt, @ImportedBy, @CanonicalStatus, @CanonicalRows, @ParserVersion)
                    """,
                    new
                    {
                        file.Sha256, file.Location, file.FileName, file.SourceCode, file.Size, file.ModifiedAt, file.FileType, file.Sheet, file.Encoding,
                        file.Delimiter, Columns = SqlJson.Write(file.Columns), file.Signature, file.RowCount, file.BatchId, file.ImportedAt, file.ImportedBy,
                        file.CanonicalStatus, file.CanonicalRows, file.ParserVersion,
                    },
                    transaction);
                SqlBulk.Insert(connection, transaction, _raw, RawColumns, rows.Select((r, i) => new object?[] { id, i + 1, SqlJson.Write(r) }));
                return id;
            });
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return null;   // ta sama treść zarejestrowana w międzyczasie (np. przez inną osobę)
        }
    }

    public void CompleteCanonical(long fileId, string status, CanonicalData? data, int? parserVersion) =>
        db.InTransaction((connection, transaction) =>
        {
            if (data is not null)
                SqlBulk.Insert(connection, transaction, db.Table(data.Parser.LogicalTable), SqlCanonical.BulkColumns(data.Fields),
                    data.Rows.Select(r => (object?[])[fileId, r.RowNumber, data.Parser.Version, .. r.Values]));
            return connection.Execute(
                $"UPDATE {_files} SET CanonicalStatus = @status, CanonicalRows = @count, ParserVersion = @parserVersion WHERE FileId = @fileId",
                new { status, count = data?.Rows.Count ?? 0, parserVersion, fileId }, transaction);
        });

    public void RecordSeen(SourceFileSeenRow seen)
    {
        using var connection = db.Open();
        connection.Execute(
            $"""
            INSERT INTO {_seen} (BatchId, Location, FileName, Size, ModifiedAt, Sha256, Decision, SourceCode, DataRows, Description)
            VALUES (@BatchId, @Location, @FileName, @Size, @ModifiedAt, @Sha256, @Decision, @SourceCode, @Rows, @Description)
            """,
            seen);
    }

    public void FinishBatch(long batchId, DateTimeOffset at, int files, int imported, int skipped, int duplicates, int unrecognized, int errors, string status)
    {
        using var connection = db.Open();
        connection.Execute(
            $"""
            UPDATE {_batches} SET FinishedAt = @at, Files = @files, Imported = @imported, Skipped = @skipped, Duplicates = @duplicates,
                Unrecognized = @unrecognized, Errors = @errors, Status = @status
            WHERE BatchId = @batchId
            """,
            new { batchId, at, files, imported, skipped, duplicates, unrecognized, errors, status });
    }

    public IReadOnlyList<ImportBatchRow> Batches(int count)
    {
        using var connection = db.Open();
        return connection.Query<BatchRow>($"SELECT TOP (@count) {BatchColumns} FROM {_batches} ORDER BY BatchId DESC", new { count }).Select(r => r.ToRow()).ToList();
    }

    public IReadOnlyList<SourceFileSeenRow> Seen(long batchId)
    {
        using var connection = db.Open();
        return connection.Query<SeenRow>($"SELECT {SeenColumns} FROM {_seen} WHERE BatchId = @batchId ORDER BY Id", new { batchId }).Select(r => r.ToRow()).ToList();
    }

    public SourceFileRow? File(long fileId)
    {
        using var connection = db.Open();
        return connection.QuerySingleOrDefault<FileRow>($"SELECT {FileColumns} FROM {_files} WHERE FileId = @fileId", new { fileId })?.ToRow();
    }

    public IReadOnlyList<RawRowRecord> RawRows(long fileId)
    {
        using var connection = db.Open();
        return connection.Query<(long FileId, int RowNumber, string Data)>($"SELECT FileId, RowNumber, Data FROM {_raw} WHERE FileId = @fileId ORDER BY RowNumber", new { fileId })
            .Select(r => new RawRowRecord(r.FileId, r.RowNumber, SqlJson.Values(r.Data)))
            .ToList();
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> CanonicalRows(ParserRow parser, long fileId) =>
        SqlCanonical.Rows(db, parser.LogicalTable, fileId);

    private sealed class BatchRow
    {
        public long Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string User { get; set; } = "";
        public string Machine { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public int Files { get; set; }
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Duplicates { get; set; }
        public int Unrecognized { get; set; }
        public int Errors { get; set; }
        public string Status { get; set; } = "";

        public ImportBatchRow ToRow() => new(Id, StartedAt, FinishedAt, User, Machine, AppVersion, Files, Imported, Skipped, Duplicates, Unrecognized, Errors, Status);
    }

    private sealed class FileRow
    {
        public long Id { get; set; }
        public string Sha256 { get; set; } = "";
        public string Location { get; set; } = "";
        public string FileName { get; set; } = "";
        public string SourceCode { get; set; } = "";
        public long Size { get; set; }
        public DateTimeOffset ModifiedAt { get; set; }
        public string FileType { get; set; } = "";
        public string? Sheet { get; set; }
        public string? Encoding { get; set; }
        public string? Delimiter { get; set; }
        public string ColumnsJson { get; set; } = "[]";
        public string Signature { get; set; } = "";
        public int DataRows { get; set; }
        public long BatchId { get; set; }
        public DateTimeOffset ImportedAt { get; set; }
        public string ImportedBy { get; set; } = "";
        public string CanonicalStatus { get; set; } = "";
        public int CanonicalRows { get; set; }
        public int? ParserVersion { get; set; }

        public SourceFileRow ToRow() =>
            new(Id, Sha256, Location, FileName, SourceCode, Size, ModifiedAt, FileType, Sheet, Encoding, Delimiter, SqlJson.Strings(ColumnsJson),
                Signature, DataRows, BatchId, ImportedAt, ImportedBy, CanonicalStatus, CanonicalRows, ParserVersion);
    }

    private sealed class SeenRow
    {
        public long Id { get; set; }
        public long BatchId { get; set; }
        public string Location { get; set; } = "";
        public string FileName { get; set; } = "";
        public long Size { get; set; }
        public DateTimeOffset ModifiedAt { get; set; }
        public string? Sha256 { get; set; }
        public string Decision { get; set; } = "";
        public string? SourceCode { get; set; }
        public int? DataRows { get; set; }
        public string Description { get; set; } = "";

        public SourceFileSeenRow ToRow() => new(Id, BatchId, Location, FileName, Size, ModifiedAt, Sha256, Decision, SourceCode, DataRows, Description);
    }
}
