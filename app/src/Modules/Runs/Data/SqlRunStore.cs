using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Runs.Data;

/// <summary>Przebiegi w MS SQL (migracja 027); dane projektu w chwili przypięcia – procedura RUN_PinBalance; reguły walidacji w SQL (migracja 051).</summary>
public sealed class SqlRunStore(SqlDatabase db) : IRunStore
{
    private readonly string _runs = db.Table(DbTables.Run);
    private readonly string _pins = db.Table(DbTables.RunPin);
    private readonly string _balance = db.Table(DbTables.RunBalance);
    private readonly string _issues = db.Table(DbTables.RunIssue);
    private readonly string _steps = db.Table("meta.RunStep");
    private readonly string _ev = db.Table(DbTables.RunBalanceEv);

    private const int LongTimeout = 1800;

    private string PinColumns =>
        "p.PinId, p.RunId, r.Code, r.Project, p.PinNo, p.StartedAt, p.StartedBy, p.FinishedAt, p.Status, p.ImportBatchId, p.Rows, p.GrossCost, p.Balance, " +
        $"p.Errors, p.Warnings, p.Summary, p.AmountField, r.IsClosing AS IsClosingWeek, r.Period, CAST(CASE WHEN {PeriodClose} THEN 1 ELSE 0 END AS BIT) AS IsPeriodClose";

    /// <summary>Zamknięcie okresu: ostatnie zakończone uruchomienie (zakończony / z ostrzeżeniami / z błędami) przebiegu tygodnia zamykającego.</summary>
    private string PeriodClose =>
        $"r.IsClosing = 1 AND p.PinId = (SELECT TOP (1) x.PinId FROM {_pins} x WHERE x.RunId = p.RunId " +
        $"AND x.Status IN (N'{RunStatuses.Ok}', N'{RunStatuses.Warnings}', N'{RunStatuses.Errors}') ORDER BY x.PinNo DESC)";

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

    public IReadOnlyList<RunPinFile> PinFiles(string project, DateTimeOffset asOf)
    {
        using var connection = db.Open();
        return connection.Query<RunPinFile>(
            $"""
            WITH defs AS (
                SELECT DISTINCT v.Code FROM {db.Table("meta.PerformanceObjective")} po
                CROSS APPLY (VALUES (po.ProjectDefinition), (po.WbsElement)) v(Code)
                WHERE po.Project = @project AND po.SupersededAt IS NULL AND v.Code IS NOT NULL)
            SELECT f.FileId, f.FileName, f.ModifiedAt AS ReportAt, f.ImportedAt, f.BatchId
            FROM {db.Table("can.LatestFiles")}('ACTUALS', NULL, @asOf) f
            WHERE EXISTS (SELECT 1 FROM {db.Table(DbTables.ActualsBalance)} b JOIN defs d ON d.Code = b.ProjectDefinition WHERE b.FileId = f.FileId)
            ORDER BY f.FileName
            """, new { project, asOf }, commandTimeout: LongTimeout).ToList();
    }

    /// <summary>Słowniki czytane przez przebieg (dane „ev”, reguły walidacji): tabela → nazwa na ekranie.</summary>
    private static readonly (string Table, string Name)[] RunDictionaries =
    [
        ("dict.CostCategory", "Cost Category"), ("dict.Exclusion", "Wykluczenia"), ("dict.WpCam", "WP i CAM"), ("dict.ScheduleBudget", "Harmonogram i budżet"),
        ("dict.DepartmentRate", "Stawki wydziałów"), ("dict.FxRate", "Kursy walut"), ("dict.RunValidation", "Reguły walidacji"),
    ];

