/* PZL-EV – migracja 031: formuły narzutów CAS jako dane (docs/slowniki.md, rozdz. 6.1; docs/model-danych.md).
   - DICT_CasPool – „Pule narzutu CAS” (globalny): pula → kolejność kolumny w raporcie kosztów CES, rodzaj (Koszt –
     TOTAL COST, FCCOM – TOTAL FCCOM).
   - DICT_CasBurdenFormula – „Formuły narzutów CAS” (globalny): grupa narzutowa + pula → podstawa (składniki
     „Base cost elements” i pule połączone „ + ”); narzut = podstawa × wskaźnik puli. Wiersz = pula naliczana w grupie.
   - DICT_CasBurdenGroup – bez kolumn pul i BaseInGsGa (migracja 029): grupa i opis; pule i podstawy – formuły.
   Dane startowe – migracja 032. Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo
   pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 031_formuly_narzutow_cas.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasPool]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_CasPool] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_CasPool] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Pool             NVARCHAR(60)      NOT NULL,
    SortOrder        INT               NOT NULL,
    Kind             NVARCHAR(20)      NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_CasPool_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_CasPool_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_CasPool_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasPool]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_CasPool_Key] ON [$(Schema)].[$(Prefix)DICT_CasPool] (Project, Pool) WHERE SupersededAt IS NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenFormula]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_CasBurdenFormula] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_CasBurdenFormula] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    BurdenGroup      NVARCHAR(50)      NOT NULL,
    Pool             NVARCHAR(60)      NOT NULL,
    Basis            NVARCHAR(2000)    NOT NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_CasBurdenFormula_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_CasBurdenFormula_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_CasBurdenFormula_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenFormula]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_CasBurdenFormula_Key] ON [$(Schema)].[$(Prefix)DICT_CasBurdenFormula] (Project, BurdenGroup, Pool) WHERE SupersededAt IS NULL;
GO

/* Grupa narzutowa: pule i podstawy przechodzą do formuł – kolumny 029 usuwane. */
DECLARE @column SYSNAME, @sql NVARCHAR(400);
DECLARE dropped CURSOR LOCAL FAST_FORWARD FOR
    SELECT c.name FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenGroup]')
      AND c.name IN (N'BaseInGsGa', N'MfgFringe', N'MfgOh', N'EngFringe', N'EngOh', N'MtlFringe', N'MtlHandling', N'Mmp', N'Gs', N'GsFringe',
                     N'MsvFringe', N'Ga', N'Fd', N'FccomMfg', N'FccomEng', N'FccomMtl', N'FccomGs', N'FccomGa');
OPEN dropped;
FETCH NEXT FROM dropped INTO @column;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER TABLE [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] DROP COLUMN ' + QUOTENAME(@column) + N';';
    EXEC (@sql);
    FETCH NEXT FROM dropped INTO @column;
END;
CLOSE dropped;
DEALLOCATE dropped;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 31)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (31, N'031_formuly_narzutow_cas.sql', '0.47.0');
GO
