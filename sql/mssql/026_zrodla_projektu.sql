/* PZL-EV – migracja 026: słownik projektu „Źródła danych” (docs/slowniki.md, rozdz. 3).
   Definicje źródeł (Administracja) importowane dla projektu – „Importuj źródła projektu” importuje tylko pliki tych
   definicji (np. ACTUALS_PROGRAM1 – pliki ACTUALS_PROGRAM1_*, parser ACTUALS).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 026_zrodla_projektu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_ProjectSource]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_ProjectSource] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_ProjectSource] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NOT NULL,
    SourceCode       VARCHAR(60)       NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_ProjectSource_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_ProjectSource_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_ProjectSource_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_ProjectSource]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_ProjectSource_Key] ON [$(Schema)].[$(Prefix)DICT_ProjectSource] (Project, SourceCode) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 26)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (26, N'026_zrodla_projektu.sql', '0.42.0');
GO
