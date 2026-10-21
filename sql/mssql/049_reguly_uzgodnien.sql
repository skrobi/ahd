/* PZL-EV – migracja 049: słownik globalny „Reguły uzgodnień” (docs/slowniki.md, rozdz. 2; docs/pipeline-fazy.md, rozdz. 4.1).
   - DICT_RunReconciliation – własne kontrole przebiegu: dla typu programu (Wszystkie / WEWNETRZNY / SAC / CAS) i wariantu
     przebiegu (Każdy / Zamykający / W środku okresu) – lewa strona, warunek (=, <=, >=, <>), prawa strona (formuły z miar programu,
     np. [Koszt brutto] <= [BAC] * 1,1), tolerancja, poziom wyniku. Bez danych startowych. Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 049_reguly_uzgodnien.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunReconciliation]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_RunReconciliation] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_RunReconciliation] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    ProjectType      VARCHAR(20)       NOT NULL,
    Name             NVARCHAR(200)     NOT NULL,
    RunKind          NVARCHAR(20)      NOT NULL,
    IsActive         BIT               NOT NULL,
    LeftFormula      NVARCHAR(1000)    NOT NULL,
    Comparison       NVARCHAR(4)       NOT NULL,
    RightFormula     NVARCHAR(1000)    NOT NULL,
    Tolerance        DECIMAL(28,8)     NULL,
    Level            NVARCHAR(10)      NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_RunReconciliation_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_RunReconciliation_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_RunReconciliation_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunReconciliation]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_RunReconciliation_Key] ON [$(Schema)].[$(Prefix)DICT_RunReconciliation] (Project, ProjectType, Name, RunKind) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 49)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (49, N'049_reguly_uzgodnien.sql', '0.56.0');
GO
