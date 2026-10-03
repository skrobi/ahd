using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Import.Data;

/// <summary>
/// Import w bazie: META_ImportBatch, META_SourceFile, META_SourceFileSeen, treść pliku (META_SourceFileContent)
/// i dane kanoniczne (CAN_Row – sloty pól parsera). Wersja pliku zapisywana w jednej transakcji: dane kanoniczne
/// strumieniowo (SqlBulkCopy) z kontrolą przepływu w bazie, treść pliku strumieniowo z dysku.
/// </summary>
public sealed class SqlImportStore(SqlDatabase db) : IImportStore
{
    private readonly string _definitions = db.Table(DbTables.SourceDefinition);
    private readonly string _locations = db.Table(DbTables.SourceLocation);
    private readonly string _batches = db.Table(DbTables.ImportBatch);
    private readonly string _files = db.Table(DbTables.SourceFile);
    private readonly string _seen = db.Table(DbTables.SourceFileSeen);
    private readonly string _content = db.Table(DbTables.SourceFileContent);
    private readonly string _canonical = db.Table(DbTables.CanonicalRow);
    private readonly string _parsers = db.Table(DbTables.Parser);

    private const string BatchColumns = "BatchId AS Id, StartedAt, FinishedAt, UserName AS [User], Machine, AppVersion, Files, Imported, Skipped, Duplicates, Unrecognized, Errors, Status";

    private const string FileColumns =
        "FileId AS Id, Sha256, Location, FileName, SourceCode, Size, ModifiedAt, FileType, Sheet, Encoding, Delimiter, Columns AS ColumnsJson, Signature, " +
        "DataRows, BatchId, ImportedAt, ImportedBy, CanonicalStatus, CanonicalRows, ParserVersion";

    private const string SeenColumns = "Id, BatchId, Location, FileName, Size, ModifiedAt, Sha256, Decision, SourceCode, DataRows, Description";

    /// <summary>Czas na zapis i kontrolę dużego pliku (miliony wierszy) w jednej transakcji.</summary>
    private const int LongTimeout = 1800;

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

