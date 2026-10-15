/* PZL-EV – migracja 042: zestaw walutowy ACTUALS – tylko Object / Report currency (docs/slowniki.md, „Parametry projektu”).
   Waluta transakcji (Value TranCurr) nie jest zestawem – kwoty w różnych walutach się nie sumują (decyzja finansów):
   REP_ProjectCostsByElement, REP_ProjectCosts, RUN_PinBalance nie przyjmują pola ValueTranCurr (THROW 50014), a
   CAN_ActualsCostElements nie sumuje go. Zestawienie CAN_ActualsBalance zachowuje kolumnę ValueTranCurr (dane źródła).
   „Parametry projektu” z wartością ValueTranCurr – ValueObjCrcy (zestaw domyślny; nowa wersja wiersza, historia zostaje).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 042_bez_waluty_transakcji.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)REP_ProjectCostsByElement]
    @Project VARCHAR(20),
    @Value   NVARCHAR(64) = N'ValueObjCrcy',  -- pole kwoty ACTUALS (ValueObjCrcy – waluta obiektu, ValueRepCur – waluta raportu)
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
    IF @Value IS NULL OR @Value NOT IN (N'ValueObjCrcy', N'ValueRepCur')
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (ValueObjCrcy albo ValueRepCur; waluta transakcji nie jest zestawem walutowym).';
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

    -- Rodzaj kosztu (Hours / Materials / Other) – słownik efektywny projektu; bez rodzaju – ACWP Other.
    CREATE TABLE #kind (CostElement NVARCHAR(400) NOT NULL PRIMARY KEY, CostKind NVARCHAR(20) NOT NULL);
    INSERT INTO #kind (CostElement, CostKind)
    SELECT c.CostElement, c.CostKind
    FROM (SELECT CostElement, CostKind,
                 ROW_NUMBER() OVER (PARTITION BY CostElement ORDER BY CASE WHEN Project IS NULL THEN 1 ELSE 0 END) AS rn
          FROM [$(Schema)].[$(Prefix)DICT_CostCategory]
          WHERE SupersededAt IS NULL AND (Project = @Project OR Project IS NULL)) c
    WHERE c.rn = 1 AND c.CostKind IS NOT NULL;

    SELECT ISNULL(b.WbsElement, b.ProjectDefinition) AS [WbsElement],
           SUM(CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur ELSE b.ValueObjCrcy END) AS [Amount],
           SUM(b.Rows) AS [Rows],
           SUM(CASE WHEN @Hours = N'TotalQuantity' THEN b.TotalQuantity END) AS [Hours],
           SUM(CASE WHEN k.CostKind = N'Hours'
                    THEN CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur ELSE b.ValueObjCrcy END END) AS [Labor],
           SUM(CASE WHEN k.CostKind = N'Materials'
                    THEN CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur ELSE b.ValueObjCrcy END END) AS [Material]
    FROM #definitions d
    JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.ProjectDefinition = d.ProjectDefinition
    JOIN [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f ON f.FileId = b.FileId
    LEFT JOIN #kind k ON k.CostElement = b.CostElement
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
    IF @Value IS NULL OR @Value NOT IN (N'ValueObjCrcy', N'ValueRepCur')
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (ValueObjCrcy albo ValueRepCur; waluta transakcji nie jest zestawem walutowym).';
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
           SUM(CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur ELSE b.ValueObjCrcy END),
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
    IF @Value IS NULL OR @Value NOT IN (N'ValueObjCrcy', N'ValueRepCur')
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (ValueObjCrcy albo ValueRepCur; waluta transakcji nie jest zestawem walutowym).';
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
               ISNULL(CASE @Value WHEN N'ValueRepCur' THEN b.ReportCurrency ELSE b.ObjectCurrency END, N'')
                   AS Currency,
               ISNULL(CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur ELSE b.ValueObjCrcy END, 0) AS Amount,
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
                           END) AS [Amount]
    FROM [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f
    JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.FileId = f.FileId
    WHERE b.CostElement IS NOT NULL
    GROUP BY b.CostElement
    ORDER BY 1;
END;
GO

/* „Parametry projektu”: Pole kwoty ACTUALS = ValueTranCurr → ValueObjCrcy (nowa wersja wiersza, historia zostaje). */
DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();
DECLARE @current TABLE (RowId BIGINT PRIMARY KEY, Version INT NOT NULL, Project VARCHAR(20) NOT NULL, Name NVARCHAR(100) NOT NULL,
                        Description NVARCHAR(400) NULL);
UPDATE s SET SupersededAt = @at, SupersededBy = @by
OUTPUT deleted.RowId, deleted.Version, deleted.Project, deleted.Name, deleted.Description
INTO @current (RowId, Version, Project, Name, Description)
FROM [$(Schema)].[$(Prefix)DICT_ProjectSetting] s
WHERE s.SupersededAt IS NULL AND s.Name = N'Pole kwoty ACTUALS' AND s.Value = N'ValueTranCurr';

INSERT INTO [$(Schema)].[$(Prefix)DICT_ProjectSetting] (RowId, Version, Project, Name, Value, Description, RecordedAt, RecordedBy)
SELECT RowId, Version + 1, Project, Name, N'ValueObjCrcy', Description, @at, @by
FROM @current;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 42)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (42, N'042_bez_waluty_transakcji.sql', '0.54.0');
GO
