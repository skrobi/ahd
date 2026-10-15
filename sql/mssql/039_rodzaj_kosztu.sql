/* PZL-EV – migracja 039: rodzaj kosztu w Cost Category – Hours / Materials / Other (docs/slowniki.md, rozdz. 2).
   - DICT_CostCategory.CostKind: wartości Labor → Hours, Material → Materials (Other – jawnie albo puste).
   - Konta materiałowe – dane startowe, migracja 040.
   - REP_ProjectCostsByElement: ACWP Hours / Materials według nowych wartości; reszta – ACWP Other.
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 039_rodzaj_kosztu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

UPDATE [$(Schema)].[$(Prefix)DICT_CostCategory] SET CostKind = N'Hours' WHERE CostKind = N'Labor';
UPDATE [$(Schema)].[$(Prefix)DICT_CostCategory] SET CostKind = N'Materials' WHERE CostKind = N'Material';
GO

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
           SUM(CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur WHEN N'ValueTranCurr' THEN b.ValueTranCurr ELSE b.ValueObjCrcy END) AS [Amount],
           SUM(b.Rows) AS [Rows],
           SUM(CASE WHEN @Hours = N'TotalQuantity' THEN b.TotalQuantity END) AS [Hours],
           SUM(CASE WHEN k.CostKind = N'Hours'
                    THEN CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur WHEN N'ValueTranCurr' THEN b.ValueTranCurr ELSE b.ValueObjCrcy END END) AS [Labor],
           SUM(CASE WHEN k.CostKind = N'Materials'
                    THEN CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur WHEN N'ValueTranCurr' THEN b.ValueTranCurr ELSE b.ValueObjCrcy END END) AS [Material]
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

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pzl_ev_user' AND type = 'R')
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)REP_ProjectCostsByElement] TO [pzl_ev_user];
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 39)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (39, N'039_rodzaj_kosztu.sql', '0.53.0');
GO
