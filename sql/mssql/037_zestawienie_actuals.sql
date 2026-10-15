/* PZL-EV – migracja 037: zestawienie ACTUALS po Project definition (docs/model-danych.md, rozdz. 5).
   Odczyty kosztów projektu czytały CAN_Row (wszystkie wersje plików, wszystkie projekty – columnstore bez możliwości
   zawężenia po projekcie) i dopiero potem filtrowały po Project definition – czas rósł z historią i liczbą projektów.
   - CAN_ActualsBalance: zestawienie każdej wersji pliku ACTUALS – klucz: plik, Project definition, WBS element, Cost
     element, klasa i obiekt partnera, rok, okres, waluty; sumy ValueObjCrcy, ValueRepCur, ValueTranCurr, TotalQuantity,
     liczba wierszy, najpóźniejsze Created on. Indeks klastrowy (ProjectDefinition, FileId) – odczyt projektu to
     wyszukiwanie w indeksie po jego Project definition, niezależnie od tego, z jakiego pliku / źródła są dane (plik
     z wieloma projektami, projekt na cudzym imporcie).
   - CAN_BuildActualsBalance @FileId: zestawienie jednej wersji pliku (czyta tylko wiersze tego pliku – rowgroupy wsadu);
     wywołuje import przed zmianą stanu pliku na „utworzone”, w tej samej transakcji (plik widoczny razem z zestawieniem).
     Każdy import pisze tylko pod swoim FileId – równoległe importy się nie blokują. Plik innego parsera – bez zmian.
   - REP_ProjectCostsByElement, REP_ProjectCosts, RUN_PinBalance, CAN_ActualsCostElements – z zestawienia (najnowsze
     wersje plików – CAN_LatestFiles); CAN_LatestImport @Project (ACTUALS) – tylko pliki z Project definition projektu.
   - Zestawienie plików już zaimportowanych – liczone tutaj, plik po pliku.
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 037_zestawienie_actuals.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_ActualsBalance]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)CAN_ActualsBalance] (
    FileId              BIGINT          NOT NULL,
    ProjectDefinition   NVARCHAR(400)   NULL,
    WbsElement          NVARCHAR(400)   NULL,
    CostElement         NVARCHAR(400)   NULL,
    CostElementName     NVARCHAR(400)   NULL,
    PartnerObjectClass  NVARCHAR(400)   NULL,
    PartnerObject       NVARCHAR(400)   NULL,
    FiscalYear          INT             NULL,
    Period              INT             NULL,
    ObjectCurrency      NVARCHAR(400)   NULL,
    ReportCurrency      NVARCHAR(400)   NULL,
    TransactionCurrency NVARCHAR(400)   NULL,
    ValueObjCrcy        DECIMAL(28,8)   NULL,
    ValueRepCur         DECIMAL(28,8)   NULL,
    ValueTranCurr       DECIMAL(28,8)   NULL,
    TotalQuantity       DECIMAL(28,8)   NULL,
    Rows                INT             NOT NULL,
    LastCreatedOn       DATE            NULL,
    INDEX [IX_$(Prefix)CAN_ActualsBalance_Definition] CLUSTERED (ProjectDefinition, FileId)
) WITH (DATA_COMPRESSION = PAGE);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)CAN_ActualsBalance_File' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_ActualsBalance]'))
CREATE INDEX [IX_$(Prefix)CAN_ActualsBalance_File] ON [$(Schema)].[$(Prefix)CAN_ActualsBalance] (FileId)
    INCLUDE (FiscalYear, Period, CostElement, CostElementName, Rows, ValueObjCrcy, ValueRepCur, ValueTranCurr) WITH (DATA_COMPRESSION = PAGE);
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)CAN_BuildActualsBalance]
    @FileId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    -- Tylko pliki źródeł parsera ACTUALS (jak CAN_LatestFiles); inne – bez zestawienia.
    IF NOT EXISTS (
        SELECT 1 FROM [$(Schema)].[$(Prefix)META_SourceFile] f
        WHERE f.FileId = @FileId
          AND f.SourceCode IN (SELECT d.Code FROM [$(Schema)].[$(Prefix)META_SourceDefinition] d WHERE d.Parser = 'ACTUALS'))
        RETURN;
    DECLARE @parserId BIGINT, @fields NVARCHAR(MAX);
    SELECT @parserId = ParserId, @fields = Fields FROM [$(Schema)].[$(Prefix)META_Parser] WHERE SupersededAt IS NULL AND Code = 'ACTUALS';
    IF @parserId IS NULL
        THROW 50011, N'Nie ma parsera ACTUALS.', 1;

    DECLARE @slots TABLE (Field NVARCHAR(64) PRIMARY KEY, Slot VARCHAR(3));
    INSERT INTO @slots (Field, Slot)
    SELECT x.Field, x.Slot FROM OPENJSON(@fields) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
    WHERE x.Slot LIKE '[TLNID][0-9][0-9]';
    -- Pole bez slotu – NULL; kwoty i ilość – tylko sloty liczbowe.
    DECLARE @pd NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ProjectDefinition'), N'NULL'),
            @wbs NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'WbsElement'), N'NULL'),
            @ce NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElement'), N'NULL'),
            @name NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElementName'), N'NULL'),
            @poc NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'PartnerObjectClass'), N'NULL'),
            @po NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'PartnerObject'), N'NULL'),
            @year NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'FiscalYear'), N'NULL'),
            @period NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'Period'), N'NULL'),
            @ocur NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ObjectCurrency'), N'NULL'),
            @rcur NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ReportCurrency'), N'NULL'),
            @tcur NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'TransactionCurrency'), N'NULL'),
            @obj NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ValueObjCrcy' AND Slot LIKE 'N%'), N'NULL'),
            @rep NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ValueRepCur' AND Slot LIKE 'N%'), N'NULL'),
            @tran NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ValueTranCurr' AND Slot LIKE 'N%'), N'NULL'),
            @qty NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'TotalQuantity' AND Slot LIKE 'N%'), N'NULL'),
            @created NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CreatedOn'), N'NULL');

    DELETE FROM [$(Schema)].[$(Prefix)CAN_ActualsBalance] WHERE FileId = @FileId;
    DECLARE @sql NVARCHAR(MAX) = N'
        INSERT INTO [$(Schema)].[$(Prefix)CAN_ActualsBalance] (FileId, ProjectDefinition, WbsElement, CostElement, CostElementName,
            PartnerObjectClass, PartnerObject, FiscalYear, Period, ObjectCurrency, ReportCurrency, TransactionCurrency,
            ValueObjCrcy, ValueRepCur, ValueTranCurr, TotalQuantity, Rows, LastCreatedOn)
        SELECT @FileId, x.ProjectDefinition, x.WbsElement, x.CostElement, MAX(x.CostElementName), x.PartnerObjectClass, x.PartnerObject,
               x.FiscalYear, x.Period, x.ObjectCurrency, x.ReportCurrency, x.TransactionCurrency,
               SUM(x.ValueObjCrcy), SUM(x.ValueRepCur), SUM(x.ValueTranCurr), SUM(x.TotalQuantity), COUNT(*), MAX(x.CreatedOn)
        FROM (
            SELECT CAST(' + @pd + N' AS NVARCHAR(400)) AS ProjectDefinition, CAST(' + @wbs + N' AS NVARCHAR(400)) AS WbsElement,
                   CAST(' + @ce + N' AS NVARCHAR(400)) AS CostElement, CAST(' + @name + N' AS NVARCHAR(400)) AS CostElementName,
                   CAST(' + @poc + N' AS NVARCHAR(400)) AS PartnerObjectClass, CAST(' + @po + N' AS NVARCHAR(400)) AS PartnerObject,
                   TRY_CONVERT(INT, ' + @year + N') AS FiscalYear, TRY_CONVERT(INT, ' + @period + N') AS Period,
                   CAST(' + @ocur + N' AS NVARCHAR(400)) AS ObjectCurrency, CAST(' + @rcur + N' AS NVARCHAR(400)) AS ReportCurrency,
                   CAST(' + @tcur + N' AS NVARCHAR(400)) AS TransactionCurrency,
                   CAST(' + @obj + N' AS DECIMAL(28,8)) AS ValueObjCrcy, CAST(' + @rep + N' AS DECIMAL(28,8)) AS ValueRepCur,
                   CAST(' + @tran + N' AS DECIMAL(28,8)) AS ValueTranCurr, CAST(' + @qty + N' AS DECIMAL(28,8)) AS TotalQuantity,
                   TRY_CONVERT(DATE, ' + @created + N') AS CreatedOn
            FROM [$(Schema)].[$(Prefix)CAN_Row] r
            WHERE r.FileId = @FileId AND r.ParserId = @parserId) x
        GROUP BY x.ProjectDefinition, x.WbsElement, x.CostElement, x.PartnerObjectClass, x.PartnerObject, x.FiscalYear, x.Period,
                 x.ObjectCurrency, x.ReportCurrency, x.TransactionCurrency;';
    EXEC sp_executesql @sql, N'@FileId BIGINT, @parserId BIGINT', @FileId = @FileId, @parserId = @parserId;
END;
GO

/* ---------- Odczyty projektu z zestawienia ---------- */

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)REP_ProjectCostsByElement]
    @Project VARCHAR(20),
    @Value   NVARCHAR(64) = N'ValueObjCrcy',  -- pole kwoty ACTUALS (ValueObjCrcy – waluta obiektu, ValueRepCur, ValueTranCurr)
    @Hours   NVARCHAR(64) = NULL              -- pole godzin (TotalQuantity – Actual DLH); NULL – kolumna Hours pusta
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @message NVARCHAR(400);
    IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Project] WHERE Code = @Project AND SupersededAt IS NULL)
    BEGIN
        SET @message = N'Nie ma projektu ' + ISNULL(@Project, N'(brak)') + N'.';
        THROW 50013, @message, 1;
    END;
    IF @Value IS NULL OR @Value NOT IN (N'ValueObjCrcy', N'ValueRepCur', N'ValueTranCurr')
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (np. ValueObjCrcy, ValueRepCur, ValueTranCurr).';
        THROW 50014, @message, 1;
    END;

    -- Project definition projektu: Project definition i WBS element węzłów nakładki.
    CREATE TABLE #definitions (ProjectDefinition NVARCHAR(400) NOT NULL PRIMARY KEY);
    INSERT INTO #definitions (ProjectDefinition)
    SELECT DISTINCT v.Code
    FROM [$(Schema)].[$(Prefix)META_PerformanceObjective] po
    CROSS APPLY (VALUES (po.ProjectDefinition), (po.WbsElement)) v(Code)
    WHERE po.Project = @Project AND po.SupersededAt IS NULL AND v.Code IS NOT NULL;

    -- Cost elementy rozliczeniowe – słownik efektywny projektu (zmiana projektu przed globalnym).
    CREATE TABLE #settlement (CostElement NVARCHAR(400) NOT NULL PRIMARY KEY);
    INSERT INTO #settlement (CostElement)
    SELECT c.CostElement
    FROM (SELECT CostElement, IsSettlement,
                 ROW_NUMBER() OVER (PARTITION BY CostElement ORDER BY CASE WHEN Project IS NULL THEN 1 ELSE 0 END) AS rn
          FROM [$(Schema)].[$(Prefix)DICT_CostCategory]
          WHERE SupersededAt IS NULL AND (Project = @Project OR Project IS NULL)) c
    WHERE c.rn = 1 AND c.IsSettlement = 1;

    SELECT ISNULL(b.WbsElement, b.ProjectDefinition) AS [WbsElement],
           SUM(CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur WHEN N'ValueTranCurr' THEN b.ValueTranCurr ELSE b.ValueObjCrcy END) AS [Amount],
           SUM(b.Rows) AS [Rows],
           SUM(CASE WHEN @Hours = N'TotalQuantity' THEN b.TotalQuantity END) AS [Hours]
    FROM #definitions d
    JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.ProjectDefinition = d.ProjectDefinition
    JOIN [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f ON f.FileId = b.FileId
    WHERE NOT (b.CostElement IN (SELECT s.CostElement FROM #settlement s) AND ISNULL(b.PartnerObjectClass, N'') <> N'Profit analysis')
      AND NOT EXISTS (
          SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_Exclusion] x
          WHERE x.Project = @Project AND x.SupersededAt IS NULL
            AND (x.CostElement IS NULL OR x.CostElement = b.CostElement)
            AND (x.WbsElement IS NULL OR x.WbsElement = b.WbsElement)
            AND (x.PartnerObject IS NULL OR x.PartnerObject = b.PartnerObject))
    GROUP BY ISNULL(b.WbsElement, b.ProjectDefinition)
    ORDER BY 1;
END;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)REP_ProjectCosts]
    @Project VARCHAR(20),
    @Value   NVARCHAR(64) = N'ValueObjCrcy'   -- pole kwoty ACTUALS (domyślnie waluta obiektu – PLN)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @message NVARCHAR(400);
    IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Project] WHERE Code = @Project AND SupersededAt IS NULL)
    BEGIN
        SET @message = N'Nie ma projektu ' + ISNULL(@Project, N'(brak)') + N'.';
        THROW 50013, @message, 1;
    END;
    IF @Value IS NULL OR @Value NOT IN (N'ValueObjCrcy', N'ValueRepCur', N'ValueTranCurr')
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (np. ValueObjCrcy, ValueRepCur, ValueTranCurr).';
        THROW 50014, @message, 1;
    END;

    -- Project definition projektu: opis (nazwa elementu = Project definition) i grupa (nazwa korzenia nakładki nad nim).
    CREATE TABLE #definitions (ProjectDefinition NVARCHAR(400) NOT NULL PRIMARY KEY, Description NVARCHAR(400) NULL, Grouping NVARCHAR(400) NULL);
    WITH po AS (
        SELECT NodeId, ParentNodeId, NodeLevel, SortOrder, Name, ProjectDefinition, WbsElement
        FROM [$(Schema)].[$(Prefix)META_PerformanceObjective] WHERE Project = @Project AND SupersededAt IS NULL),
    tree AS (
        SELECT NodeId, Name AS RootName, 0 AS Depth FROM po WHERE ParentNodeId IS NULL
        UNION ALL
        SELECT c.NodeId, t.RootName, t.Depth + 1 FROM po c JOIN tree t ON c.ParentNodeId = t.NodeId),
    ranked AS (
        SELECT v.Code AS ProjectDefinition, po.Name, t.RootName,
               ROW_NUMBER() OVER (PARTITION BY v.Code
                                  ORDER BY CASE WHEN po.WbsElement = v.Code THEN 0 ELSE 1 END, t.Depth, po.SortOrder) AS rn
        FROM po JOIN tree t ON t.NodeId = po.NodeId
        CROSS APPLY (VALUES (po.ProjectDefinition), (po.WbsElement)) v(Code)
        WHERE v.Code IS NOT NULL)
    INSERT INTO #definitions (ProjectDefinition, Description, Grouping)
    SELECT ProjectDefinition, Name, RootName FROM ranked WHERE rn = 1
    OPTION (MAXRECURSION 200);

    -- Ostatni okres w danych: wszystkie najnowsze pliki ACTUALS (indeks zestawienia po pliku).
    DECLARE @last INT = (
        SELECT MAX(b.FiscalYear * 100 + b.Period)
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f
        JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.FileId = f.FileId);

    -- Sumy ostatniego importu: Project definition × Cost Element × rok × okres, bez wykluczeń projektu.
    CREATE TABLE #sums (ProjectDefinition NVARCHAR(400) NOT NULL, CostElement NVARCHAR(400) NOT NULL, FiscalYear INT NULL, Period INT NULL,
                        Amount DECIMAL(28,8) NULL, CostElementName NVARCHAR(400) NULL);
    INSERT INTO #sums (ProjectDefinition, CostElement, FiscalYear, Period, Amount, CostElementName)
    SELECT b.ProjectDefinition, b.CostElement, b.FiscalYear, b.Period,
           SUM(CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur WHEN N'ValueTranCurr' THEN b.ValueTranCurr ELSE b.ValueObjCrcy END),
           MAX(b.CostElementName)
    FROM #definitions d
    JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.ProjectDefinition = d.ProjectDefinition
    JOIN [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f ON f.FileId = b.FileId
    WHERE b.CostElement IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_Exclusion] x
          WHERE x.Project = @Project AND x.SupersededAt IS NULL
            AND (x.CostElement IS NULL OR x.CostElement = b.CostElement)
            AND (x.WbsElement IS NULL OR x.WbsElement = b.WbsElement)
            AND (x.PartnerObject IS NULL OR x.PartnerObject = b.PartnerObject))
    GROUP BY b.ProjectDefinition, b.CostElement, b.FiscalYear, b.Period;

    -- Kolumny: ostatni okres w danych, potem lata projektu (od najnowszego).
    DECLARE @lastYear INT = @last / 100, @lastPeriod INT = @last % 100;
    DECLARE @columns NVARCHAR(MAX) = N'';
    IF @last IS NOT NULL
        SET @columns = N', SUM(CASE WHEN s.FiscalYear = ' + CAST(@lastYear AS NVARCHAR(4)) + N' AND s.Period = ' + CAST(@lastPeriod AS NVARCHAR(3))
            + N' THEN s.Amount END) AS ' + QUOTENAME(N'Period ' + RIGHT(N'0' + CAST(@lastPeriod AS NVARCHAR(3)), 2) + N'/' + CAST(@lastYear AS NVARCHAR(4)));
    SELECT @columns += N', SUM(CASE WHEN s.FiscalYear = ' + CAST(y.FiscalYear AS NVARCHAR(4)) + N' THEN s.Amount END) AS ' + QUOTENAME(CAST(y.FiscalYear AS NVARCHAR(4)))
    FROM (SELECT DISTINCT FiscalYear FROM #sums WHERE FiscalYear IS NOT NULL) y
    ORDER BY y.FiscalYear DESC;

    -- Opis i Cost Category: słownik efektywny projektu (zmiany projektu przed globalnym); opis bez wpisu – z danych.
    DECLARE @sql NVARCHAR(MAX) = N'
        SELECT d.Grouping AS [Grouping], s.ProjectDefinition AS [Project definition], d.Description AS [Project definition description],
               s.CostElement AS [Cost Element], COALESCE(MAX(p.Description), MAX(g.Description), MAX(s.CostElementName)) AS [Cost Elem. Descr.],
               COALESCE(MAX(p.CostCategory), MAX(g.CostCategory)) AS [Cost grouping]' + @columns + N'
        FROM #sums s
        JOIN #definitions d ON d.ProjectDefinition = s.ProjectDefinition
        LEFT JOIN [$(Schema)].[$(Prefix)DICT_CostCategory] p ON p.Project = @Project AND p.SupersededAt IS NULL AND p.CostElement = s.CostElement
        LEFT JOIN [$(Schema)].[$(Prefix)DICT_CostCategory] g ON g.Project IS NULL AND g.SupersededAt IS NULL AND g.CostElement = s.CostElement
        GROUP BY d.Grouping, s.ProjectDefinition, d.Description, s.CostElement
        ORDER BY d.Grouping, s.ProjectDefinition, s.CostElement;';
    EXEC sp_executesql @sql, N'@Project VARCHAR(20)', @Project = @Project;
END;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)RUN_PinBalance]
    @Project VARCHAR(20),
    @PinId   BIGINT,
    @Value   NVARCHAR(64) = N'ValueObjCrcy'   -- pole kwoty ACTUALS (słownik „Parametry projektu”)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @message NVARCHAR(400);
    IF @Value IS NULL OR @Value NOT IN (N'ValueObjCrcy', N'ValueRepCur', N'ValueTranCurr')
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (np. ValueObjCrcy, ValueRepCur, ValueTranCurr).';
        THROW 50014, @message, 1;
    END;

    CREATE TABLE #definitions (ProjectDefinition NVARCHAR(400) NOT NULL PRIMARY KEY);
    INSERT INTO #definitions (ProjectDefinition)
    SELECT DISTINCT v.Code
    FROM [$(Schema)].[$(Prefix)META_PerformanceObjective] po
    CROSS APPLY (VALUES (po.ProjectDefinition), (po.WbsElement)) v(Code)
    WHERE po.Project = @Project AND po.SupersededAt IS NULL AND v.Code IS NOT NULL;

    CREATE TABLE #settlement (CostElement NVARCHAR(400) NOT NULL PRIMARY KEY);
    INSERT INTO #settlement (CostElement)
    SELECT c.CostElement
    FROM (SELECT CostElement, IsSettlement,
                 ROW_NUMBER() OVER (PARTITION BY CostElement ORDER BY CASE WHEN Project IS NULL THEN 1 ELSE 0 END) AS rn
          FROM [$(Schema)].[$(Prefix)DICT_CostCategory]
          WHERE SupersededAt IS NULL AND (Project = @Project OR Project IS NULL)) c
    WHERE c.rn = 1 AND c.IsSettlement = 1;

    DELETE FROM [$(Schema)].[$(Prefix)RUN_Balance] WHERE PinId = @PinId;
    UPDATE [$(Schema)].[$(Prefix)META_RunPin] SET AmountField = @Value WHERE PinId = @PinId;
    -- Waluta – pole waluty kwoty @Value.
    INSERT INTO [$(Schema)].[$(Prefix)RUN_Balance] (PinId, WbsElement, CostElement, FiscalYear, Period, IsSettlement, Currency, Rows, Amount, LastCreatedOn)
    SELECT @PinId, x.WbsElement, x.CostElement, x.FiscalYear, x.Period, x.IsSettlement, x.Currency, SUM(x.Rows), SUM(x.Amount), MAX(x.CreatedOn)
    FROM (
        SELECT ISNULL(b.WbsElement, N'') AS WbsElement, ISNULL(b.CostElement, N'') AS CostElement,
               ISNULL(b.FiscalYear, 0) AS FiscalYear, ISNULL(b.Period, 0) AS Period,
               CAST(CASE WHEN b.CostElement IN (SELECT s.CostElement FROM #settlement s) AND ISNULL(b.PartnerObjectClass, N'') <> N'Profit analysis'
                         THEN 1 ELSE 0 END AS BIT) AS IsSettlement,
               ISNULL(CASE @Value WHEN N'ValueRepCur' THEN b.ReportCurrency WHEN N'ValueTranCurr' THEN b.TransactionCurrency ELSE b.ObjectCurrency END, N'')
                   AS Currency,
               ISNULL(CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur WHEN N'ValueTranCurr' THEN b.ValueTranCurr ELSE b.ValueObjCrcy END, 0) AS Amount,
               b.Rows, b.LastCreatedOn AS CreatedOn
        FROM #definitions d
        JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.ProjectDefinition = d.ProjectDefinition
        JOIN [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f ON f.FileId = b.FileId) x
    GROUP BY x.WbsElement, x.CostElement, x.FiscalYear, x.Period, x.IsSettlement, x.Currency;
END;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)CAN_ActualsCostElements]
    @Value NVARCHAR(64) = N'ValueObjCrcy'   -- pole kwoty ACTUALS (suma – informacyjnie)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.CostElement AS [CostElement], MAX(b.CostElementName) AS [Name], SUM(b.Rows) AS [Rows],
           SUM(CASE @Value WHEN N'ValueObjCrcy' THEN b.ValueObjCrcy WHEN N'ValueRepCur' THEN b.ValueRepCur
                           WHEN N'ValueTranCurr' THEN b.ValueTranCurr END) AS [Amount]
    FROM [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f
    JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.FileId = f.FileId
    WHERE b.CostElement IS NOT NULL
    GROUP BY b.CostElement
    ORDER BY 1;
END;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)CAN_LatestImport]
    @Parser     VARCHAR(30),
    @SourceCode VARCHAR(60)       = NULL,
    @AsOf       DATETIMEOFFSET(7) = NULL,
    @Project    VARCHAR(20)       = NULL   -- projekt PZL-EV: tylko Project definition z jego nakładki
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @parserId BIGINT, @fields NVARCHAR(MAX), @message NVARCHAR(400);
    SELECT @parserId = ParserId, @fields = Fields
    FROM [$(Schema)].[$(Prefix)META_Parser]
    WHERE SupersededAt IS NULL AND Code = @Parser;
    IF @parserId IS NULL
    BEGIN
        SET @message = N'Nie ma parsera ' + ISNULL(@Parser, N'(brak)') + N'.';
        THROW 50011, @message, 1;
    END;

    -- Projekt PZL-EV: Project definition elementów jego nakładki (bieżące wersje); filtr po polu ProjectDefinition parsera.
    DECLARE @projectSlot VARCHAR(3), @files NVARCHAR(MAX);
    CREATE TABLE #definitions (ProjectDefinition NVARCHAR(400) NOT NULL PRIMARY KEY);
    IF @Project IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Project] WHERE Code = @Project AND SupersededAt IS NULL)
        BEGIN
            SET @message = N'Nie ma projektu ' + @Project + N'.';
            THROW 50013, @message, 1;
        END;
        SELECT @projectSlot = x.Slot
        FROM OPENJSON(@fields) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
        WHERE x.Field = N'ProjectDefinition' AND x.Slot LIKE '[TL][0-9][0-9]';
        IF @projectSlot IS NULL
        BEGIN
            SET @message = N'Parser ' + @Parser + N' nie ma pola ProjectDefinition – nie można filtrować po projekcie.';
            THROW 50012, @message, 1;
        END;
        INSERT INTO #definitions (ProjectDefinition)
        SELECT DISTINCT v.Code
        FROM [$(Schema)].[$(Prefix)META_PerformanceObjective] po
        CROSS APPLY (VALUES (po.ProjectDefinition), (po.WbsElement)) v(Code)
        WHERE po.Project = @Project AND po.SupersededAt IS NULL AND v.Code IS NOT NULL;
        -- ACTUALS: tylko najnowsze pliki z Project definition projektu (zestawienie) – lista stała w zapytaniu, serwer
        -- czyta tylko rowgroupy tych plików (bez pozostałych plików i starszych wersji).
        IF @Parser = 'ACTUALS'
            SET @files = ISNULL((
                SELECT STRING_AGG(CAST(f.FileId AS NVARCHAR(MAX)), N',')
                FROM [$(Schema)].[$(Prefix)CAN_LatestFiles](@Parser, @SourceCode, @AsOf) f
                WHERE EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)CAN_ActualsBalance] b
                              JOIN #definitions d ON d.ProjectDefinition = b.ProjectDefinition
                              WHERE b.FileId = f.FileId)), N'-1');
    END;

    -- Pola bieżącej wersji parsera ze slotem, w kolejności parsera: r.<slot> AS [<pole>]
    DECLARE @list NVARCHAR(MAX) = (
        SELECT N', r.' + QUOTENAME(x.Slot) + N' AS ' + QUOTENAME(x.Field)
        FROM OPENJSON(@fields) j
        CROSS APPLY OPENJSON(j.value) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
        WHERE x.Slot LIKE '[TLNID][0-9][0-9]' AND COL_LENGTH(N'[$(Schema)].[$(Prefix)CAN_Row]', x.Slot) IS NOT NULL
        ORDER BY CAST(j.[key] AS INT)
        FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)');

    DECLARE @sql NVARCHAR(MAX) = N'
        SELECT f.SourceCode, f.Location, f.FileName, f.ModifiedAt, f.ImportedAt, f.BatchId, r.FileId, r.RowNumber, r.ParserVersion'
        + ISNULL(@list, N'') + N'
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles](@Parser, @SourceCode, @AsOf) f
        JOIN [$(Schema)].[$(Prefix)CAN_Row] r ON r.FileId = f.FileId AND r.ParserId = @parserId'
        + CASE WHEN @projectSlot IS NULL THEN N'' ELSE N' WHERE r.' + QUOTENAME(@projectSlot) + N' IN (SELECT d.ProjectDefinition FROM #definitions d)' END
        + CASE WHEN @files IS NULL THEN N'' ELSE N' AND r.FileId IN (' + @files + N')' END + N';';

    EXEC sp_executesql @sql,
        N'@Parser VARCHAR(30), @SourceCode VARCHAR(60), @AsOf DATETIMEOFFSET(7), @parserId BIGINT',
        @Parser = @Parser, @SourceCode = @SourceCode, @AsOf = @AsOf, @parserId = @parserId;
