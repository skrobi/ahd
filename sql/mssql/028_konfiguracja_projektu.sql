/* PZL-EV – migracja 028: konfiguracja projektu (docs/slowniki.md, rozdz. 3; docs/model-danych.md).
   - DICT_ProjectSetting: słownik projektu „Parametry projektu” (parametr → wartość), np. „Pole kwoty ACTUALS” –
     pole kwoty parsera ACTUALS dla ACWP struktury, raportu kosztów i przebiegu (ValueObjCrcy – waluta obiektu,
     ValueRepCur – waluta raportu, ValueTranCurr – waluta transakcji); brak wiersza – ValueObjCrcy.
   - META_RunPin.AmountField: pole kwoty użyte w przypięciu (porównanie z poprzednim przebiegiem tylko przy tym samym polu).
   - RUN_PinBalance @Project, @PinId, @Value: kwota z pola @Value, waluta z pola waluty tego pola
     (ObjectCurrency, ReportCurrency, TransactionCurrency).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 028_konfiguracja_projektu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_ProjectSetting]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_ProjectSetting] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_ProjectSetting] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NOT NULL,
    Name             NVARCHAR(100)     NOT NULL,
    Value            NVARCHAR(400)     NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_ProjectSetting_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_ProjectSetting_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_ProjectSetting_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_ProjectSetting]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_ProjectSetting_Key] ON [$(Schema)].[$(Prefix)DICT_ProjectSetting] (Project, Name) WHERE SupersededAt IS NULL;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_RunPin]', N'AmountField') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)META_RunPin] ADD AmountField NVARCHAR(64) NULL;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)RUN_PinBalance]
    @Project VARCHAR(20),
    @PinId   BIGINT,
    @Value   NVARCHAR(64) = N'ValueObjCrcy'   -- pole kwoty parsera ACTUALS (słownik „Parametry projektu”)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @parserId BIGINT, @fields NVARCHAR(MAX);
    SELECT @parserId = ParserId, @fields = Fields FROM [$(Schema)].[$(Prefix)META_Parser] WHERE SupersededAt IS NULL AND Code = 'ACTUALS';
    IF @parserId IS NULL
        THROW 50011, N'Nie ma parsera ACTUALS.', 1;
    DECLARE @message NVARCHAR(400),
            @currency NVARCHAR(64) = CASE @Value WHEN N'ValueRepCur' THEN N'ReportCurrency'
                                                 WHEN N'ValueTranCurr' THEN N'TransactionCurrency'
                                                 ELSE N'ObjectCurrency' END;

    DECLARE @slots TABLE (Field NVARCHAR(64) PRIMARY KEY, Slot VARCHAR(3));
    INSERT INTO @slots (Field, Slot)
    SELECT x.Field, x.Slot FROM OPENJSON(@fields) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
    WHERE x.Slot LIKE '[TLNID][0-9][0-9]';
    DECLARE @pd NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'ProjectDefinition'),
            @ce NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElement'), N'NULL'),
            @val NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = @Value AND Slot LIKE 'N%'),
            @wbs NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'WbsElement'), N'NULL'),
            @poc NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'PartnerObjectClass'), N'NULL'),
            @cur NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = @currency), N'NULL'),
            @year NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'FiscalYear'), N'NULL'),
            @period NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'Period'), N'NULL'),
            @created NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CreatedOn'), N'NULL');
    IF @val IS NULL
    BEGIN
        SET @message = N'Parser ACTUALS nie ma pola kwoty ' + ISNULL(@Value, N'(brak)') + N' (np. ValueObjCrcy, ValueRepCur, ValueTranCurr).';
        THROW 50014, @message, 1;
    END;
    IF @pd IS NULL
        THROW 50012, N'Parser ACTUALS musi mieć pole ProjectDefinition.', 1;

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

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 28)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (28, N'028_konfiguracja_projektu.sql', '0.44.0');
GO
