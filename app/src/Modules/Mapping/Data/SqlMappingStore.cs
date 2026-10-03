using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.Mapping.Models;
using PzlEv.Modules.Mapping.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Modules.Mapping.Data;

/// <summary>Dane mapowania w MS SQL: CAN_MappingReport i CAN_Actuals (odczyt), DICT_MappingCorrection (zapis z historią).</summary>
public sealed class SqlMappingStore(SqlDatabase db, IClock clock, ICurrentUser user) : IMappingStore
{
    private const string CorrectionColumns =
        "Id, RowId, Version, Kind, CesKey, TargetPspnr, TargetWbs, PreviousTarget, Justification, ValidFrom, ValidTo, " +
        "RecordedAt, RecordedBy, SupersededAt, SupersededBy";

    private string Corrections => db.Table(DbTables.MappingCorrection);

    public ReportInfo? LatestReport()
    {
        using var connection = db.Open();
        var report = db.Table(DbTables.MappingReport);
        var file = connection.QueryFirstOrDefault<FileInfoRow>(
            $"""
            SELECT TOP 1 f.FileId, f.FileName, f.ImportedAt FROM {db.Table(DbTables.SourceFile)} f
            WHERE EXISTS (SELECT 1 FROM {report} r WHERE r.FileId = f.FileId)
            ORDER BY f.ImportedAt DESC, f.FileId DESC
            """);
        if (file is null)
            return null;
        var entries = connection.Query<ReportRow>(
                $"""
                SELECT RowNumber, Src, Pspnr, PspnrSap, PspnrCes, ProjectSap, ProjectCes, Wbs, WbsSap, WbsCes
                FROM {report} WHERE FileId = @fileId ORDER BY RowNumber
                """,
                new { file.FileId })
            .Select(MappingKeys.Entry)
            .ToList();
        return new ReportInfo(file.FileId, file.FileName, file.ImportedAt, entries);
    }

    public IReadOnlyList<CesElement> CesElements()
    {
        using var connection = db.Open();
        return connection.Query<CesElement>(
                $"""
                SELECT a.WbsElement, ISNULL(MAX(a.ProjectDefinition), N'') AS Project,
                       CAST(MAX(CASE WHEN ISNULL(a.ValueTranCurr, 0) <> 0 OR ISNULL(a.ValueObjCrcy, 0) <> 0 OR ISNULL(a.ValueRepCur, 0) <> 0
                                     THEN 1 ELSE 0 END) AS BIT) AS HasCost,
                       MIN(f.BatchId) AS FirstBatchId, MAX(f.BatchId) AS LastBatchId
                FROM {db.Table(DbTables.Actuals)} a JOIN {db.Table(DbTables.SourceFile)} f ON f.FileId = a.FileId
                WHERE a.WbsElement IS NOT NULL AND a.WbsElement <> N''
                GROUP BY a.WbsElement
                """,
                commandTimeout: 300)
            .ToList();
    }

    public IReadOnlyList<CorrectionRow> ActiveCorrections()
    {
        using var connection = db.Open();
        return connection.Query<CorrectionRow>(
                $"SELECT {CorrectionColumns} FROM {Corrections} WHERE SupersededAt IS NULL AND ValidTo IS NULL ORDER BY Kind, CesKey")
            .ToList();
    }

    public IReadOnlyList<CorrectionRow> History(string kind, string cesKey)
    {
        using var connection = db.Open();
        return connection.Query<CorrectionRow>(
                $"SELECT {CorrectionColumns} FROM {Corrections} WHERE Kind = @kind AND CesKey = @cesKey ORDER BY RecordedAt, Id",
                new { kind, cesKey })
            .ToList();
    }

