/* PZL-EV – migracja 027: przebieg projektu – uruchomienie, przypięcie stanu i kontrole danych ACTUALS
   (docs/pipeline-fazy.md, P0–P2; docs/model-danych.md, rozdz. 4).
   - META_Run: przebieg = projekt + tydzień kalendarza okresów (jeden na projekt i tydzień), R-<Projekt>-<RRRR-MM>-T<NN>.
   - META_RunPin: kolejne uruchomienia (przypięcia stanu) przebiegu – kto, kiedy, import, wynik kontroli.
   - RUN_Balance: dane ACTUALS projektu w chwili przypięcia, zagregowane po elemencie WBS × cost element × rok × okres
     × rozliczenie wychodzące × waluta obiektu – podstawa porównań z poprzednim przebiegiem.
   - RUN_Issue: wyniki kontroli (poziom, element, opis, kwota).
   - RUN_PinBalance @Project, @PinId – zapis RUN_Balance z ostatniego importu ACTUALS (CAN_LatestFiles), Project
     definition projektu jak REP_ProjectCostsByElement; rozliczenie wychodzące – cost element „Rozliczeniowy” (słownik
     efektywny projektu) z klasą obiektu partnera inną niż „Profit analysis”. Kwota – ValueObjCrcy (waluta obiektu).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 027_przebiegi.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Run]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_Run] (
    RunId      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_Run] PRIMARY KEY,
    Code       VARCHAR(60)          NOT NULL CONSTRAINT [UQ_$(Prefix)META_Run_Code] UNIQUE,
    Project    VARCHAR(20)          NOT NULL,
    Year       INT                  NOT NULL,
    Week       INT                  NOT NULL,
    Period     VARCHAR(7)           NOT NULL,
    IsClosing  BIT                  NOT NULL,
    CreatedAt  DATETIMEOFFSET(7)    NOT NULL,
    CreatedBy  NVARCHAR(128)        NOT NULL,
    CONSTRAINT [UQ_$(Prefix)META_Run_Week] UNIQUE (Project, Year, Week)
);
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_RunPin]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_RunPin] (
    PinId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_RunPin] PRIMARY KEY,
    RunId          BIGINT               NOT NULL CONSTRAINT [FK_$(Prefix)META_RunPin_Run] REFERENCES [$(Schema)].[$(Prefix)META_Run] (RunId),
    PinNo          INT                  NOT NULL,
    StartedAt      DATETIMEOFFSET(7)    NOT NULL,
    StartedBy      NVARCHAR(128)        NOT NULL,
    Machine        NVARCHAR(128)        NOT NULL,
    FinishedAt     DATETIMEOFFSET(7)    NULL,
    Status         NVARCHAR(60)         NOT NULL,
    ImportBatchId  BIGINT               NULL,
    Rows           BIGINT               NULL,
    GrossCost      DECIMAL(28,8)        NULL,
    Balance        DECIMAL(28,8)        NULL,
    Errors         INT                  NOT NULL CONSTRAINT [DF_$(Prefix)META_RunPin_Errors] DEFAULT 0,
    Warnings       INT                  NOT NULL CONSTRAINT [DF_$(Prefix)META_RunPin_Warnings] DEFAULT 0,
    Summary        NVARCHAR(2000)       NULL,
    CONSTRAINT [UQ_$(Prefix)META_RunPin_No] UNIQUE (RunId, PinNo)
);
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)RUN_Balance]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)RUN_Balance] (
    PinId         BIGINT          NOT NULL,
    WbsElement    NVARCHAR(400)   NOT NULL,
    CostElement   NVARCHAR(400)   NOT NULL,
    FiscalYear    INT             NOT NULL,
    Period        INT             NOT NULL,
    IsSettlement  BIT             NOT NULL,
    Currency      NVARCHAR(20)    NOT NULL,
    Rows          INT             NOT NULL,
    Amount        DECIMAL(28,8)   NOT NULL,
    LastCreatedOn DATE            NULL,
    INDEX [IX_$(Prefix)RUN_Balance_Pin] CLUSTERED (PinId, WbsElement, CostElement, FiscalYear, Period)
);
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)RUN_Issue]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)RUN_Issue] (
    Id        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)RUN_Issue] PRIMARY KEY,
    PinId     BIGINT          NOT NULL,
    CheckName NVARCHAR(100)   NOT NULL,
    Level     VARCHAR(10)     NOT NULL,
    Element   NVARCHAR(400)   NULL,
    Message   NVARCHAR(2000)  NOT NULL,
    Amount    DECIMAL(28,8)   NULL,
    INDEX [IX_$(Prefix)RUN_Issue_Pin] NONCLUSTERED (PinId)
);
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)RUN_PinBalance]
    @Project VARCHAR(20),
    @PinId   BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @parserId BIGINT, @fields NVARCHAR(MAX);
    SELECT @parserId = ParserId, @fields = Fields FROM [$(Schema)].[$(Prefix)META_Parser] WHERE SupersededAt IS NULL AND Code = 'ACTUALS';
    IF @parserId IS NULL
        THROW 50011, N'Nie ma parsera ACTUALS.', 1;

    DECLARE @slots TABLE (Field NVARCHAR(64) PRIMARY KEY, Slot VARCHAR(3));
    INSERT INTO @slots (Field, Slot)
    SELECT x.Field, x.Slot FROM OPENJSON(@fields) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
    WHERE x.Slot LIKE '[TLNID][0-9][0-9]';
    DECLARE @pd NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ProjectDefinition'),
            @ce NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElement'), N'NULL'),
            @val NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ValueObjCrcy' AND Slot LIKE 'N%'),
            @wbs NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'WbsElement'), N'NULL'),
            @poc NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'PartnerObjectClass'), N'NULL'),
            @cur NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ObjectCurrency'), N'NULL'),
            @year NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'FiscalYear'), N'NULL'),
            @period NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'Period'), N'NULL'),
            @created NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CreatedOn'), N'NULL');
    IF @pd IS NULL OR @val IS NULL
        THROW 50012, N'Parser ACTUALS musi mieć pola ProjectDefinition i ValueObjCrcy.', 1;

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
    DECLARE @sql NVARCHAR(MAX) = N'
        INSERT INTO [$(Schema)].[$(Prefix)RUN_Balance] (PinId, WbsElement, CostElement, FiscalYear, Period, IsSettlement, Currency, Rows, Amount, LastCreatedOn)
        SELECT @PinId, x.WbsElement, x.CostElement, x.FiscalYear, x.Period, x.IsSettlement, x.Currency, COUNT(*), SUM(x.Amount), MAX(x.CreatedOn)
        FROM (
            SELECT ISNULL(' + @wbs + N', N'''') AS WbsElement, ISNULL(' + @ce + N', N'''') AS CostElement,
                   ISNULL(TRY_CONVERT(INT, ' + @year + N'), 0) AS FiscalYear, ISNULL(TRY_CONVERT(INT, ' + @period + N'), 0) AS Period,
                   CAST(CASE WHEN ' + @ce + N' IN (SELECT s.CostElement FROM #settlement s) AND ISNULL(' + @poc + N', N'''') <> N''Profit analysis''
                             THEN 1 ELSE 0 END AS BIT) AS IsSettlement,
                   ISNULL(' + @cur + N', N'''') AS Currency, ISNULL(' + @val + N', 0) AS Amount, TRY_CONVERT(DATE, ' + @created + N') AS CreatedOn
            FROM [$(Schema)].[$(Prefix)CAN_LatestFiles](''ACTUALS'', NULL, NULL) f
            JOIN [$(Schema)].[$(Prefix)CAN_Row] r ON r.FileId = f.FileId AND r.ParserId = @parserId
            WHERE ' + @pd + N' IN (SELECT d.ProjectDefinition FROM #definitions d)) x
        GROUP BY x.WbsElement, x.CostElement, x.FiscalYear, x.Period, x.IsSettlement, x.Currency;';
    EXEC sp_executesql @sql, N'@parserId BIGINT, @PinId BIGINT', @parserId = @parserId, @PinId = @PinId;
END;
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pzl_ev_user' AND type = 'R')
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)RUN_PinBalance] TO [pzl_ev_user];
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 27)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (27, N'027_przebiegi.sql', '0.43.0');
GO
