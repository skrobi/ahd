using Dapper;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Modules.Runs.Data;

/// <summary>Przebiegi w MS SQL (migracja 027); dane projektu w chwili przypięcia – procedura RUN_PinBalance.</summary>
public sealed class SqlRunStore(SqlDatabase db) : IRunStore
{
    private readonly string _runs = db.Table(DbTables.Run);
    private readonly string _pins = db.Table(DbTables.RunPin);
    private readonly string _balance = db.Table(DbTables.RunBalance);
    private readonly string _issues = db.Table(DbTables.RunIssue);

    private const int LongTimeout = 1800;

    private string PinColumns =>
        "p.PinId, p.RunId, r.Code, r.Project, p.PinNo, p.StartedAt, p.StartedBy, p.FinishedAt, p.Status, p.ImportBatchId, p.Rows, p.GrossCost, p.Balance, " +
        "p.Errors, p.Warnings, p.Summary, p.AmountField";

    private string PinFrom => $"{_pins} p JOIN {_runs} r ON r.RunId = p.RunId";

    private const string BalanceColumns = "WbsElement, CostElement, FiscalYear, Period, IsSettlement, Currency, Rows, Amount, LastCreatedOn";

    public CalendarWeek? Week(DateTime day)
    {
        using var connection = db.Open();
        return connection.QueryFirstOrDefault<CalendarWeek>(
            $"SELECT TOP (1) Year, Week, Period, DateFrom, DateTo, IsClosing FROM {db.Table("dict.Calendar")} " +
            "WHERE Project IS NULL AND SupersededAt IS NULL AND @day BETWEEN DateFrom AND DateTo",
            new { day = day.Date });
    }

    public RunPinRow StartPin(string project, CalendarWeek week, DateTimeOffset at, string user, string machine) =>
        db.InTransaction((connection, transaction) =>
        {
            var code = $"R-{project}-{week.Period}-T{week.Week:00}";
            var runId = connection.ExecuteScalar<long?>($"SELECT RunId FROM {_runs} WITH (UPDLOCK, HOLDLOCK) WHERE Project = @project AND Year = @Year AND Week = @Week",
                new { project, week.Year, week.Week }, transaction)
                ?? connection.ExecuteScalar<long>(
                    $"""
                    INSERT INTO {_runs} (Code, Project, Year, Week, Period, IsClosing, CreatedAt, CreatedBy) OUTPUT INSERTED.RunId
                    VALUES (@code, @project, @Year, @Week, @Period, @IsClosing, @at, @user)
                    """,
                    new { code, project, week.Year, week.Week, week.Period, week.IsClosing, at, user }, transaction);
            var pinId = connection.ExecuteScalar<long>(
                $"""
                INSERT INTO {_pins} (RunId, PinNo, StartedAt, StartedBy, Machine, Status) OUTPUT INSERTED.PinId
                VALUES (@runId, ISNULL((SELECT MAX(PinNo) FROM {_pins} WHERE RunId = @runId), 0) + 1, @at, @user, @machine, @status)
                """,
                new { runId, at, user, machine, status = RunStatuses.Running }, transaction);
            return connection.QuerySingle<RunPinRow>($"SELECT {PinColumns} FROM {PinFrom} WHERE p.PinId = @pinId", new { pinId }, transaction);
        });

    public IReadOnlyList<RunBalanceRow> PinBalance(string project, long pinId, string amountField)
    {
        // Jedna transakcja: procedura usuwa saldo przypięcia, zmienia pole kwoty i zapisuje nowe saldo – błąd w trakcie nie zostawia
        // przypięcia z częściowym saldem.
        db.InTransaction((connection, transaction) =>
            connection.Execute(db.Table("run.PinBalance"), new { Project = project, PinId = pinId, Value = amountField }, transaction,
                commandType: System.Data.CommandType.StoredProcedure, commandTimeout: LongTimeout));
        return Balance(pinId);
    }

    public IReadOnlyList<RunBalanceRow> Balance(long pinId)
    {
        using var connection = db.Open();
        return connection.Query<RunBalanceRow>($"SELECT {BalanceColumns} FROM {_balance} WHERE PinId = @pinId", new { pinId }, commandTimeout: LongTimeout).ToList();
    }

    public RunPinRow? Previous(string project, long runId)
    {
        using var connection = db.Open();
        return connection.QueryFirstOrDefault<RunPinRow>(
            $"""
            SELECT TOP (1) {PinColumns} FROM {PinFrom}
            WHERE r.Project = @project AND r.RunId <> @runId AND p.Status IN @finished
              AND (r.Year < (SELECT Year FROM {_runs} WHERE RunId = @runId)
                   OR (r.Year = (SELECT Year FROM {_runs} WHERE RunId = @runId) AND r.Week < (SELECT Week FROM {_runs} WHERE RunId = @runId)))
            ORDER BY r.Year DESC, r.Week DESC, p.PinNo DESC
            """,
            new { project, runId, finished = new[] { RunStatuses.Ok, RunStatuses.Warnings, RunStatuses.Errors } });
    }