    public IReadOnlyList<RunDictionaryChange> DictionaryChanges(string project, DateTimeOffset since)
    {
        using var connection = db.Open();
        var changes = new List<RunDictionaryChange>();
        foreach (var (table, name) in RunDictionaries)
        {
            // Zmiana wiersza = nowa wersja (RecordedAt) albo zamknięcie (SupersededAt) po chwili since.
            var rows = connection.Query<(long RowId, DateTimeOffset At, string By)>(
                $"""
                SELECT RowId, RecordedAt, RecordedBy FROM {db.Table(table)} WHERE (Project = @project OR Project IS NULL) AND RecordedAt > @since
                UNION ALL
                SELECT RowId, SupersededAt, SupersededBy FROM {db.Table(table)} WHERE (Project = @project OR Project IS NULL) AND SupersededAt > @since
                """, new { project, since }).ToList();
            if (rows.Count > 0)
            {
                var last = rows.MaxBy(r => r.At);
                changes.Add(new RunDictionaryChange(name, rows.Select(r => r.RowId).Distinct().Count(), last.At, last.By ?? ""));
            }
        }
        return changes;
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

    public IReadOnlyList<RunRule> Rules()
    {
        using var connection = db.Open();
        var rules = connection.Query<(string Name, string? Description, string Level, bool IsActive, string SqlText, string? ResultTemplate)>(
                $"SELECT Name, Description, Level, IsActive, SqlText, ResultTemplate FROM {db.Table("dict.RunValidation")} " +
                "WHERE Project IS NULL AND SupersededAt IS NULL ORDER BY Id")
            .Select(r => new RunRule(r.Name, RunRules.LevelOf(r.Level) ?? CheckLevel.Warning, r.IsActive, r.SqlText, r.ResultTemplate, r.Description))
            .ToList();
        return rules.Count == 0 ? RunRules.Starters : rules;
    }

    public void SaveEv(long pinId, IReadOnlyList<RunEvRow> rows)
    {
        using var connection = db.Open();
        connection.Execute($"DELETE FROM {_ev} WHERE PinId = @pinId", new { pinId }, commandTimeout: LongTimeout);
        SqlBulk.InsertInBatches(connection, _ev,
            [("PinId", typeof(long)), ("WbsElement", typeof(string)), ("CostElement", typeof(string)), ("FiscalYear", typeof(int)), ("Period", typeof(int)),
             ("CostKind", typeof(string)), ("Amount", typeof(decimal))],
            rows.Select(r => new object?[] { pinId, r.WbsElement, r.CostElement, r.FiscalYear, r.Period, r.CostKind, r.Amount }));
    }

    public bool HasEv(long pinId)
    {
        using var connection = db.Open();
        return connection.ExecuteScalar<int?>($"SELECT TOP (1) 1 FROM {_ev} WHERE PinId = @pinId", new { pinId }) is not null;
    }

    public RunRuleResult RunRule(string sql, RunRuleContext context)
    {
        if (RunRules.Check(sql) is { } refused)
            return RunRuleResult.Failed(refused);
        var (text, offset) = RunRules.Compose(RuleTables(), sql);
        using var connection = db.Open();
        // Reguła tylko czyta (RunRules.Check), a transakcja i tak jest zawsze wycofywana.
        using var transaction = connection.BeginTransaction();
        try
        {
            return Read(connection, transaction, text, context);
        }
        catch (SqlException ex)
        {
            return RunRuleResult.Failed(RuleError(ex, offset));
        }
        finally
        {
            try
            {
                transaction.Rollback();
            }
            catch (InvalidOperationException)
            {
                // Transakcja już wycofana przez SQL Server (błąd zapytania).
            }
        }
    }

    private static RunRuleResult Read(SqlConnection connection, SqlTransaction transaction, string text, RunRuleContext context)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = text;
        command.CommandTimeout = RunRules.TimeoutSeconds;
        static string Period((int Year, int Period) p) => $"{p.Year}/{Math.Min(p.Period, 12):00}";
        void Add(string name, SqlDbType type, object? value, int size = 0) =>
            command.Parameters.Add(new SqlParameter(name, type, size) { Value = value ?? DBNull.Value });
        Add("@PinId", SqlDbType.BigInt, context.PinId);
        Add("@PoprzedniPinId", SqlDbType.BigInt, context.PreviousPinId);
        Add("@Program", SqlDbType.NVarChar, context.Project, 20);
        Add("@BiezacyOkres", SqlDbType.NVarChar, Period(context.Today), 7);
        Add("@PierwszyOtwartyOkres", SqlDbType.NVarChar, Period(context.ClosedBefore), 7);
        Add("@JestPoprzedni", SqlDbType.Int, context.PreviousPinId is null ? 0 : 1);
        Add("@WalutaKosztow", SqlDbType.NVarChar, context.Currency, 20);
        Add("@TypProgramu", SqlDbType.NVarChar, context.ProjectType, 20);
        var reader = command.ExecuteReader();
        var rows = new List<IReadOnlyList<object?>>();
        var truncated = false;
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(i =>
            {
                var basis = reader.GetName(i) is { Length: > 0 } name ? name : $"Kolumna{i + 1}";
                var unique = Enumerable.Range(1, 99).Select(n => n == 1 ? basis : $"{basis}{n}").First(names.Add);
                var type = reader.GetFieldType(i);
                return new RunRuleColumn(unique, type == typeof(decimal) || type == typeof(int) || type == typeof(long) || type == typeof(short)
                                                 || type == typeof(byte) || type == typeof(double) || type == typeof(float));
            }).ToList();
            while (reader.Read())
            {
                if (rows.Count == RunRules.MaxRows)
                {
                    truncated = true;
                    command.Cancel();
                    break;
                }
                var values = new object?[reader.FieldCount];
                reader.GetValues(values!);
                rows.Add(values.Select(v => v is DBNull ? null : v).ToList());
            }
            return new RunRuleResult(columns, rows, truncated);
        }
        finally
        {
            try
            {
                reader.Dispose();
            }
            catch (SqlException) when (truncated)
            {
                // Przerwane czytanie nadmiarowych wierszy (Cancel).
            }
        }
    }

    // Błąd SQL Server z linią w zapytaniu reguły (linie tabel danych uruchomienia – przed zapytaniem); limit czasu – opis.
    private static string RuleError(SqlException ex, int offset)
    {
        if (ex.Number == -2)
            return $"Przekroczony limit czasu zapytania ({RunRules.TimeoutSeconds} s) – zawęź dane albo uprość zapytanie";
        return string.Join("; ", ex.Errors.Cast<SqlError>().Select(e => e.LineNumber - offset is var line and > 0 ? $"linia {line}: {e.Message}" : e.Message).Distinct());
    }

    // Tabele danych uruchomienia dla zapytania reguły (RunRules.Tables): CTE z parametrami uruchomienia.
    private string RuleTables()
    {
        // Rok i okres (okresy 13–16 jak 12) → „RRRR/OO” i liczba okresów przed bieżącym.
        const string Periods =
            """
            CROSS APPLY (SELECT CASE WHEN b.Period > 12 THEN 12 ELSE b.Period END AS M) m
                CROSS APPLY (SELECT CONCAT(b.FiscalYear, N'/', RIGHT(N'0' + CAST(m.M AS NVARCHAR(2)), 2)) AS RokOkres,
                                    CAST(LEFT(@BiezacyOkres, 4) AS INT) * 12 + CAST(RIGHT(@BiezacyOkres, 2) AS INT) - (b.FiscalYear * 12 + m.M) AS OkresyWstecz) p
            """;
        string Balance(string pin) =>
            $"""
                SELECT NULLIF(b.WbsElement, N'') AS ElementWBS, NULLIF(b.CostElement, N'') AS CostElement, b.FiscalYear AS Rok, b.Period AS Okres, p.RokOkres, p.OkresyWstecz,
                       b.IsSettlement AS Rozliczeniowy, b.Currency AS Waluta, b.Rows AS Wiersze, b.Amount AS Kwota, b.LastCreatedOn AS Utworzono,
                       CAST(CASE WHEN p.RokOkres < @PierwszyOtwartyOkres THEN 1 ELSE 0 END AS BIT) AS OkresZamkniety,
                       CAST(CASE WHEN p.RokOkres > @BiezacyOkres THEN 1 ELSE 0 END AS BIT) AS OkresPrzyszly
                FROM {_balance} b
                {Periods}
                WHERE b.PinId = {pin}
            """;
        string Ev(string pin) =>
            $"""
                SELECT NULLIF(b.WbsElement, N'') AS ElementWBS, NULLIF(b.CostElement, N'') AS CostElement, b.FiscalYear AS Rok, b.Period AS Okres, p.RokOkres, p.OkresyWstecz,
                       CAST(CASE WHEN p.RokOkres < @PierwszyOtwartyOkres THEN 1 ELSE 0 END AS BIT) AS OkresZamkniety, b.CostKind AS RodzajKosztu, b.Amount AS Kwota
                FROM {_ev} b
                {Periods}
                WHERE b.PinId = {pin}
            """;
        return
            $"""
            WITH biezace AS (
            {Balance("@PinId")}),
            poprzednie AS (
            {Balance("@PoprzedniPinId")}),
            ev AS (
            {Ev("@PinId")}),
            ev_poprzednie AS (
            {Ev("@PoprzedniPinId")}),
            cost_category AS (
                SELECT c.CostElement, c.Description AS Opis, NULLIF(LTRIM(RTRIM(c.CostCategory)), N'') AS CostCategory,
                       CASE WHEN LTRIM(RTRIM(c.CostKind)) IN (N'{GlobalDictionaries.LaborKind}', N'{GlobalDictionaries.MaterialKind}') THEN LTRIM(RTRIM(c.CostKind))
                            ELSE N'{GlobalDictionaries.OtherKind}' END AS RodzajKosztu,
                       CAST(ISNULL(c.IsSettlement, 0) AS BIT) AS Rozliczeniowy
                FROM (SELECT CostElement, Description, CostCategory, CostKind, IsSettlement,
                             ROW_NUMBER() OVER (PARTITION BY CostElement ORDER BY CASE WHEN Project IS NULL THEN 1 ELSE 0 END) AS rn
                      FROM {db.Table("dict.CostCategory")} WHERE SupersededAt IS NULL AND (Project = @Program OR Project IS NULL)) c
                WHERE c.rn = 1),
            wp_cam AS (
                SELECT P1sElement AS ElementP1S, Wp AS WP, NULLIF(LTRIM(RTRIM(Cam)), N'') AS CAM, NULLIF(LTRIM(RTRIM(Mpk)), N'') AS MPK
                FROM {db.Table("dict.WpCam")} WHERE Project = @Program AND SupersededAt IS NULL),
            stawki AS (
                SELECT CostCenter AS MPK, Year AS Rok, LaborRate FROM {db.Table("dict.DepartmentRate")} WHERE Project IS NULL AND SupersededAt IS NULL),
            harmonogram AS (
                SELECT Wp AS WP, Bac AS BAC, BacHours, BacMaterial, Currency AS Waluta
                FROM {db.Table("dict.ScheduleBudget")} WHERE Project = @Program AND SupersededAt IS NULL)
            """;
    }

    public IReadOnlyList<RunExclusionRow> Exclusions(string project)
    {
        using var connection = db.Open();
        return connection.Query<RunExclusionRow>(
            $"SELECT CostElement, WbsElement, PartnerObject FROM {db.Table("dict.Exclusion")} WHERE Project = @project AND SupersededAt IS NULL", new { project }).ToList();
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

    public IReadOnlyList<RunPinRow> ClosingPins(string project)
    {
        using var connection = db.Open();
        return connection.Query<RunPinRow>($"SELECT {PinColumns} FROM {PinFrom} WHERE r.Project = @project AND {PeriodClose} ORDER BY r.Year DESC, r.Week DESC",
            new { project }).ToList();
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
        return connection.Query<(long Id, long PinId, string CheckName, string Level, string? Element, string Message, decimal? Amount, int? StepNo, string? Details)>(
                $"SELECT Id, PinId, CheckName, Level, Element, Message, Amount, StepNo, Details FROM {_issues} WHERE PinId = @pinId ORDER BY Id", new { pinId })
            .Select(i => new RunIssueRow(i.Id, i.PinId, i.CheckName, Enum.Parse<CheckLevel>(i.Level), i.Element, i.Message, i.Amount, i.StepNo, i.Details))
            .ToList();
    }

    public IReadOnlySet<string> ProjectSources(string project)
    {
        using var connection = db.Open();
        return connection.Query<string>($"SELECT SourceCode FROM {db.Table("dict.ProjectSource")} WHERE Project = @project AND SupersededAt IS NULL", new { project })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public RunDictionaryData Dictionaries(string project)
    {
        using var connection = db.Open();
        // Cost Category efektywny: wiersz projektu przed globalnym (jak RUN_PinBalance – cost elementy rozliczeniowe).
        var effective = connection.Query<(string CostElement, string? CostKind, string? Description)>(
                $"""
                SELECT CostElement, NULLIF(LTRIM(RTRIM(CostKind)), N'') AS CostKind, Description
                FROM (SELECT CostElement, CostKind, Description,
                             ROW_NUMBER() OVER (PARTITION BY CostElement ORDER BY CASE WHEN Project IS NULL THEN 1 ELSE 0 END) AS rn
                      FROM {db.Table("dict.CostCategory")} WHERE SupersededAt IS NULL AND (Project = @project OR Project IS NULL)) c
                WHERE c.rn = 1
                """, new { project })
            .ToList();
        return new RunDictionaryData(
            effective.Where(r => r.CostKind is not null).ToDictionary(r => r.CostElement, r => r.CostKind!, StringComparer.OrdinalIgnoreCase),
            effective.Where(r => !string.IsNullOrWhiteSpace(r.Description)).ToDictionary(r => r.CostElement, r => r.Description!, StringComparer.OrdinalIgnoreCase));
    }

    public RunLines ActualsLines(string wbsElement, string costElement, int year, int period, DateTimeOffset asOf, string amountField, int top)
    {
        var lines = ActualsLines(null, wbsElement, [costElement], year, period, asOf, amountField, top, exactPeriod: true);
        return new RunLines(lines.Rows.Select(r => new RunLineRow(r.FileName, r.RowNumber, r.CreatedOn, r.Amount, r.Quantity, r.CostElementName, r.CoObjectName,
            r.PartnerObject, r.PurchaseOrderNumber, r.InvoiceNumber, r.OriginalOrderNumber)).ToList(), lines.Count, lines.Sum);
    }

    public RunActualsLines ActualsLines(RunActualsScope scope, DateTimeOffset asOf, string amountField, int top) =>
        ActualsLines(scope.PinId, scope.WbsElement, scope.CostElements, scope.Year, scope.Period, asOf, amountField, top, exactPeriod: false);

    // Linie ACTUALS z najnowszych wersji plików do asOf: element WBS (null – elementy WBS danych przypiętych w uruchomieniu pinId),
    // cost elementy, rok, okres (exactPeriod false – okres 12 obejmuje 13–16, jak RokOkres reguł).
    private RunActualsLines ActualsLines(long? pinId, string? wbsElement, IReadOnlyCollection<string>? costElements, int? year, int? period, DateTimeOffset asOf,
        string amountField, int top, bool exactPeriod)
    {
        using var connection = db.Open();
        var parser = SqlCanonical.CurrentParser(connection, db, "ACTUALS") ?? throw new InvalidOperationException("Nie ma parsera ACTUALS.");
        // Pole parsera bez slotu (usunięte z parsera) – NULL: warunek bez wierszy, kolumna pusta.
        string Slot(string field) => SqlCanonical.SlotOrNull(parser, field) is var slot && slot == "NULL" ? "NULL" : "r." + slot;
        string Text(string field) => Slot(field) is var slot && slot == "NULL" ? "NULL" : $"CAST({slot} AS NVARCHAR(400))";
        string Number(string field) => Slot(field) is var slot && slot == "NULL" ? "NULL" : $"TRY_CONVERT(DECIMAL(28,8), {slot})";
        string Integer(string field) => Slot(field) is var slot && slot == "NULL" ? "NULL" : $"TRY_CONVERT(INT, {slot})";
        var periodValue = exactPeriod ? Integer("Period") : $"CASE WHEN {Integer("Period")} > 12 THEN 12 ELSE {Integer("Period")} END";
        var conditions = new List<string>
        {
            wbsElement is not null ? $"{Slot("WbsElement")} = @wbsElement"
                : $"{Slot("WbsElement")} IN (SELECT WbsElement FROM {_balance} WHERE PinId = @pinId AND WbsElement <> N'')",
        };
        if (costElements is not null)
            conditions.Add($"{Slot("CostElement")} IN @costElements");
        if (year is not null)
            conditions.Add($"{Integer("FiscalYear")} = @year");
        if (period is not null)
            conditions.Add($"{periodValue} = @period");
        var from =
            $"""
            FROM {db.Table("can.LatestFiles")}('ACTUALS', NULL, @asOf) f
            JOIN {db.Table(DbTables.CanonicalRow)} r ON r.FileId = f.FileId AND r.ParserId = @parserId
            WHERE {string.Join(" AND ", conditions)}
            """;
        var args = new { asOf, parserId = parser.ParserId, pinId, wbsElement, costElements = costElements?.ToArray() ?? [], year, period, top };
        var rows = connection.Query<RunActualsLine>(
            $"""
            SELECT TOP (@top) {Text("WbsElement")} AS WbsElement, {Text("CostElement")} AS CostElement, {Integer("FiscalYear")} AS FiscalYear, {Integer("Period")} AS Period,
                   f.FileName, r.RowNumber, {Text("CreatedOn")} AS CreatedOn, {Number(amountField)} AS Amount, {Number("TotalQuantity")} AS Quantity,
                   {Text("CostElementName")} AS CostElementName, {Text("CoObjectName")} AS CoObjectName, {Text("PartnerObject")} AS PartnerObject,
                   {Text("PurchaseOrderNumber")} AS PurchaseOrderNumber, {Text("InvoiceNumber")} AS InvoiceNumber, {Text("OriginalOrderNumber")} AS OriginalOrderNumber
            {from}
            ORDER BY ABS(ISNULL({Number(amountField)}, 0)) DESC, f.FileName, r.RowNumber
            """, args, commandTimeout: LongTimeout).ToList();
        var (count, sum) = connection.QuerySingle<(int, decimal)>(
            $"SELECT COUNT(*), ISNULL(SUM({Number(amountField)}), 0) {from}", args, commandTimeout: LongTimeout);
        return new RunActualsLines(rows, count, sum);
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
                $"SELECT Definition, Step, Element, IsActive FROM {db.Table("dict.RunStepElement")} {Current}").ToList());
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

    public void ReplaceStepIssues(long pinId, int stepNo, IReadOnlyList<(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount, string? Details)> issues) =>
        db.InTransaction((connection, transaction) =>
        {
            connection.Execute($"DELETE FROM {_issues} WHERE PinId = @pinId AND StepNo = @stepNo", new { pinId, stepNo }, transaction);
            connection.Execute(
                $"INSERT INTO {_issues} (PinId, StepNo, CheckName, Level, Element, Message, Amount, Details) VALUES (@pinId, @stepNo, @Check, @Level, @Element, @Message, @Amount, @Details)",
                issues.Select(i => new { pinId, stepNo, Check = Clip(i.Check, 200), Level = i.Level.ToString(), Element = Clip(i.Element, 400), Message = Clip(i.Message, 2000),
                    i.Amount, i.Details }), transaction);
            return true;
        });

    // Tekst przycięty do długości kolumny RUN_Issue (opis wyniku reguły z wieloma kolumnami bywa długi).
    private static string? Clip(string? text, int length) => text is { Length: var n } && n > length ? text[..(length - 1)] + "…" : text;

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
