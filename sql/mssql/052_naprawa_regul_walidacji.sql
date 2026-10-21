/* PZL-EV – migracja 052: naprawa reguł walidacji w SQL (docs/pipeline-fazy.md, rozdz. 4.1; tasks/F5.7).
   Baza, w której migrację 051 wykonała aplikacja 0.58.0 (reguły z formułami), ma tabelę DICT_RunValidation w starym kształcie
   (CheckName, Condition, Message …) i nie ma zmian nowej 051 (zapisana jako wykonana). Skrypt:
   - DICT_RunValidation bez kolumny SqlText (reguły z formułami – nieużywane) – usuwana z danymi i historią i zakładana na nowo
     (reguły w SQL; reguły startowe – migracja 053);
   - zmiany migracji 051 (wykonywane tylko, gdy ich brak): RUN_BalanceEv, RUN_Issue.Details, RUN_Issue.CheckName do 200 znaków,
     usunięcie DICT_RunCheck (047), DICT_RunReconciliation (049), META_RunPin.CheckSettings (048), DICT_RunStepElement.Level
     i Threshold (050).
   W bazie z nową 051 – bez zmian. Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy
   starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 052_naprawa_regul_walidacji.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Stara tabela reguł z formułami (051 z aplikacji 0.58.0) – usunięta; niżej założona w kształcie reguł SQL.
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunValidation]') IS NOT NULL
   AND COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_RunValidation]', N'SqlText') IS NULL
    DROP TABLE [$(Schema)].[$(Prefix)DICT_RunValidation];
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunValidation]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_RunValidation] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_RunValidation] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Name             NVARCHAR(200)     NOT NULL,
    Description      NVARCHAR(1000)    NULL,
    Level            NVARCHAR(10)      NOT NULL,
    IsActive         BIT               NOT NULL,
    SqlText          NVARCHAR(MAX)     NOT NULL,
    ResultTemplate   NVARCHAR(2000)    NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_RunValidation_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_RunValidation_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_RunValidation_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunValidation]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_RunValidation_Key] ON [$(Schema)].[$(Prefix)DICT_RunValidation] (Project, Name) WHERE SupersededAt IS NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)RUN_BalanceEv]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)RUN_BalanceEv] (
    PinId         BIGINT          NOT NULL,
    WbsElement    NVARCHAR(400)   NOT NULL,
    CostElement   NVARCHAR(400)   NOT NULL,
    FiscalYear    INT             NOT NULL,
    Period        INT             NOT NULL,
    CostKind      NVARCHAR(20)    NOT NULL,
    Amount        DECIMAL(28,8)   NOT NULL,
    INDEX [IX_$(Prefix)RUN_BalanceEv_Pin] CLUSTERED (PinId, WbsElement, CostElement, FiscalYear, Period)
);
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)RUN_Issue]', N'Details') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)RUN_Issue] ADD Details NVARCHAR(MAX) NULL;
GO
-- Kontrola wyniku = nazwa reguły (do 200 znaków jak w słowniku).
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)RUN_Issue]', N'CheckName') < 400
    ALTER TABLE [$(Schema)].[$(Prefix)RUN_Issue] ALTER COLUMN CheckName NVARCHAR(200) NOT NULL;
GO

-- Sprzątanie: słowniki i kolumny reguł sprzed reguł SQL.
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunCheck]') IS NOT NULL
    DROP TABLE [$(Schema)].[$(Prefix)DICT_RunCheck];
GO
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunReconciliation]') IS NOT NULL
    DROP TABLE [$(Schema)].[$(Prefix)DICT_RunReconciliation];
GO
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_RunPin]', N'CheckSettings') IS NOT NULL
    ALTER TABLE [$(Schema)].[$(Prefix)META_RunPin] DROP COLUMN CheckSettings;
GO
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_RunStepElement]', N'Level') IS NOT NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_RunStepElement] DROP COLUMN Level;
GO
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_RunStepElement]', N'Threshold') IS NOT NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_RunStepElement] DROP COLUMN Threshold;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 52)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (52, N'052_naprawa_regul_walidacji.sql', '0.59.0');
GO