    public string? SaveCorrection(CorrectionInput input, string targetWbs, string? previousTarget)
    {
        try
        {
            return db.InTransaction((connection, transaction) =>
            {
                var current = connection.QueryFirstOrDefault<CorrectionRow>(
                    $"SELECT {CorrectionColumns} FROM {Corrections} WITH (UPDLOCK, HOLDLOCK) WHERE Kind = @Kind AND CesKey = @CesKey AND SupersededAt IS NULL AND ValidTo IS NULL",
                    new { input.Kind, input.CesKey }, transaction);
                if (Conflict(current, input.RowId, input.Version, input.CesKey) is { } conflict)
                    return conflict;

                var now = clock.Now;
                if (current is not null)
                    Supersede(connection, transaction, current.RowId, now);
                connection.Execute(
                    $"""
                    INSERT INTO {Corrections} (RowId, Version, Kind, CesKey, TargetPspnr, TargetWbs, PreviousTarget, Justification, ValidFrom, RecordedAt, RecordedBy)
                    VALUES (@rowId, @version, @Kind, @CesKey, @TargetPspnr, @targetWbs, @previousTarget, @Justification, @validFrom, @now, @user)
                    """,
                    new
                    {
                        rowId = current?.RowId ?? db.NextLogicalId(connection, transaction),
                        version = (current?.Version ?? 0) + 1,
                        input.Kind, input.CesKey, input.TargetPspnr, targetWbs, previousTarget, input.Justification,
                        validFrom = now.Date, now, user = user.Account,
                    },
                    transaction);
                return (string?)null;
            });
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return $"Korekta {input.CesKey} została w międzyczasie zapisana przez inną osobę – odśwież dane.";
        }
    }

    public string? CloseCorrection(long rowId, int expectedVersion) =>
        db.InTransaction((connection, transaction) =>
        {
            var current = connection.QueryFirstOrDefault<CorrectionRow>(
                $"SELECT {CorrectionColumns} FROM {Corrections} WITH (UPDLOCK, HOLDLOCK) WHERE RowId = @rowId AND SupersededAt IS NULL",
                new { rowId }, transaction);
            if (current is not { IsActive: true })
                return "Korekta została w międzyczasie usunięta – odśwież dane.";
            if (Conflict(current, rowId, expectedVersion, current.CesKey) is { } conflict)
                return conflict;

            var now = clock.Now;
            Supersede(connection, transaction, rowId, now);
            connection.Execute(
                $"""
                INSERT INTO {Corrections} (RowId, Version, Kind, CesKey, TargetPspnr, TargetWbs, PreviousTarget, Justification, ValidFrom, ValidTo, RecordedAt, RecordedBy)
                SELECT RowId, Version + 1, Kind, CesKey, TargetPspnr, TargetWbs, PreviousTarget, Justification, ValidFrom, @validTo, @now, @user
                FROM {Corrections} WHERE Id = @id
                """,
                new { id = current.Id, validTo = now.Date, now, user = user.Account },
                transaction);
            return (string?)null;
        });

    /// <summary>Bieżąca wersja różna od tej, którą widział użytkownik – zapis innej osoby w międzyczasie.</summary>
    private static string? Conflict(CorrectionRow? current, long? rowId, int? version, string cesKey)
    {
        if (current is null)
            return rowId is null ? null : $"Korekta {cesKey} została w międzyczasie usunięta – odśwież dane.";
        if (current.RowId != rowId || current.Version != version)
            return $"Korektę {cesKey} zmienił {current.RecordedBy} ({current.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm}) – odśwież dane.";
        return null;
    }

    private void Supersede(SqlConnection connection, SqlTransaction transaction, long rowId, DateTimeOffset now) =>
        connection.Execute($"UPDATE {Corrections} SET SupersededAt = @now, SupersededBy = @user WHERE RowId = @rowId AND SupersededAt IS NULL",
            new { now, user = user.Account, rowId }, transaction);

    private sealed class FileInfoRow
    {
        public long FileId { get; set; }
        public string FileName { get; set; } = "";
        public DateTimeOffset ImportedAt { get; set; }
    }
}

/// <summary>Wiersz CAN_MappingReport (pola parsera MAPOWANIA).</summary>
public sealed class ReportRow
{
    public int RowNumber { get; set; }
    public string? Src { get; set; }
    public string? Pspnr { get; set; }
    public string? PspnrSap { get; set; }
    public string? PspnrCes { get; set; }
    public string? ProjectSap { get; set; }
    public string? ProjectCes { get; set; }
    public string? Wbs { get; set; }
    public string? WbsSap { get; set; }
    public string? WbsCes { get; set; }
}