    public StoredFile? StoreFile(SourceFileRow file, string contentPath, CanonicalData? canonical, Action<string>? stage = null)
    {
        try
        {
            return db.InTransaction<StoredFile?>((connection, transaction) =>
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
                        CanonicalStatus = canonical is null ? file.CanonicalStatus : "utworzone",
                        CanonicalRows = canonical is null ? file.CanonicalRows : 0,
                        ParserVersion = canonical?.Parser.Version ?? file.ParserVersion,
                    },
                    transaction);

                var stored = new StoredFile(id, 0, new Dictionary<string, decimal>());
                if (canonical is not null)
                {
                    var parserId = canonical.Parser.ParserId;
                    var version = canonical.Parser.Version;
                    SqlBulk.Insert(connection, transaction, _canonical, SqlCanonical.BulkColumns(canonical.Fields),
                        canonical.Rows.Select(r => (object?[])[id, r.RowNumber, parserId, version, .. r.Values]));
                    var totals = canonical.Totals();   // błędy treści pliku – wyjątek, transakcja wycofana
                    stage?.Invoke("kontrola w bazie (liczba wierszy i sumy)");
                    stored = Verify(connection, transaction, id, canonical, totals);
                    connection.Execute($"UPDATE {_files} SET DataRows = @rows, CanonicalRows = @rows WHERE FileId = @id",
                        new { id, rows = totals.Rows }, transaction);
                }

                stage?.Invoke("zapis treści pliku");
                InsertContent(connection, transaction, id, contentPath, file.FileName);
                stage?.Invoke("zatwierdzanie zapisu");
                return stored;
            });
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return null;   // ta sama treść zapisana w międzyczasie (np. przez inną osobę)
        }
    }

    /// <summary>
    /// Treść pliku strumieniowo z dysku (bez kopii w pamięci): Excel (.xlsx, .xlsm – już skompresowany ZIP) bez zmian
    /// (format raw), tekst (CSV, TXT) skompresowany GZip w locie (format gzip).
    /// </summary>
    private void InsertContent(SqlConnection connection, SqlTransaction transaction, long id, string path, string fileName)
    {
        var raw = TabularFileReader.ExcelExtensions.Contains(System.IO.Path.GetExtension(fileName));
        var file = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read, 1 << 16,
            System.IO.FileOptions.SequentialScan);
        var size = file.Length;
        using var content = raw ? file : FileCompression.GZipReading(file);
        using var command = new SqlCommand($"INSERT INTO {_content} (FileId, Format, Size, Content) VALUES (@id, @format, @size, @content)",
            connection, transaction) { CommandTimeout = LongTimeout };
        command.Parameters.Add("@id", System.Data.SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@format", System.Data.SqlDbType.VarChar, 20).Value = raw ? ContentFormats.Raw : ContentFormats.GZip;
        command.Parameters.Add("@size", System.Data.SqlDbType.BigInt).Value = size;
        command.Parameters.Add("@content", System.Data.SqlDbType.VarBinary, -1).Value = content;
        command.ExecuteNonQuery();
    }

    /// <summary>Kontrola przepływu w bazie: liczba wierszy i sumy pól liczbowych pliku w CAN_Row = odczytane z pliku.</summary>
    private StoredFile Verify(SqlConnection connection, SqlTransaction transaction, long fileId, CanonicalData canonical, CanonicalTotals totals)
    {
        var decimals = canonical.Fields.Where(f => f.Type == FieldTypes.Decimal).ToList();
        var sums = string.Concat(decimals.Select((f, i) => $", ISNULL(SUM({SqlCanonical.Slot(f)}), 0) AS S{i}"));
        var row = (IDictionary<string, object>)connection.QuerySingle(
            $"SELECT COUNT_BIG(*) AS Rows{sums} FROM {_canonical} WHERE FileId = @fileId AND ParserId = @parserId",
            new { fileId, parserId = canonical.Parser.ParserId }, transaction, LongTimeout);
        var count = Convert.ToInt32(row["Rows"], CultureInfo.InvariantCulture);
        var stored = decimals.Select((f, i) => (f.Field, Sum: Convert.ToDecimal(row[$"S{i}"], CultureInfo.InvariantCulture)))
            .ToDictionary(s => s.Field, s => s.Sum);
        var differences = decimals
            .Where(f => stored[f.Field] != totals.Sums.GetValueOrDefault(f.Field))
            .Select(f => $", suma {f.Column} {totals.Sums.GetValueOrDefault(f.Field)}/{stored[f.Field]}")
            .ToList();
        if (count != totals.Rows || differences.Count > 0)
            throw new CanonicalFlowException($"dane kanoniczne w bazie niezgodne z plikiem (wiersze {totals.Rows}/{count}{string.Concat(differences)})");
        return new StoredFile(fileId, count, stored);
    }

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
        var stored = connection.QuerySingleOrDefault<(string Format, byte[] Content, string FileName)>(
            $"SELECT c.Format, c.Content, f.FileName FROM {_content} c JOIN {_files} f ON f.FileId = c.FileId WHERE c.FileId = @fileId",
            new { fileId }, commandTimeout: LongTimeout);
        if (stored.Content is null)
            return [];
        var content = stored.Format == ContentFormats.Raw ? stored.Content : FileCompression.GUnzip(stored.Content);
        if (stored.Format == ContentFormats.LegacyRawRows)   // dawne wiersze surowe (migracja 007): JSON na wiersz
            return System.Text.Encoding.Unicode.GetString(content).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select((line, i) => new RawRowRecord(fileId, i + 1, SqlJson.Values(line)))
                .ToList();
        return TabularFileReader.Open(content, stored.FileName).Rows()
            .Select((cells, i) => new RawRowRecord(fileId, i + 1, cells))
            .ToList();
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> CanonicalRows(ParserRow parser, long fileId) =>
        SqlCanonical.Rows(db, parser, fileId);

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
