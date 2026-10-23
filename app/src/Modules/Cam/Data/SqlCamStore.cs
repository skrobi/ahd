using Dapper;
using PzlEv.Modules.Cam.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Modules.Cam.Data;

/// <summary>Estymacje CAM na MS SQL (migracja 056) – Dapper; cykl z dict.Calendar (wiersze globalne, bieżące wersje).</summary>
public sealed class SqlCamStore(SqlDatabase db) : ICamStore
{
    private string EstimateTable => db.Table("cam.Estimate");
    private string LineTable => db.Table("cam.EstimateLine");
    private string EventTable => db.Table("cam.EstimateEvent");
    private string NotificationTable => db.Table("cam.Notification");

    private const string Columns =
        "EstimateId, Project, Wp, Cycle, Cam, Status, ForecastStart, ForecastFinish, Currency, Justification, ReviewNote, UpdatedAt, UpdatedBy, " +
        "SubmittedAt, SubmittedBy, ReviewedAt, ReviewedBy";

    public CamCycle? Cycle(DateOnly day, bool weekly = false)
    {
        using var connection = db.Open();
        var week = connection.QueryFirstOrDefault<(string Period, int Year, int Week, DateTime DateTo)?>(
            $"SELECT TOP (1) Period, Year, Week, DateTo FROM {db.Table("dict.Calendar")} WHERE Project IS NULL AND SupersededAt IS NULL AND @day BETWEEN DateFrom AND DateTo ORDER BY DateFrom",
            new { day = day.ToDateTime(TimeOnly.MinValue) });
        if (week is not { } w)
            return null;
        return weekly ? new CamCycle(CamCycles.Week(w.Year, w.Week), DateOnly.FromDateTime(w.DateTo)) : new CamCycle(w.Period, Deadline(w.Period));
    }

    public DateOnly? Deadline(string code)
    {
        using var connection = db.Open();
        var calendar = db.Table("dict.Calendar");
        var deadline = CamCycles.WeekOf(code) is { } week
            ? connection.QueryFirstOrDefault<DateTime?>(
                $"SELECT MAX(DateTo) FROM {calendar} WHERE Project IS NULL AND SupersededAt IS NULL AND Year = @year AND Week = @week", new { year = week.Year, week = week.Week })
            : connection.QueryFirstOrDefault<DateTime?>(
                $"SELECT MAX(DateTo) FROM {calendar} WHERE Project IS NULL AND SupersededAt IS NULL AND Period = @code AND IsClosing = 1", new { code });
        return deadline is { } d ? DateOnly.FromDateTime(d) : null;
    }

    public IReadOnlyList<CamEstimate> Estimates(string cycle, string? cam = null, string? project = null)
    {
        using var connection = db.Open();
        var headers = connection.Query<Row>(
            $"SELECT {Columns} FROM {EstimateTable} WHERE Cycle = @cycle AND (@cam IS NULL OR Cam = @cam) AND (@project IS NULL OR Project = @project)",
            new { cycle, cam, project }).ToList();
        return WithLines(connection, headers);
    }

    public CamEstimate? Estimate(string project, string wp, string cycle)
    {
        using var connection = db.Open();
        var header = connection.QuerySingleOrDefault<Row>($"SELECT {Columns} FROM {EstimateTable} WHERE Project = @project AND Wp = @wp AND Cycle = @cycle",
            new { project, wp, cycle });
        return header is null ? null : WithLines(connection, [header]).Single();
    }

    public CamEstimate? LastApproved(string project, string wp, string beforeCycle)
    {
        using var connection = db.Open();
        var header = connection.QueryFirstOrDefault<Row>(
            $"SELECT TOP (1) {Columns} FROM {EstimateTable} WHERE Project = @project AND Wp = @wp AND Cycle <> @beforeCycle AND Status = @status ORDER BY ReviewedAt DESC",
            new { project, wp, beforeCycle, status = CamStatuses.Approved });
        return header is null ? null : WithLines(connection, [header]).Single();
    }

    public IReadOnlyList<CamNotification> Notifications(string project, string cycle)
    {
        using var connection = db.Open();
        return connection.Query<CamNotification>(
            $"SELECT Cam, Cam AS Name, Email, SentAt, SentBy, Wps, Result, Message FROM {NotificationTable} WHERE Project = @project AND Cycle = @cycle ORDER BY SentAt DESC, NotificationId DESC",
            new { project, cycle }).ToList();
    }

