/* PZL-EV – migracja 044: raport kosztów CES (REP_ProjectCosts) – Grouping z drzewa Performance Objectives po elemencie WBS.
   Dotąd Grouping wyznaczał Project definition: wszystkie koszty Project definition trafiały pod korzeń jednego węzła, choć
   finansista grupuje elementy WBS w drzewie nakładki (także w węzłach wirtualnych) pod różnymi węzłami najwyższego poziomu.
   Teraz (decyzja 2026-10-15): Grouping wiersza kosztu = najwyższy poziom drzewa nad węzłem jego elementu WBS; element WBS
   spoza nakładki – jak dotąd, Grouping Project definition. Project definition – z ACTUALS; Cost Element (klucz słownika)
   i Cost Elem. Descr. (słownik efektywny projektu, bez wpisu – nazwa z danych) jak dotąd; wiersze sumowane do Cost Element
   w ramach Grouping i Project definition; kolejność grup – kolejność węzłów najwyższego poziomu w drzewie.
   Procedura jak w 043, zmienione tylko wyznaczenie Grouping i kolejność. Skrypt jest idempotentny. Uruchomienie: aplikacja
   (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 044_raport_kosztow_grouping.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
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

    -- Ostatni okres w danych: wszystkie najnowsze pliki ACTUALS (indeks zestawienia po pliku).
    DECLARE @last INT = (
        SELECT MAX(b.FiscalYear * 100 + b.Period)
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f
        JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.FileId = f.FileId);

    -- Cost elementy rozliczeniowe – słownik efektywny projektu (zmiana projektu przed globalnym); w raporcie pomijane.
    CREATE TABLE #settlement (CostElement NVARCHAR(400) NOT NULL PRIMARY KEY);
    INSERT INTO #settlement (CostElement)
    SELECT c.CostElement
    FROM (SELECT CostElement, IsSettlement,
                 ROW_NUMBER() OVER (PARTITION BY CostElement ORDER BY CASE WHEN Project IS NULL THEN 1 ELSE 0 END) AS rn
          FROM [$(Schema)].[$(Prefix)DICT_CostCategory]
          WHERE SupersededAt IS NULL AND (Project = @Project OR Project IS NULL)) c
    WHERE c.rn = 1 AND c.IsSettlement = 1;

    -- Sumy ostatniego importu: Grouping × Project definition × Cost Element × rok × okres, bez wykluczeń projektu i bez
    -- cost elementów rozliczeniowych; Grouping – z elementu WBS wiersza w nakładce, a bez niego – z Project definition.
    CREATE TABLE #sums (Grouping NVARCHAR(400) NULL, GroupingOrder INT NULL, ProjectDefinition NVARCHAR(400) NOT NULL,
                        CostElement NVARCHAR(400) NOT NULL, FiscalYear INT NULL, Period INT NULL,
                        Amount DECIMAL(28,8) NULL, CostElementName NVARCHAR(400) NULL);
    INSERT INTO #sums (Grouping, GroupingOrder, ProjectDefinition, CostElement, FiscalYear, Period, Amount, CostElementName)
    SELECT COALESCE(e.Grouping, d.Grouping), COALESCE(e.GroupingOrder, d.GroupingOrder), b.ProjectDefinition, b.CostElement, b.FiscalYear, b.Period,
           SUM(CASE @Value WHEN N'ValueRepCur' THEN b.ValueRepCur ELSE b.ValueObjCrcy END),
           MAX(b.CostElementName)
    FROM #definitions d
    JOIN [$(Schema)].[$(Prefix)CAN_ActualsBalance] b ON b.ProjectDefinition = d.ProjectDefinition
    LEFT JOIN #elements e ON e.WbsElement = b.WbsElement
    JOIN [$(Schema)].[$(Prefix)CAN_LatestFiles]('ACTUALS', NULL, NULL) f ON f.FileId = b.FileId
    WHERE b.CostElement IS NOT NULL
      AND b.CostElement NOT IN (SELECT s.CostElement FROM #settlement s)
      AND NOT EXISTS (
          SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_Exclusion] x
          WHERE x.Project = @Project AND x.SupersededAt IS NULL
            AND (x.CostElement IS NULL OR x.CostElement = b.CostElement)
            AND (x.WbsElement IS NULL OR x.WbsElement = b.WbsElement)
            AND (x.PartnerObject IS NULL OR x.PartnerObject = b.PartnerObject))
    GROUP BY COALESCE(e.Grouping, d.Grouping), COALESCE(e.GroupingOrder, d.GroupingOrder), b.ProjectDefinition, b.CostElement, b.FiscalYear, b.Period;

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
        SELECT s.Grouping AS [Grouping], s.ProjectDefinition AS [Project definition], d.Description AS [Project definition description],
               s.CostElement AS [Cost Element], COALESCE(MAX(p.Description), MAX(g.Description), MAX(s.CostElementName)) AS [Cost Elem. Descr.],
               COALESCE(MAX(p.CostCategory), MAX(g.CostCategory)) AS [Cost grouping]' + @columns + N'
        FROM #sums s
        JOIN #definitions d ON d.ProjectDefinition = s.ProjectDefinition
        LEFT JOIN [$(Schema)].[$(Prefix)DICT_CostCategory] p ON p.Project = @Project AND p.SupersededAt IS NULL AND p.CostElement = s.CostElement
        LEFT JOIN [$(Schema)].[$(Prefix)DICT_CostCategory] g ON g.Project IS NULL AND g.SupersededAt IS NULL AND g.CostElement = s.CostElement
        GROUP BY s.GroupingOrder, s.Grouping, s.ProjectDefinition, d.Description, s.CostElement
        ORDER BY s.GroupingOrder, s.Grouping, s.ProjectDefinition, s.CostElement;';
    EXEC sp_executesql @sql, N'@Project VARCHAR(20)', @Project = @Project;
END;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 44)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (44, N'044_raport_kosztow_grouping.sql', '0.55.0');
GO