    public IReadOnlySet<string> CostElements(string project)
    {
        using var connection = db.Open();
        return connection.Query<string>(
                $"SELECT DISTINCT CostElement FROM {db.Table("dict.CostCategory")} WHERE SupersededAt IS NULL AND (Project IS NULL OR Project = @project)",
                new { project })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void Finish(long pinId, DateTimeOffset at, string status, long? importBatchId, long rows, decimal grossCost, decimal balance,
        IReadOnlyList<(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount)> issues, string summary) =>
        db.InTransaction((connection, transaction) =>
        {
            connection.Execute($"DELETE FROM {_issues} WHERE PinId = @pinId", new { pinId }, transaction);
            connection.Execute(
                $"INSERT INTO {_issues} (PinId, CheckName, Level, Element, Message, Amount) VALUES (@pinId, @Check, @Level, @Element, @Message, @Amount)",
                issues.Select(i => new { pinId, i.Check, Level = i.Level.ToString(), i.Element, i.Message, i.Amount }), transaction);
            connection.Execute(
                $"""
                UPDATE {_pins} SET FinishedAt = @at, Status = @status, ImportBatchId = @importBatchId, Rows = @rows, GrossCost = @grossCost,
                    Balance = @balance, Errors = @errors, Warnings = @warnings, Summary = @summary
                WHERE PinId = @pinId
                """,
                new
                {
                    pinId, at, status, importBatchId, rows, grossCost, balance, summary,
                    errors = issues.Count(i => i.Level == CheckLevel.Error), warnings = issues.Count(i => i.Level == CheckLevel.Warning),
                }, transaction);
            return true;
        });

    public void AbandonRunning(Func<long, bool> isAlive, DateTimeOffset at)
    {
        using var connection = db.Open();
        var dead = connection.Query<long>($"SELECT PinId FROM {_pins} WHERE Status = @running", new { running = RunStatuses.Running })
            .Where(id => !isAlive(id)).ToList();
        if (dead.Count > 0)
            connection.Execute($"UPDATE {_pins} SET Status = @abandoned, FinishedAt = @at WHERE PinId IN @dead AND Status = @running",
                new { dead, at, abandoned = RunStatuses.Abandoned, running = RunStatuses.Running });
    }

    public IReadOnlyDictionary<string, RunPinRow> Latest()
    {
        using var connection = db.Open();
        return connection.Query<RunPinRow>(
                $"""
                SELECT {PinColumns} FROM {PinFrom}
                WHERE p.PinId = (SELECT TOP (1) p2.PinId FROM {_pins} p2 JOIN {_runs} r2 ON r2.RunId = p2.RunId
                                 WHERE r2.Project = r.Project ORDER BY p2.StartedAt DESC, p2.PinId DESC)
                """)
            .ToDictionary(p => p.Project, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<RunPinRow> Pins(string project, int count)
    {
        using var connection = db.Open();
        return connection.Query<RunPinRow>($"SELECT TOP (@count) {PinColumns} FROM {PinFrom} WHERE r.Project = @project ORDER BY p.StartedAt DESC, p.PinId DESC",
            new { project, count }).ToList();
    }

    public IReadOnlyList<RunIssueRow> Issues(long pinId)
    {
        using var connection = db.Open();
        return connection.Query<(long Id, long PinId, string CheckName, string Level, string? Element, string Message, decimal? Amount)>(
                $"SELECT Id, PinId, CheckName, Level, Element, Message, Amount FROM {_issues} WHERE PinId = @pinId ORDER BY Id", new { pinId })
            .Select(i => new RunIssueRow(i.Id, i.PinId, i.CheckName, Enum.Parse<CheckLevel>(i.Level), i.Element, i.Message, i.Amount))
            .ToList();
    }

    public IReadOnlySet<string> ProjectSources(string project)
    {
        using var connection = db.Open();
        return connection.Query<string>($"SELECT SourceCode FROM {db.Table("dict.ProjectSource")} WHERE Project = @project AND SupersededAt IS NULL", new { project })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string ActualsAmount(string project)
    {
        using var connection = db.Open();
        return ProjectSettings.ActualsAmountOf(connection.QueryFirstOrDefault<string?>(
            $"SELECT Value FROM {db.Table("dict.ProjectSetting")} WHERE Project = @project AND Name = @name AND SupersededAt IS NULL",
            new { project, name = ProjectSettings.ActualsAmount }));
    }
}