    public void AddNotification(string project, string cycle, CamNotification notification, long? pinId)
    {
        using var connection = db.Open();
        connection.Execute(
            $"INSERT INTO {NotificationTable} (Project, Cycle, Cam, Email, SentAt, SentBy, PinId, Wps, Result, Message) VALUES (@project, @cycle, @Cam, @Email, @SentAt, @SentBy, @pinId, @Wps, @Result, @Message)",
            new { project, cycle, notification.Cam, notification.Email, notification.SentAt, notification.SentBy, pinId, Wps = Cut(notification.Wps, 2000), notification.Result,
                Message = notification.Message is null ? null : Cut(notification.Message, 1000) });
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    public IReadOnlyList<CamEstimateEvent> Events(long estimateId)
    {
        using var connection = db.Open();
        return connection.Query<CamEstimateEvent>(
            $"SELECT At, [By], Status, Summary, Comment FROM {EventTable} WHERE EstimateId = @estimateId ORDER BY At DESC, EventId DESC", new { estimateId }).ToList();
    }

    public CamEstimate Save(CamEstimate estimate, CamEstimateEvent change) =>
        db.InTransaction((connection, transaction) =>
        {
            var args = new
            {
                estimate.Project, estimate.Wp, estimate.Cycle, estimate.Cam, estimate.Status,
                ForecastStart = estimate.ForecastStart?.ToDateTime(TimeOnly.MinValue), ForecastFinish = estimate.ForecastFinish?.ToDateTime(TimeOnly.MinValue),
                estimate.Currency, estimate.Justification, estimate.ReviewNote, UpdatedAt = change.At, UpdatedBy = change.By,
                estimate.SubmittedAt, estimate.SubmittedBy, estimate.ReviewedAt, estimate.ReviewedBy, Expected = estimate.UpdatedAt,
            };
            long id;
            if (estimate.EstimateId is null)
            {
                if (connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {EstimateTable} WITH (UPDLOCK, HOLDLOCK) WHERE Project = @Project AND Wp = @Wp AND Cycle = @Cycle",
                        args, transaction) > 0)
                    throw new CamConflictException($"Estymację WP {estimate.Wp} w cyklu {estimate.Cycle} zapisał w międzyczasie ktoś inny – odśwież.");
                id = connection.ExecuteScalar<long>(
                    $"""
                    INSERT INTO {EstimateTable} (Project, Wp, Cycle, Cam, Status, ForecastStart, ForecastFinish, Currency, Justification, ReviewNote, UpdatedAt, UpdatedBy,
                        SubmittedAt, SubmittedBy, ReviewedAt, ReviewedBy)
                    OUTPUT inserted.EstimateId
                    VALUES (@Project, @Wp, @Cycle, @Cam, @Status, @ForecastStart, @ForecastFinish, @Currency, @Justification, @ReviewNote, @UpdatedAt, @UpdatedBy,
                        @SubmittedAt, @SubmittedBy, @ReviewedAt, @ReviewedBy)
                    """, args, transaction);
            }
            else
            {
                id = estimate.EstimateId.Value;
                var updated = connection.Execute(
                    $"""
                    UPDATE {EstimateTable} SET Cam = @Cam, Status = @Status, ForecastStart = @ForecastStart, ForecastFinish = @ForecastFinish, Currency = @Currency,
                        Justification = @Justification, ReviewNote = @ReviewNote, UpdatedAt = @UpdatedAt, UpdatedBy = @UpdatedBy, SubmittedAt = @SubmittedAt,
                        SubmittedBy = @SubmittedBy, ReviewedAt = @ReviewedAt, ReviewedBy = @ReviewedBy
                    WHERE EstimateId = @id AND UpdatedAt = @Expected
                    """, new { args.Cam, args.Status, args.ForecastStart, args.ForecastFinish, args.Currency, args.Justification, args.ReviewNote, args.UpdatedAt,
                        args.UpdatedBy, args.SubmittedAt, args.SubmittedBy, args.ReviewedAt, args.ReviewedBy, id, args.Expected }, transaction);
                if (updated == 0)
                    throw new CamConflictException($"Estymację WP {estimate.Wp} zmienił w międzyczasie ktoś inny – odśwież i wprowadź zmiany ponownie.");
                connection.Execute($"DELETE FROM {LineTable} WHERE EstimateId = @id", new { id }, transaction);
            }
            connection.Execute($"INSERT INTO {LineTable} (EstimateId, Kind, Year, Proposal, Value) VALUES (@id, @Kind, @Year, @Proposal, @Value)",
                estimate.Lines.Select(l => new { id, Kind = l.Kind.ToString(), l.Year, l.Proposal, l.Value }), transaction);
            connection.Execute($"INSERT INTO {EventTable} (EstimateId, At, [By], Status, Summary, Comment) VALUES (@id, @At, @By, @Status, @Summary, @Comment)",
                new { id, change.At, change.By, change.Status, change.Summary, change.Comment }, transaction);
            return estimate with { EstimateId = id, UpdatedAt = change.At, UpdatedBy = change.By };
        });

    private List<CamEstimate> WithLines(Microsoft.Data.SqlClient.SqlConnection connection, IReadOnlyList<Row> headers)
    {
        if (headers.Count == 0)
            return [];
        var lines = connection.Query<(long EstimateId, string Kind, int Year, decimal? Proposal, decimal Value)>(
                $"SELECT EstimateId, Kind, Year, Proposal, Value FROM {LineTable} WHERE EstimateId IN @ids", new { ids = headers.Select(h => h.EstimateId).ToArray() })
            .ToLookup(l => l.EstimateId);
        return headers.Select(h => h.ToEstimate(lines[h.EstimateId].Select(l => new CamEstimateLine(l.Kind[0], l.Year, l.Proposal, l.Value))
            .OrderBy(l => l.Kind).ThenBy(l => l.Year).ToList())).ToList();
    }

    private sealed class Row
    {
        public long EstimateId { get; set; }
        public string Project { get; set; } = "";
        public string Wp { get; set; } = "";
        public string Cycle { get; set; } = "";
        public string Cam { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? ForecastStart { get; set; }
        public DateTime? ForecastFinish { get; set; }
        public string? Currency { get; set; }
        public string? Justification { get; set; }
        public string? ReviewNote { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = "";
        public DateTimeOffset? SubmittedAt { get; set; }
        public string? SubmittedBy { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }

        public CamEstimate ToEstimate(IReadOnlyList<CamEstimateLine> lines) =>
            new(EstimateId, Project, Wp, Cycle, Cam, Status, ForecastStart is { } s ? DateOnly.FromDateTime(s) : null,
                ForecastFinish is { } f ? DateOnly.FromDateTime(f) : null, Currency, Justification, ReviewNote, lines, UpdatedAt, UpdatedBy,
                SubmittedAt, SubmittedBy, ReviewedAt, ReviewedBy);
    }
}
