/* PZL-EV – migracja 019: słownik projektu „Kategorie WBS” (docs/slowniki.md, rozdz. 3, 8).
   Kategorie elementów WBS indywidualne dla projektu (np. Production, Programs) – kategoria opisuje WP jako rodzaj
   kosztu do raportu po kategoriach (wiele elementów WBS w jednej kategorii). Inna niż globalny słownik Cost Category
   (numer elementu kosztowego). Kolumna „Cost Category” słownika „WP i CAM” (DICT_WpCam.CostCategory) wybiera wartość
   z tego słownika (tabela Struktura – lista rozwijana).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 019_kategorie_wbs.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_WbsCategory]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_WbsCategory] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_WbsCategory] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NOT NULL,
    Category         NVARCHAR(100)     NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_WbsCategory_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_WbsCategory_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_WbsCategory_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_WbsCategory]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_WbsCategory_Key] ON [$(Schema)].[$(Prefix)DICT_WbsCategory] (Project, Category) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 19)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (19, N'019_kategorie_wbs.sql', '0.31.0');
GO
