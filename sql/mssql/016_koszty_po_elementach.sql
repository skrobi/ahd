/* PZL-EV – migracja 016: koszt rzeczywisty (ACWP) projektu po elemencie CES (docs/performance-objectives.md, rozdz. 4.2).
   REP_ProjectCostsByElement @Project, @Value – suma kwoty z ostatniego importu ACTUALS (najnowsza wersja każdego
   pliku – CAN_LatestFiles) po WBS elemencie CES, dla Project definition projektu (jak REP_ProjectCosts: Project
   definition i WBS element węzłów nakładki), bez wykluczeń projektu. Zrzut ACTUALS zawsze zawiera całość, więc suma
   po wszystkich okresach to koszt narastająco. Wiersz bez WBS elementu – pod Project definition. Tabela struktury
   przypisuje koszt elementu do WP (StructureBuilder). Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) – w jednej transakcji; ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 016_koszty_po_elementach.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)REP_ProjectCostsByElement]
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

    DECLARE @slots TABLE (Field NVARCHAR(64) PRIMARY KEY, Slot VARCHAR(3));
    INSERT INTO @slots (Field, Slot)
    SELECT x.Field, x.Slot FROM OPENJSON(@fields) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
    WHERE x.Slot LIKE '[TLNID][0-9][0-9]';
    DECLARE @pd NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ProjectDefinition'),
            @ce NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElement'), N'NULL'),
            @val NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = @Value AND Slot LIKE 'N%'),
            @wbs NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'WbsElement'), N'NULL'),
            @po NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'PartnerObject'), N'NULL');
    IF @val IS NULL
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (np. ValueObjCrcy, ValueRepCur, ValueTranCurr).';
        THROW 50014, @message, 1;
    END;
    IF @pd IS NULL
        THROW 50012, N'Parser ACTUALS musi mieć pole ProjectDefinition.', 1;

    -- Project definition projektu: Project definition i WBS element węzłów nakładki (jak REP_ProjectCosts, migracja 014).
    CREATE TABLE #definitions (ProjectDefinition NVARCHAR(400) NOT NULL PRIMARY KEY);
    INSERT INTO #definitions (ProjectDefinition)
    SELECT DISTINCT v.Code
    FROM [$(Schema)].[$(Prefix)META_PerformanceObjective] po
    CROSS APPLY (VALUES (po.ProjectDefinition), (po.WbsElement)) v(Code)
    WHERE po.Project = @Project AND po.SupersededAt IS NULL AND v.Code IS NOT NULL;

    DECLARE @sql NVARCHAR(MAX) = N'
        SELECT ISNULL(' + @wbs + N', ' + @pd + N') AS [WbsElement], SUM(' + @val + N') AS [Amount], COUNT(*) AS [Rows]
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles](''ACTUALS'', NULL, NULL) f
        JOIN [$(Schema)].[$(Prefix)CAN_Row] r ON r.FileId = f.FileId AND r.ParserId = @parserId
        WHERE ' + @pd + N' IN (SELECT d.ProjectDefinition FROM #definitions d)
          AND NOT EXISTS (
              SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_Exclusion] x
              WHERE x.Project = @Project AND x.SupersededAt IS NULL
                AND (x.CostElement IS NULL OR x.CostElement = ' + @ce + N')
                AND (x.WbsElement IS NULL OR x.WbsElement = ' + @wbs + N')
                AND (x.PartnerObject IS NULL OR x.PartnerObject = ' + @po + N'))
        GROUP BY ISNULL(' + @wbs + N', ' + @pd + N')
        ORDER BY 1;';
    EXEC sp_executesql @sql, N'@parserId BIGINT, @Project VARCHAR(20)', @parserId = @parserId, @Project = @Project;
END;
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pzl_ev_user' AND type = 'R')
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)REP_ProjectCostsByElement] TO [pzl_ev_user];
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 16)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (16, N'016_koszty_po_elementach.sql', '0.28.0');
GO
