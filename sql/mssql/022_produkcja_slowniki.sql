/* PZL-EV – migracja 022: słowniki odczytu danych produkcyjnych struktury projektu (docs/slowniki.md, rozdz. 2–3).
   - DICT_DjkRate – „Wskaźniki DJK” (globalny): udział godzin jakości DJK według prefiksu grupy stanowisk (IPT).
   - DICT_ProductionParameter – „Parametry produkcji” (globalny): okno produktywności IPT, statusy materiałów
     dostarczonych (vAPD), dzielenie wartości materiałów przez (1 + Z_CLO).
   - DICT_VirtualP1s – „Elementy wirtualne P1S” (projektu): element spoza LOG.WBS (np. Paint) wydzielony z elementu
     nadrzędnego regułą operacji vAHDD (SWBS, CPLGR, ARBPL).
   Dane startowe parametrów – migracja 023. Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja
   albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 022_produkcja_slowniki.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_DjkRate]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_DjkRate] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_DjkRate] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    WorkCenterGroup  NVARCHAR(20)      NOT NULL,
    Share            DECIMAL(28,8)     NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_DjkRate_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_DjkRate_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_DjkRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_DjkRate]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_DjkRate_Key] ON [$(Schema)].[$(Prefix)DICT_DjkRate] (Project, WorkCenterGroup) WHERE SupersededAt IS NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_ProductionParameter]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_ProductionParameter] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_ProductionParameter] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Name             NVARCHAR(100)     NOT NULL,
    Value            NVARCHAR(400)     NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_ProductionParameter_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_ProductionParameter_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_ProductionParameter_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_ProductionParameter]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_ProductionParameter_Key] ON [$(Schema)].[$(Prefix)DICT_ProductionParameter] (Project, Name) WHERE SupersededAt IS NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_VirtualP1s]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_VirtualP1s] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_VirtualP1s] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NOT NULL,
    Code             NVARCHAR(100)     NOT NULL,
    Parent           NVARCHAR(100)     NOT NULL,
    Name             NVARCHAR(200)     NOT NULL,
    Swbs             NVARCHAR(100)     NULL,
    Cplgr            NVARCHAR(100)     NULL,
    Arbpl            NVARCHAR(100)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_VirtualP1s_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_VirtualP1s_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_VirtualP1s_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_VirtualP1s]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_VirtualP1s_Key] ON [$(Schema)].[$(Prefix)DICT_VirtualP1s] (Project, Code) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 22)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (22, N'022_produkcja_slowniki.sql', '0.38.0');
GO