END;
GO

/* ---------- Zestawienie plików już zaimportowanych ---------- */

DECLARE @file BIGINT = (
    SELECT MIN(f.FileId) FROM [$(Schema)].[$(Prefix)META_SourceFile] f
    WHERE f.CanonicalStatus = N'utworzone'
      AND f.SourceCode IN (SELECT d.Code FROM [$(Schema)].[$(Prefix)META_SourceDefinition] d WHERE d.Parser = 'ACTUALS')
      AND NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)CAN_ActualsBalance] b WHERE b.FileId = f.FileId));
WHILE @file IS NOT NULL
BEGIN
    EXEC [$(Schema)].[$(Prefix)CAN_BuildActualsBalance] @FileId = @file;
    SET @file = (
        SELECT MIN(f.FileId) FROM [$(Schema)].[$(Prefix)META_SourceFile] f
        WHERE f.FileId > @file AND f.CanonicalStatus = N'utworzone'
          AND f.SourceCode IN (SELECT d.Code FROM [$(Schema)].[$(Prefix)META_SourceDefinition] d WHERE d.Parser = 'ACTUALS')
          AND NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)CAN_ActualsBalance] b WHERE b.FileId = f.FileId));
END;
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pzl_ev_user' AND type = 'R')
BEGIN
    GRANT SELECT, INSERT, DELETE ON [$(Schema)].[$(Prefix)CAN_ActualsBalance] TO [pzl_ev_user];
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)CAN_BuildActualsBalance] TO [pzl_ev_user];
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)REP_ProjectCostsByElement] TO [pzl_ev_user];
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)REP_ProjectCosts] TO [pzl_ev_user];
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)RUN_PinBalance] TO [pzl_ev_user];
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)CAN_ActualsCostElements] TO [pzl_ev_user];
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)CAN_LatestImport] TO [pzl_ev_user];
END;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 37)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (37, N'037_zestawienie_actuals.sql', '0.51.0');
GO
