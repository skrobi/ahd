/* PZL-EV – migracja 013: raport kosztów projektu (docs/model-danych.md, rozdz. 5).
   CAN_LatestFiles – najnowsza wersja każdego pliku parsera (wspólne dla CAN_LatestImport i raportów).
   REP_ProjectCosts @Project, @Value – koszty rzeczywiste ostatniego importu ACTUALS projektu PZL-EV: wiersz =
   Project definition z nakładki × Cost Element (tylko elementy z kosztami), bez wykluczeń projektu; kolumny: Grouping
   (nazwa korzenia nakładki nad Project definition), Project definition, opis (nazwa elementu = Project definition),
   Cost Element, opis i Cost grouping ze słownika Cost Category (zmiany projektu przed globalnym), kwota ostatniego
   okresu w danych (Period MM/RRRR) i kwoty lat projektu (od najnowszego). Kwota: pole @Value (domyślnie
   ValueObjCrcy – waluta obiektu, PLN).

       EXEC [FINOP].[PZLEV_REP_ProjectCosts] @Project = 'M28';
       EXEC [FINOP].[PZLEV_REP_ProjectCosts] @Project = 'M28', @Value = 'ValueRepCur';

   Uprawnienia jak w migracji 011 (prawa wywołującego). Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) – w jednej transakcji; ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 013_raport_kosztow_projektu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER FUNCTION [$(Schema)].[$(Prefix)CAN_LatestFiles] (@Parser VARCHAR(30), @SourceCode VARCHAR(60), @AsOf DATETIMEOFFSET(7))
RETURNS TABLE
AS
RETURN
    -- Najnowsza wersja każdego pliku (lokalizacja + nazwa) z danymi kanonicznymi parsera, zaimportowana do @AsOf.
    SELECT x.FileId, x.SourceCode, x.Location, x.FileName, x.ModifiedAt, x.ImportedAt, x.BatchId
    FROM (
        SELECT f.FileId, f.SourceCode, f.Location, f.FileName, f.ModifiedAt, f.ImportedAt, f.BatchId,
               ROW_NUMBER() OVER (PARTITION BY f.Location, f.FileName ORDER BY f.ModifiedAt DESC, f.ImportedAt DESC, f.FileId DESC) AS rn
        FROM [$(Schema)].[$(Prefix)META_SourceFile] f
        WHERE f.CanonicalStatus = N'utworzone'
          AND (@AsOf IS NULL OR f.ImportedAt <= @AsOf)
          AND (@SourceCode IS NULL OR f.SourceCode = @SourceCode)
          AND f.SourceCode IN (SELECT d.Code FROM [$(Schema)].[$(Prefix)META_SourceDefinition] d WHERE d.Parser = @Parser)
    ) x
    WHERE x.rn = 1;
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
    DECLARE @projectSlot VARCHAR(3);
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
        SELECT DISTINCT ProjectDefinition
        FROM [$(Schema)].[$(Prefix)META_PerformanceObjective]
        WHERE Project = @Project AND SupersededAt IS NULL AND ProjectDefinition IS NOT NULL;
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
        + CASE WHEN @projectSlot IS NULL THEN N'' ELSE N' WHERE r.' + QUOTENAME(@projectSlot) + N' IN (SELECT d.ProjectDefinition FROM #definitions d)' END + N';';

    EXEC sp_executesql @sql,
        N'@Parser VARCHAR(30), @SourceCode VARCHAR(60), @AsOf DATETIMEOFFSET(7), @parserId BIGINT',
        @Parser = @Parser, @SourceCode = @SourceCode, @AsOf = @AsOf, @parserId = @parserId;
END;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)REP_ProjectCosts]
    @Project VARCHAR(20),
    @Value   NVARCHAR(64) = N'ValueObjCrcy'   -- pole kwoty parsera ACTUALS (domyślnie waluta obiektu – PLN)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @message NVARCHAR(400), @parserId BIGINT, @fields NVARCHAR(MAX);

    IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Project] WHERE Code = @Project AND SupersededAt IS NULL)
    BEGIN
        SET @message = N'Nie ma projektu ' + ISNULL(@Project, N'(brak)') + N'.';
        THROW 50013, @message, 1;
    END;
    SELECT @parserId = ParserId, @fields = Fields FROM [$(Schema)].[$(Prefix)META_Parser] WHERE SupersededAt IS NULL AND Code = 'ACTUALS';
    IF @parserId IS NULL
        THROW 50011, N'Nie ma parsera ACTUALS.', 1;

    -- Sloty pól ACTUALS (pole bez slotu – NULL w zapytaniu).
    DECLARE @slots TABLE (Field NVARCHAR(64) PRIMARY KEY, Slot VARCHAR(3));
    INSERT INTO @slots (Field, Slot)
    SELECT x.Field, x.Slot FROM OPENJSON(@fields) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
    WHERE x.Slot LIKE '[TLNID][0-9][0-9]';
    DECLARE @pd NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ProjectDefinition'),
            @ce NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElement'),
            @yr NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'FiscalYear'),
            @pe NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'Period'),
            @val NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = @Value AND Slot LIKE 'N%'),
            @wbs NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'WbsElement'), N'NULL'),
            @po NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'PartnerObject'), N'NULL'),
            @name NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElementName'), N'NULL');
    IF @val IS NULL
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (np. ValueObjCrcy, ValueRepCur, ValueTranCurr).';
        THROW 50014, @message, 1;
    END;
    IF @pd IS NULL OR @ce IS NULL OR @yr IS NULL OR @pe IS NULL
        THROW 50012, N'Parser ACTUALS musi mieć pola ProjectDefinition, CostElement, FiscalYear i Period.', 1;

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
        SELECT po.ProjectDefinition, po.Name, t.RootName,
               ROW_NUMBER() OVER (PARTITION BY po.ProjectDefinition
                                  ORDER BY CASE WHEN po.WbsElement = po.ProjectDefinition THEN 0 ELSE 1 END, t.Depth, po.SortOrder) AS rn
        FROM po JOIN tree t ON t.NodeId = po.NodeId
        WHERE po.ProjectDefinition IS NOT NULL)
    INSERT INTO #definitions (ProjectDefinition, Description, Grouping)
    SELECT ProjectDefinition, Name, RootName FROM ranked WHERE rn = 1
    OPTION (MAXRECURSION 200);

    -- Sumy ostatniego importu: Project definition × Cost Element × rok × okres, bez wykluczeń projektu.
    CREATE TABLE #sums (ProjectDefinition NVARCHAR(400) NOT NULL, CostElement NVARCHAR(400) NOT NULL, FiscalYear INT NULL, Period INT NULL,
                        Amount DECIMAL(28,8) NULL, CostElementName NVARCHAR(400) NULL);
    DECLARE @last INT;
    DECLARE @sql NVARCHAR(MAX) = N'
        SELECT @last = MAX(' + @yr + N' * 100 + ' + @pe + N')
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles](''ACTUALS'', NULL, NULL) f
        JOIN [$(Schema)].[$(Prefix)CAN_Row] r ON r.FileId = f.FileId AND r.ParserId = @parserId;
        INSERT INTO #sums (ProjectDefinition, CostElement, FiscalYear, Period, Amount, CostElementName)
        SELECT ' + @pd + N', ' + @ce + N', ' + @yr + N', ' + @pe + N', SUM(' + @val + N'), MAX(' + @name + N')
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles](''ACTUALS'', NULL, NULL) f
        JOIN [$(Schema)].[$(Prefix)CAN_Row] r ON r.FileId = f.FileId AND r.ParserId = @parserId
        WHERE ' + @pd + N' IN (SELECT d.ProjectDefinition FROM #definitions d) AND ' + @ce + N' IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_Exclusion] x
              WHERE x.Project = @Project AND x.SupersededAt IS NULL
                AND (x.CostElement IS NULL OR x.CostElement = ' + @ce + N')
                AND (x.WbsElement IS NULL OR x.WbsElement = ' + @wbs + N')
                AND (x.PartnerObject IS NULL OR x.PartnerObject = ' + @po + N'))
        GROUP BY ' + @pd + N', ' + @ce + N', ' + @yr + N', ' + @pe + N';';
    EXEC sp_executesql @sql, N'@parserId BIGINT, @Project VARCHAR(20), @last INT OUTPUT', @parserId = @parserId, @Project = @Project, @last = @last OUTPUT;

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
    SET @sql = N'
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

IF DATABASE_PRINCIPAL_ID(N'pzl_ev_user') IS NOT NULL
BEGIN
    GRANT SELECT ON [$(Schema)].[$(Prefix)CAN_LatestFiles] TO [pzl_ev_user];
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)REP_ProjectCosts] TO [pzl_ev_user];
END;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 13)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (13, N'013_raport_kosztow_projektu.sql', '0.23.0');
GO
