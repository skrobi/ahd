/* PZL-EV – migracja 045: koszt projektów CAS po narzutach i kurs CAS (docs/slowniki.md, rozdz. 6.1).
   - DICT_FxRate („Kursy walut”): kurs obowiązujący od – do (ValidFrom, ValidTo; ValidTo NULL – bezterminowo) zamiast
     okresu RRRR-MM i kolumna CasRate („Kurs CAS” – projekty CAS w walucie innej niż PLN). Istniejące wiersze: obowiązują
     w miesiącu swojego okresu (od pierwszego do ostatniego dnia); kolumna Period zostaje dla historii, aplikacja jej
     nie używa. Rate („Kurs”) opcjonalny – wiersz ma kurs albo kurs CAS.
   - REP_ProjectCasCosts @Project – koszt bazowy projektu CAS: Grouping (jak REP_ProjectCosts, 044) × Project definition ×
     element WBS × Cost Element × rok × okres, kwota zawsze w PLN (ValueObjCrcy – waluta obiektu); te same wiersze co ACWP
     struktury (bez cost elementów rozliczeniowych – 043, bez wykluczeń projektu); LastYear / LastPeriod – ostatni okres
     w danych ACTUALS (kolumna okresu raportu). Narzuty, przeliczenie kursem CAS i TOTAL COST liczy aplikacja (CasCosts).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 045_koszty_cas_z_narzutami.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_FxRate]', N'ValidFrom') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_FxRate] ADD ValidFrom DATE NULL, ValidTo DATE NULL, CasRate DECIMAL(28,8) NULL;
GO

UPDATE [$(Schema)].[$(Prefix)DICT_FxRate]
SET ValidFrom = DATEFROMPARTS(CAST(LEFT(Period, 4) AS INT), CAST(RIGHT(Period, 2) AS INT), 1),
    ValidTo = EOMONTH(DATEFROMPARTS(CAST(LEFT(Period, 4) AS INT), CAST(RIGHT(Period, 2) AS INT), 1))
WHERE ValidFrom IS NULL AND Period LIKE '[0-9][0-9][0-9][0-9]-[01][0-9]' AND CAST(RIGHT(Period, 2) AS INT) BETWEEN 1 AND 12;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_FxRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_FxRate]'))
    DROP INDEX [UX_$(Prefix)DICT_FxRate_Key] ON [$(Schema)].[$(Prefix)DICT_FxRate];
GO

ALTER TABLE [$(Schema)].[$(Prefix)DICT_FxRate] ALTER COLUMN Period VARCHAR(7) NULL;
ALTER TABLE [$(Schema)].[$(Prefix)DICT_FxRate] ALTER COLUMN Rate DECIMAL(28,8) NULL;
GO

IF EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_FxRate] WHERE ValidFrom IS NULL)
    THROW 51045, N'Kursy walut z okresem innym niż RRRR-MM – popraw okres przed migracją 045.', 1;
ALTER TABLE [$(Schema)].[$(Prefix)DICT_FxRate] ALTER COLUMN ValidFrom DATE NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_FxRate_Validity' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_FxRate]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_FxRate_Validity] ON [$(Schema)].[$(Prefix)DICT_FxRate] (Project, Currency, ValidFrom) WHERE SupersededAt IS NULL;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)REP_ProjectCasCosts]
    @Project VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @message NVARCHAR(400);
    IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Project] WHERE Code = @Project AND SupersededAt IS NULL)
    BEGIN
        SET @message = N'Nie ma projektu ' + ISNULL(@Project, N'(brak)') + N'.';
        THROW 50013, @message, 1;
    END;

    -- Drzewo nakładki Performance Objectives (finansista grupuje w nim elementy, także w węzłach wirtualnych): każdy węzeł
    -- z korzeniem (najwyższy poziom) – Grouping wiersza kosztu.
    CREATE TABLE #tree (NodeId BIGINT NOT NULL PRIMARY KEY, Name NVARCHAR(400) NOT NULL, ProjectDefinition NVARCHAR(400) NULL,
                        WbsElement NVARCHAR(400) NULL, SortOrder INT NOT NULL, Depth INT NOT NULL, RootName NVARCHAR(400) NOT NULL, RootOrder INT NOT NULL);
    WITH po AS (
        SELECT NodeId, ParentNodeId, SortOrder, Name, ProjectDefinition, WbsElement
        FROM [$(Schema)].[$(Prefix)META_PerformanceObjective] WHERE Project = @Project AND SupersededAt IS NULL),
    tree AS (
        SELECT NodeId, Name AS RootName, SortOrder AS RootOrder, 0 AS Depth FROM po WHERE ParentNodeId IS NULL
        UNION ALL
        SELECT c.NodeId, t.RootName, t.RootOrder, t.Depth + 1 FROM po c JOIN tree t ON c.ParentNodeId = t.NodeId)
    INSERT INTO #tree (NodeId, Name, ProjectDefinition, WbsElement, SortOrder, Depth, RootName, RootOrder)
    SELECT po.NodeId, po.Name, po.ProjectDefinition, po.WbsElement, po.SortOrder, t.Depth, t.RootName, t.RootOrder
    FROM po JOIN tree t ON t.NodeId = po.NodeId
    OPTION (MAXRECURSION 200);

    -- Element WBS nakładki → Grouping (korzeń nad jego węzłem).
    CREATE TABLE #elements (WbsElement NVARCHAR(400) NOT NULL PRIMARY KEY, Grouping NVARCHAR(400) NOT NULL, GroupingOrder INT NOT NULL);
    INSERT INTO #elements (WbsElement, Grouping, GroupingOrder)
    SELECT WbsElement, MIN(RootName), MIN(RootOrder) FROM #tree WHERE WbsElement IS NOT NULL GROUP BY WbsElement;

    -- Project definition projektu: opis (nazwa elementu = Project definition) i Grouping wierszy, których elementu WBS
    -- nie ma w nakładce (korzeń nad węzłem Project definition albo najpłytszym węzłem z tym Project definition).
    CREATE TABLE #definitions (ProjectDefinition NVARCHAR(400) NOT NULL PRIMARY KEY, Description NVARCHAR(400) NULL, Grouping NVARCHAR(400) NULL,
                               GroupingOrder INT NULL);
    WITH ranked AS (
        SELECT v.Code AS ProjectDefinition, t.Name, t.RootName, t.RootOrder,
               ROW_NUMBER() OVER (PARTITION BY v.Code
                                  ORDER BY CASE WHEN t.WbsElement = v.Code THEN 0 ELSE 1 END, t.Depth, t.SortOrder) AS rn
        FROM #tree t
        CROSS APPLY (VALUES (t.ProjectDefinition), (t.WbsElement)) v(Code)
        WHERE v.Code IS NOT NULL)
    INSERT INTO #definitions (ProjectDefinition, Description, Grouping, GroupingOrder)
    SELECT ProjectDefinition, Name, RootName, RootOrder FROM ranked WHERE rn = 1;


    -- Cost elementy rozliczeniowe – słownik efektywny projektu (043).
    CREATE TABLE #settlement (CostElement NVARCHAR(400) NOT NULL PRIMARY KEY);
    INSERT INTO #settlement (CostElement)
    SELECT c.CostElement
    FROM (SELECT CostElement, IsSettlement,
                 ROW_NUMBER() OVER (PARTITION BY CostElement ORDER BY CASE WHEN Project IS NULL THEN 1 ELSE 0 END) AS rn
          FROM [$(Schema)].[$(Prefix)DICT_CostCategory]
          WHERE SupersededAt IS NULL AND (Project = @Project OR Project IS NULL)) c
    WHERE c.rn = 1 AND c.IsSettlement = 1;

    -- Ostatni okres w danych: wszystkie najnowsze pliki ACTUALS (jak kolumna okresu REP_ProjectCosts).
    DECLARE @last INT = (
        SELECT MAX(b.FiscalYear * 100 + b.Period)
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f
        JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.FileId = f.FileId);

    WITH sums AS (
        SELECT COALESCE(e.Grouping, d.Grouping) AS Grouping, COALESCE(e.GroupingOrder, d.GroupingOrder) AS GroupingOrder,
               b.ProjectDefinition, ISNULL(b.WbsElement, b.ProjectDefinition) AS WbsElement, b.CostElement,
               b.FiscalYear, b.Period, SUM(b.ValueObjCrcy) AS Amount, MAX(b.CostElementName) AS CostElementName
        FROM #definitions d
        JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.ProjectDefinition = d.ProjectDefinition
        JOIN [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f ON f.FileId = b.FileId
        LEFT JOIN #elements e ON e.WbsElement = b.WbsElement
        WHERE NOT (b.CostElement IN (SELECT s.CostElement FROM #settlement s))
          AND NOT EXISTS (
              SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_Exclusion] x
              WHERE x.Project = @Project AND x.SupersededAt IS NULL
                AND (x.CostElement IS NULL OR x.CostElement = b.CostElement)
                AND (x.WbsElement IS NULL OR x.WbsElement = b.WbsElement)
                AND (x.PartnerObject IS NULL OR x.PartnerObject = b.PartnerObject))
        GROUP BY COALESCE(e.Grouping, d.Grouping), COALESCE(e.GroupingOrder, d.GroupingOrder), b.ProjectDefinition,
                 ISNULL(b.WbsElement, b.ProjectDefinition), b.CostElement, b.FiscalYear, b.Period)
    -- Opis i Cost Category: słownik efektywny projektu (zmiany projektu przed globalnym); opis bez wpisu – z danych.
    SELECT s.Grouping, s.GroupingOrder, s.ProjectDefinition, d.Description AS ProjectDefinitionDescription, s.WbsElement,
           s.CostElement, COALESCE(p.Description, g.Description, s.CostElementName) AS CostElementDescription,
           COALESCE(p.CostCategory, g.CostCategory) AS CostGrouping,
           ISNULL(s.FiscalYear, 0) AS FiscalYear, ISNULL(s.Period, 0) AS Period, ISNULL(s.Amount, 0) AS Amount,
           @last / 100 AS LastYear, @last % 100 AS LastPeriod
    FROM sums s
    JOIN #definitions d ON d.ProjectDefinition = s.ProjectDefinition
    OUTER APPLY (SELECT TOP (1) Description, CostCategory FROM [$(Schema)].[$(Prefix)DICT_CostCategory]
                 WHERE Project = @Project AND SupersededAt IS NULL AND CostElement = s.CostElement) p
    OUTER APPLY (SELECT TOP (1) Description, CostCategory FROM [$(Schema)].[$(Prefix)DICT_CostCategory]
                 WHERE Project IS NULL AND SupersededAt IS NULL AND CostElement = s.CostElement) g
    ORDER BY s.GroupingOrder, s.Grouping, s.ProjectDefinition, s.CostElement, s.WbsElement, s.FiscalYear, s.Period;
END;
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pzl_ev_user' AND type = 'R')
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)REP_ProjectCasCosts] TO [pzl_ev_user];
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 45)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (45, N'045_koszty_cas_z_narzutami.sql', '0.55.0');
GO
