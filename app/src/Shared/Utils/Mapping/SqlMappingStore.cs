using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Shared.Utils.Mapping;

/// <summary>
/// Dane mapowania w MS SQL: raport mapowań – słownik globalny DICT_MappingReport, elementy CES z CAN_Row (pola parsera
/// ACTUALS według slotów), korekty – DICT_MappingCorrection (zapis z historią).
/// </summary>
public sealed class SqlMappingStore(SqlDatabase db, IClock clock, ICurrentUser user) : IMappingStore
{
    private const string CorrectionColumns =
        "Id, RowId, Version, Kind, CesKey, TargetPspnr, TargetWbs, PreviousTarget, Justification, ValidFrom, ValidTo, " +
        "RecordedAt, RecordedBy, SupersededAt, SupersededBy";

    private string Corrections => db.Table(DbTables.MappingCorrection);

    private string Canonical => db.Table(DbTables.CanonicalRow);

    /// <summary>Kod parsera kosztów rzeczywistych – źródło elementów CES (pola WbsElement, ProjectDefinition, kwoty).</summary>
    public const string ActualsParser = "ACTUALS";

    public ReportInfo? Report()
    {
        var report = new SqlDictionaryStore(db, clock, user, GlobalDictionaries.Tables).Current(GlobalDictionaries.MappingReport);
        if (report.Count == 0)
            return null;
        var entries = report.Select((r, i) => MappingKeys.Entry(i + 1, r.Values)).ToList();
        var last = report.MaxBy(r => r.RecordedAt)!;
        return new ReportInfo(last.RecordedAt, last.RecordedBy, entries);
    }

    public IReadOnlyList<CesElement> CesElements()
    {
        using var connection = db.Open();
        if (SqlCanonical.CurrentParser(connection, db, ActualsParser) is not { } parser)
            return [];
        var wbs = SqlCanonical.Slot(parser, "WbsElement");
        var project = SqlCanonical.Slot(parser, "ProjectDefinition");
        var cost = string.Join(" OR ", new[] { "ValueTranCurr", "ValueObjCrcy", "ValueRepCur" }
            .Select(f => SqlCanonical.SlotOrNull(parser, f)).Where(s => s != "NULL").Select(s => $"ISNULL(a.{s}, 0) <> 0").DefaultIfEmpty("1 = 0"));
        return connection.Query<CesElement>(
                $"""
                SELECT a.{wbs} AS WbsElement, ISNULL(MAX(a.{project}), N'') AS Project,
                       CAST(MAX(CASE WHEN {cost} THEN 1 ELSE 0 END) AS BIT) AS HasCost,
                       MIN(f.BatchId) AS FirstBatchId, MAX(f.BatchId) AS LastBatchId
                FROM {Canonical} a JOIN {db.Table(DbTables.SourceFile)} f ON f.FileId = a.FileId
                WHERE a.ParserId = @parserId AND a.{wbs} IS NOT NULL AND a.{wbs} <> N''
                GROUP BY a.{wbs}
                """,
                new { parserId = parser.ParserId }, commandTimeout: 600)
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

}

/// <summary>Wiersz słownika „Raport mapowań CES ↔ P1S” – kolumny potrzebne do rozstrzygania.</summary>
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
