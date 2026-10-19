/* PZL-EV – migracja 047: słownik globalny „Kontrole przebiegu” (docs/slowniki.md, rozdz. 2; docs/pipeline-fazy.md, rozdz. 4.1).
   - DICT_RunCheck – definicja przebiegu: reguła kontroli danych przebiegu dla typu programu (Wszystkie / WEWNETRZNY / SAC / CAS)
     i wariantu przebiegu (Każdy / Zamykający / W środku okresu) – aktywna, poziom (ERROR / WARNING / INFO), próg (kwota).
   Bez danych startowych: reguła bez wiersza ma ustawienie domyślne (RunCheckRules w aplikacji). Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 047_kontrole_przebiegu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunCheck]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_RunCheck] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_RunCheck] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    ProjectType      VARCHAR(20)       NOT NULL,
    RuleName         NVARCHAR(100)     NOT NULL,
    RunKind          NVARCHAR(20)      NOT NULL,
    IsActive         BIT               NOT NULL,
    Level            NVARCHAR(10)      NULL,
    Threshold        DECIMAL(28,8)     NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_RunCheck_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_RunCheck_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_RunCheck_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunCheck]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_RunCheck_Key] ON [$(Schema)].[$(Prefix)DICT_RunCheck] (Project, ProjectType, RuleName, RunKind) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 47)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (47, N'047_kontrole_przebiegu.sql', '0.56.0');
GO
