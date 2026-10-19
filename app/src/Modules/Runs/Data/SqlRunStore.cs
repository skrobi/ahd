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
    private readonly string _steps = db.Table("meta.RunStep");

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
            WHERE r.Project = @project AND r.RunId <> @runId AND p.Rows IS NOT NULL AND p.Status NOT IN @excluded
              AND (r.Year < (SELECT Year FROM {_runs} WHERE RunId = @runId)
                   OR (r.Year = (SELECT Year FROM {_runs} WHERE RunId = @runId) AND r.Week < (SELECT Week FROM {_runs} WHERE RunId = @runId)))
            ORDER BY r.Year DESC, r.Week DESC, p.PinNo DESC
            """,
            new { project, runId, excluded = new[] { RunStatuses.Running, RunStatuses.Failed } });
    }

    public IReadOnlySet<string> CostElements(string project)
    {
        using var connection = db.Open();
        return connection.Query<string>(
                $"SELECT DISTINCT CostElement FROM {db.Table("dict.CostCategory")} WHERE SupersededAt IS NULL AND (Project IS NULL OR Project = @project)",
                new { project })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void AbandonRunning(Func<long, bool> isAlive, DateTimeOffset at)
    {
        using var connection = db.Open();
        var dead = connection.Query<long>($"SELECT PinId FROM {_pins} WHERE Status = @running", new { running = RunStatuses.Running })
            .Where(id => !isAlive(id)).ToList();
        if (dead.Count == 0)
            return;
        connection.Execute($"UPDATE {_pins} SET Status = @abandoned, FinishedAt = @at WHERE PinId IN @dead AND Status = @running",
            new { dead, at, abandoned = RunStatuses.Abandoned, running = RunStatuses.Running });
        connection.Execute($"UPDATE {_steps} SET Status = @failed, FinishedAt = @at WHERE PinId IN @dead AND Status = @running",
            new { dead, at, failed = RunStepStatuses.Failed, running = RunStepStatuses.Running });
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
        return connection.Query<(long Id, long PinId, string CheckName, string Level, string? Element, string Message, decimal? Amount, int? StepNo)>(
                $"SELECT Id, PinId, CheckName, Level, Element, Message, Amount, StepNo FROM {_issues} WHERE PinId = @pinId ORDER BY Id", new { pinId })
            .Select(i => new RunIssueRow(i.Id, i.PinId, i.CheckName, Enum.Parse<CheckLevel>(i.Level), i.Element, i.Message, i.Amount, i.StepNo))
            .ToList();
    }

    public IReadOnlySet<string> ProjectSources(string project)
    {
        using var connection = db.Open();
        return connection.Query<string>($"SELECT SourceCode FROM {db.Table("dict.ProjectSource")} WHERE Project = @project AND SupersededAt IS NULL", new { project })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public RunDictionaryData Dictionaries(string project, int year)
    {
        using var connection = db.Open();
        // Cost Category efektywny: wiersz projektu przed globalnym (jak RUN_PinBalance – cost elementy rozliczeniowe).
        var effective = connection.Query<(string CostElement, string? CostCategory, string? CostKind, string? Description)>(
                $"""
                SELECT CostElement, NULLIF(LTRIM(RTRIM(CostCategory)), N'') AS CostCategory, NULLIF(LTRIM(RTRIM(CostKind)), N'') AS CostKind, Description
                FROM (SELECT CostElement, CostCategory, CostKind, Description,
                             ROW_NUMBER() OVER (PARTITION BY CostElement ORDER BY CASE WHEN Project IS NULL THEN 1 ELSE 0 END) AS rn
                      FROM {db.Table("dict.CostCategory")} WHERE SupersededAt IS NULL AND (Project = @project OR Project IS NULL)) c
                WHERE c.rn = 1
                """, new { project })
            .ToList();
        var categories = effective.ToDictionary(r => r.CostElement, r => r.CostCategory, StringComparer.OrdinalIgnoreCase);
        var kinds = effective.Where(r => r.CostKind is not null).ToDictionary(r => r.CostElement, r => r.CostKind!, StringComparer.OrdinalIgnoreCase);
        var names = effective.Where(r => !string.IsNullOrWhiteSpace(r.Description)).ToDictionary(r => r.CostElement, r => r.Description!, StringComparer.OrdinalIgnoreCase);
        var wps = connection.Query<RunWpRow>(
                $"""
                SELECT w.Wp, w.Mpk,
                       CAST(CASE WHEN EXISTS (SELECT 1 FROM {db.Table("dict.ScheduleBudget")} b
                                              WHERE b.Project = @project AND b.SupersededAt IS NULL AND b.Wp = w.Wp
                                                AND (ISNULL(b.Bac, 0) <> 0 OR ISNULL(b.BacHours, 0) <> 0 OR ISNULL(b.BacMaterial, 0) <> 0))
                                 THEN 1 ELSE 0 END AS BIT) AS HasBudget
                FROM (SELECT DISTINCT Wp, NULLIF(LTRIM(RTRIM(Mpk)), N'') AS Mpk FROM {db.Table("dict.WpCam")}
                      WHERE Project = @project AND SupersededAt IS NULL) w
                ORDER BY w.Wp
                """, new { project })
            .ToList();
        var rated = connection.Query<string>(
                $"SELECT DISTINCT CostCenter FROM {db.Table("dict.DepartmentRate")} WHERE Project IS NULL AND SupersededAt IS NULL AND Year = @year",
                new { year })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var budget = connection.QuerySingle<RunBudgetTotals>(
            $"""
            SELECT ISNULL(SUM(Bac), 0) AS Bac, ISNULL(SUM(BacHours), 0) AS BacHours, ISNULL(SUM(BacMaterial), 0) AS BacMaterial
            FROM {db.Table("dict.ScheduleBudget")} WHERE Project = @project AND SupersededAt IS NULL
            """, new { project });
        return new RunDictionaryData(categories, wps, rated, year, budget, kinds, names);
    }

    public RunLines ActualsLines(string wbsElement, string costElement, int year, int period, DateTimeOffset asOf, string amountField, int top)
    {
        using var connection = db.Open();
        var parser = SqlCanonical.CurrentParser(connection, db, "ACTUALS") ?? throw new InvalidOperationException("Nie ma parsera ACTUALS.");
        // Pole parsera bez slotu (usunięte z parsera) – NULL: warunek bez wierszy, kolumna pusta.
        string Slot(string field) => SqlCanonical.SlotOrNull(parser, field) is var slot && slot == "NULL" ? "NULL" : "r." + slot;
        string Text(string field) => Slot(field) is var slot && slot == "NULL" ? "NULL" : $"CAST({slot} AS NVARCHAR(400))";
        string Number(string field) => Slot(field) is var slot && slot == "NULL" ? "NULL" : $"TRY_CONVERT(DECIMAL(28,8), {slot})";
        var from =
            $"""
            FROM {db.Table("can.LatestFiles")}('ACTUALS', NULL, @asOf) f
            JOIN {db.Table(DbTables.CanonicalRow)} r ON r.FileId = f.FileId AND r.ParserId = @parserId
            WHERE {Slot("WbsElement")} = @wbsElement AND {Slot("CostElement")} = @costElement
              AND TRY_CONVERT(INT, {Slot("FiscalYear")}) = @year AND TRY_CONVERT(INT, {Slot("Period")}) = @period
            """;
        var args = new { asOf, parserId = parser.ParserId, wbsElement, costElement, year, period, top };
        var rows = connection.Query<RunLineRow>(
            $"""
            SELECT TOP (@top) f.FileName, r.RowNumber, {Text("CreatedOn")} AS CreatedOn, {Number(amountField)} AS Amount, {Number("TotalQuantity")} AS Quantity,
                   {Text("CostElementName")} AS CostElementName, {Text("CoObjectName")} AS CoObjectName, {Text("PartnerObject")} AS PartnerObject,
                   {Text("PurchaseOrderNumber")} AS PurchaseOrderNumber, {Text("InvoiceNumber")} AS InvoiceNumber, {Text("OriginalOrderNumber")} AS OriginalOrderNumber
            {from}
            ORDER BY ABS(ISNULL({Number(amountField)}, 0)) DESC, f.FileName, r.RowNumber
            """, args, commandTimeout: LongTimeout).ToList();
        var (count, sum) = connection.QuerySingle<(int, decimal)>(
            $"SELECT COUNT(*), ISNULL(SUM({Number(amountField)}), 0) {from}", args, commandTimeout: LongTimeout);
        return new RunLines(rows, count, sum);
    }

    public IReadOnlyList<RunReconciliationRow> Reconciliations()
    {
        using var connection = db.Open();
        return connection.Query<RunReconciliationRow>(
            $"SELECT ProjectType, Name, RunKind, IsActive, LeftFormula, Comparison, RightFormula, Tolerance, Level FROM {db.Table("dict.RunReconciliation")} " +
            "WHERE Project IS NULL AND SupersededAt IS NULL ORDER BY Name").ToList();
    }

    public IReadOnlyList<RunProjectRow> Projects()
    {
        using var connection = db.Open();
        return connection.Query<RunProjectRow>(
            $"SELECT Code, Name, ProjectType FROM {db.Table("meta.Project")} WHERE SupersededAt IS NULL AND Active = 1 ORDER BY Code").ToList();
    }

    public string? ProjectType(string project)
    {
        using var connection = db.Open();
        return connection.QueryFirstOrDefault<string?>(
            $"SELECT ProjectType FROM {db.Table("meta.Project")} WHERE Code = @project AND SupersededAt IS NULL", new { project });
    }

    public IReadOnlyList<RunCheckRow> CheckSettings()
    {
        using var connection = db.Open();
        return connection.Query<RunCheckRow>(
            $"SELECT ProjectType, RuleName, RunKind, IsActive, Level, Threshold FROM {db.Table("dict.RunCheck")} WHERE Project IS NULL AND SupersededAt IS NULL")
            .ToList();
    }

    public RunPinRow? Pin(long pinId)
    {
        using var connection = db.Open();
        return connection.QueryFirstOrDefault<RunPinRow>($"SELECT {PinColumns} FROM {PinFrom} WHERE p.PinId = @pinId", new { pinId });
    }

    public CalendarWeek RunWeek(long runId)
    {
        using var connection = db.Open();
        var run = connection.QuerySingle<(int Year, int Week, string Period, bool IsClosing)>(
            $"SELECT Year, Week, Period, IsClosing FROM {_runs} WHERE RunId = @runId", new { runId });
        var week = connection.QueryFirstOrDefault<CalendarWeek>(
            $"SELECT TOP (1) Year, Week, Period, DateFrom, DateTo, IsClosing FROM {db.Table("dict.Calendar")} " +
            "WHERE Project IS NULL AND SupersededAt IS NULL AND Year = @Year AND Week = @Week", new { run.Year, run.Week });
        return week ?? new CalendarWeek(run.Year, run.Week, run.Period, DateTime.MinValue, DateTime.MinValue, run.IsClosing);
    }

    public (IReadOnlyList<RunDefinitionHeaderRow> Headers, IReadOnlyList<RunStepRowData> Steps, IReadOnlyList<RunStepElementRow> Elements) DefinitionRows()
    {
        using var connection = db.Open();
        const string Current = "WHERE Project IS NULL AND SupersededAt IS NULL";
        return (
            connection.Query<RunDefinitionHeaderRow>(
                $"SELECT Code, ProjectType, IsActive, Description FROM {db.Table("dict.RunDefinition")} {Current}").ToList(),
            connection.Query<RunStepRowData>(
                $"SELECT Definition, StepNo AS No, Name, Action, WarningGate, Approval, Description FROM {db.Table("dict.RunStep")} {Current}").ToList(),
            connection.Query<RunStepElementRow>(
                $"SELECT Definition, Step, Element, IsActive, Level, Threshold FROM {db.Table("dict.RunStepElement")} {Current}").ToList());
    }

    public void StartSteps(long pinId, string definitionJson, IReadOnlyList<RunStepDefinition> steps) =>
        db.InTransaction((connection, transaction) =>
        {
            connection.Execute($"UPDATE {_pins} SET Definition = @definitionJson WHERE PinId = @pinId", new { pinId, definitionJson }, transaction);
            connection.Execute($"DELETE FROM {_steps} WHERE PinId = @pinId", new { pinId }, transaction);
            connection.Execute($"INSERT INTO {_steps} (PinId, StepNo, Name, Action, Status) VALUES (@pinId, @No, @Name, @Action, @Status)",
                steps.Select((s, i) => new { pinId, s.No, s.Name, s.Action, Status = i == 0 ? RunStepStatuses.Ready : RunStepStatuses.Waiting }), transaction);
            return true;
        });

    public string? DefinitionOf(long pinId)
    {
        using var connection = db.Open();
        return connection.QueryFirstOrDefault<string?>($"SELECT Definition FROM {_pins} WHERE PinId = @pinId", new { pinId });
    }

    public IReadOnlyList<RunStepRow> Steps(long pinId)
    {
        using var connection = db.Open();
        return connection.Query<RunStepRow>(
            $"SELECT PinId, StepNo, Name, Action, Status, StartedAt, FinishedAt, ActedBy, Errors, Warnings, Comment FROM {_steps} WHERE PinId = @pinId ORDER BY StepNo",
            new { pinId }).ToList();
    }

    public void SetStep(long pinId, int stepNo, string status, DateTimeOffset? startedAt, DateTimeOffset? finishedAt, string? actedBy, int errors, int warnings,
        string? comment = null)
    {
        using var connection = db.Open();
        connection.Execute(
            $"""
            UPDATE {_steps} SET Status = @status, StartedAt = @startedAt, FinishedAt = @finishedAt, ActedBy = @actedBy, Errors = @errors, Warnings = @warnings,
                Comment = ISNULL(@comment, Comment)
            WHERE PinId = @pinId AND StepNo = @stepNo
            """, new { pinId, stepNo, status, startedAt, finishedAt, actedBy, errors, warnings, comment });
    }

    public void ReplaceStepIssues(long pinId, int stepNo, IReadOnlyList<(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount)> issues) =>
        db.InTransaction((connection, transaction) =>
        {
            connection.Execute($"DELETE FROM {_issues} WHERE PinId = @pinId AND StepNo = @stepNo", new { pinId, stepNo }, transaction);
            connection.Execute(
                $"INSERT INTO {_issues} (PinId, StepNo, CheckName, Level, Element, Message, Amount) VALUES (@pinId, @stepNo, @Check, @Level, @Element, @Message, @Amount)",
                issues.Select(i => new { pinId, stepNo, i.Check, Level = i.Level.ToString(), i.Element, i.Message, i.Amount }), transaction);
            return true;
        });

    public void SetPinState(long pinId, string status, DateTimeOffset? finishedAt, string summary)
    {
        using var connection = db.Open();
        connection.Execute(
            $"""
            UPDATE {_pins} SET Status = @status, FinishedAt = @finishedAt, Summary = @summary,
                Errors = (SELECT COUNT(*) FROM {_issues} WHERE PinId = @pinId AND Level = 'Error'),
                Warnings = (SELECT COUNT(*) FROM {_issues} WHERE PinId = @pinId AND Level = 'Warning')
            WHERE PinId = @pinId
            """, new { pinId, status, finishedAt, summary });
    }

    public void SetPinImport(long pinId, long? importBatchId)
    {
        using var connection = db.Open();
        connection.Execute($"UPDATE {_pins} SET ImportBatchId = @importBatchId WHERE PinId = @pinId", new { pinId, importBatchId });
    }

    public void SetPinBalance(long pinId, long rows, decimal grossCost, decimal balance)
    {
        using var connection = db.Open();
        connection.Execute($"UPDATE {_pins} SET Rows = @rows, GrossCost = @grossCost, Balance = @balance WHERE PinId = @pinId", new { pinId, rows, grossCost, balance });
    }

    public IReadOnlyDictionary<string, string> DefinitionAssignments()
    {
        using var connection = db.Open();
        return connection.Query<(string Project, string Value)>(
                $"SELECT Project, Value FROM {db.Table("dict.ProjectSetting")} WHERE Project IS NOT NULL AND Name = @name AND SupersededAt IS NULL AND Value IS NOT NULL",
                new { name = ProjectSettings.RunDefinition })
            .GroupBy(r => r.Project, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public string? AssignedDefinition(string project)
    {
        using var connection = db.Open();
        return connection.QueryFirstOrDefault<string?>(
            $"SELECT Value FROM {db.Table("dict.ProjectSetting")} WHERE Project = @project AND Name = @name AND SupersededAt IS NULL",
            new { project, name = ProjectSettings.RunDefinition })?.Trim() is { Length: > 0 } code ? code : null;
    }

    public string ActualsAmount(string project)
    {
        using var connection = db.Open();
        return ProjectSettings.ActualsAmountOf(connection.QueryFirstOrDefault<string?>(
            $"SELECT Value FROM {db.Table("dict.ProjectSetting")} WHERE Project = @project AND Name = @name AND SupersededAt IS NULL",
            new { project, name = ProjectSettings.ActualsAmount }));
    }
}
