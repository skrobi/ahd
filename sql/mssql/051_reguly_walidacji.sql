/* PZL-EV – migracja 051: reguły walidacji przebiegu w SQL (docs/pipeline-fazy.md, rozdz. 4.1; tasks/F5.7).
   - DICT_RunValidation – „Reguły walidacji” (globalny): nazwa, opis, poziom (ERROR / WARNING / INFO), aktywna, zapytanie SQL
     (jeden SELECT / WITH na tabelach danych uruchomienia – każdy zwrócony wiersz to nieprawidłowość), opis wyniku z kolumnami
     w { }. Bez danych startowych – pusty słownik oznacza reguły startowe aplikacji (edytor na ekranie Przebiegi zapisuje je do zmiany).
   - RUN_BalanceEv – dane uruchomienia jak w strukturze programu (tabela „ev” reguł): bez rozliczeniowych, po Wykluczeniach,
     CAS po narzutach (liczy aplikacja), rodzaj kosztu; element WBS × cost element × rok × okres × rodzaj.
   - RUN_Issue.Details – wiersz wyniku reguły (JSON kolumna → wartość): porównanie uruchomień, „Pokaż zmiany”; RUN_Issue.CheckName –
     do 200 znaków (nazwa reguły).
   - Sprzątanie po regułach z formułami i katalogu C# (nieużywane od tej wersji): tabele DICT_RunCheck – „Kontrole przebiegu” (047)
     i DICT_RunReconciliation – „Reguły uzgodnień” (049) usuwane z danymi i historią; kolumny META_RunPin.CheckSettings (048)
     oraz DICT_RunStepElement.Level i Threshold (050) usuwane.
   Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 051_reguly_walidacji.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
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

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 51)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (51, N'051_reguly_walidacji.sql', '0.59.0');
GO
