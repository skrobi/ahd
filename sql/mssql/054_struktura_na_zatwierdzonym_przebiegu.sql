/* PZL-EV – migracja 054: struktura programu na danych ACTUALS zatwierdzonego przebiegu (docs/pipeline-fazy.md, rozdz. 4.1).
   - REP_ProjectCostsByElement (043) i REP_ProjectCasCosts (045) – nowy parametr @AsOf: stan danych ACTUALS na chwilę przypięcia
     ostatniego przebiegu z zakończonym etapem „Zatwierdzenie ACTUALS” (pliki zaimportowane do @AsOf – CAN_LatestFiles); NULL – ostatni
     import (jak dotąd). Treść procedur bez innych zmian. Wywołanie bez @AsOf działa jak wcześniej.
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 054_struktura_na_zatwierdzonym_przebiegu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)REP_ProjectCostsByElement]
    @Project VARCHAR(20),
    @Value   NVARCHAR(64) = N'ValueObjCrcy',  -- pole kwoty ACTUALS (ValueObjCrcy – waluta obiektu, ValueRepCur – waluta raportu)
    @Hours   NVARCHAR(64) = NULL,             -- pole godzin (TotalQuantity – Actual DLH); NULL – kolumna Hours pusta
    @AsOf    DATETIMEOFFSET(7) = NULL         -- stan danych: pliki zaimportowane do tej chwili (przypięcie zatwierdzonego przebiegu); NULL – ostatni import
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
    JOIN [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, @AsOf) f ON f.FileId = b.FileId
    LEFT JOIN #kind k ON k.CostElement = b.CostElement
    WHERE NOT (b.CostElement IN (SELECT s.CostElement FROM #settlement s))
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

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)REP_ProjectCasCosts]
    @Project VARCHAR(20),
    @AsOf    DATETIMEOFFSET(7) = NULL   -- stan danych: pliki zaimportowane do tej chwili (przypięcie zatwierdzonego przebiegu); NULL – ostatni import
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

    -- Ostatni okres w danych: wszystkie najnowsze pliki ACTUALS do @AsOf (jak kolumna okresu REP_ProjectCosts).
    DECLARE @last INT = (
        SELECT MAX(b.FiscalYear * 100 + b.Period)
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, @AsOf) f
        JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.FileId = f.FileId);

    WITH sums AS (
        SELECT COALESCE(e.Grouping, d.Grouping) AS Grouping, COALESCE(e.GroupingOrder, d.GroupingOrder) AS GroupingOrder,
               b.ProjectDefinition, ISNULL(b.WbsElement, b.ProjectDefinition) AS WbsElement, b.CostElement,
               b.FiscalYear, b.Period, SUM(b.ValueObjCrcy) AS Amount, MAX(b.CostElementName) AS CostElementName
        FROM #definitions d
        JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.ProjectDefinition = d.ProjectDefinition
        JOIN [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, @AsOf) f ON f.FileId = b.FileId
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

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 54)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (54, N'054_struktura_na_zatwierdzonym_przebiegu.sql', '0.60.0');
GO
