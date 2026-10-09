/* PZL-EV – migracja 009: raport mapowań SAP↔CES jako słownik globalny (docs/mapowanie-ces-p1s.md, rozdz. 2;
   docs/slowniki.md, rozdz. 2).
   - DICT_MappingReport: słownik „Raport mapowań CES ↔ P1S” – wszystkie kolumny pliku, klucz src + pspnr, historia jak
     w pozostałych słownikach. Utrzymywany na ekranie Słowniki; wczytanie z Excela zastępuje całą zawartość.
   - Raport nie jest już importowany: definicje źródeł z parserem MAPOWANIA i sam parser zostają usunięte (zamknięta
     bieżąca wersja – historia zostaje). Dane kanoniczne wcześniej zaimportowanych plików zostają w CAN_Row, ale
     mapowanie ich nie czyta – raport trzeba raz wczytać do słownika.

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) albo
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 009_raport_mapowan_slownik.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_MappingReport]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_MappingReport] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_MappingReport] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Src NVARCHAR(10) NOT NULL,
    Pspnr NVARCHAR(50) NOT NULL,
    PspnrSap NVARCHAR(50) NULL,
    PspnrCes NVARCHAR(50) NULL,
    PspnrParent NVARCHAR(50) NULL,
    ProjectDef NVARCHAR(100) NULL,
    ProjectSap NVARCHAR(100) NULL,
    ProjectSapOrg NVARCHAR(100) NULL,
    ProjectCes NVARCHAR(100) NULL,
    Wbs NVARCHAR(100) NULL,
    WbsSap NVARCHAR(100) NULL,
    WbsCes NVARCHAR(100) NULL,
    WbsDesc NVARCHAR(400) NULL,
    WbsDescSap NVARCHAR(400) NULL,
    WbsDescCes NVARCHAR(400) NULL,
    Prctr NVARCHAR(50) NULL,
    PrctrSap NVARCHAR(50) NULL,
    PrctrCes NVARCHAR(50) NULL,
    Lvl NVARCHAR(10) NULL,
    LvlSap NVARCHAR(10) NULL,
    LvlCes NVARCHAR(10) NULL,
    PerfObg NVARCHAR(100) NULL,
    Techs NVARCHAR(100) NULL,
    SalesOrderTyp NVARCHAR(20) NULL,
    SalesOrder NVARCHAR(50) NULL,
    SalesOrderPos NVARCHAR(20) NULL,
    Matnr NVARCHAR(100) NULL,
    Network NVARCHAR(50) NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_MappingReport_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_MappingReport_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_MappingReport_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_MappingReport]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_MappingReport_Key] ON [$(Schema)].[$(Prefix)DICT_MappingReport] (Project, Src, Pspnr) WHERE SupersededAt IS NULL;
GO

/* ---------- Koniec importu raportu mapowań: definicje źródeł z parserem MAPOWANIA i parser ---------- */

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();
DECLARE @definitions INT, @parsers INT;

UPDATE [$(Schema)].[$(Prefix)META_SourceDefinition] SET SupersededAt = @at, SupersededBy = @by
WHERE SupersededAt IS NULL AND Parser = 'MAPOWANIA';
SET @definitions = @@ROWCOUNT;

UPDATE [$(Schema)].[$(Prefix)META_Parser] SET SupersededAt = @at, SupersededBy = @by
WHERE SupersededAt IS NULL AND Code = 'MAPOWANIA';
SET @parsers = @@ROWCOUNT;

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 9)
INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
VALUES (@at, @by, N'Migracje', N'Migracja 009 – raport mapowań SAP↔CES jako słownik globalny (DICT_MappingReport); usunięte definicje źródeł z parserem MAPOWANIA: '
        + CAST(@definitions AS NVARCHAR(10)) + N', parser MAPOWANIA: ' + CAST(@parsers AS NVARCHAR(10)) + N' – raport wczytuje się w Słownikach');

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 9)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (9, N'009_raport_mapowan_slownik.sql', '0.19.0');
GO
