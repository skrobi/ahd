/* PZL-EV – migracja 029: narzuty CAS do raportu kosztów CES (docs/slowniki.md, rozdz. 2 i 6; docs/model-danych.md).
   - DICT_CostCategory.BurdenGroup – „Grupa narzutowa CAS” elementu kosztowego (globalny; zmiana projektu ma
     pierwszeństwo – słownik efektywny).
   - DICT_CasBurdenGroup – „Grupy narzutowe CAS” (globalny): grupa → pule narzutu stosowane do kosztu elementu
     (po jednej kolumnie BIT na pulę) i czy koszt bazowy wchodzi do podstawy GS / G&A (BaseInGsGa).
   - DICT_CasBurdenRate – „Wskaźniki narzutu CAS” (globalny): pula → wskaźnik obowiązujący od – do.
   Dane startowe – migracja 030. Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo
   pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 029_narzuty_cas.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_CostCategory]', N'BurdenGroup') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_CostCategory] ADD BurdenGroup NVARCHAR(50) NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenGroup]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_CasBurdenGroup] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    BurdenGroup      NVARCHAR(50)      NOT NULL,
    Description      NVARCHAR(400)     NULL,
    BaseInGsGa       BIT               NULL,
    MfgFringe        BIT               NULL,
    MfgOh            BIT               NULL,
    EngFringe        BIT               NULL,
    EngOh            BIT               NULL,
    MtlFringe        BIT               NULL,
    MtlHandling      BIT               NULL,
    Mmp              BIT               NULL,
    Gs               BIT               NULL,
    GsFringe         BIT               NULL,
    MsvFringe        BIT               NULL,
    Ga               BIT               NULL,
    Fd               BIT               NULL,
    FccomMfg         BIT               NULL,
    FccomEng         BIT               NULL,
    FccomMtl         BIT               NULL,
    FccomGs          BIT               NULL,
    FccomGa          BIT               NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_CasBurdenGroup_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_CasBurdenGroup_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_CasBurdenGroup_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenGroup]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_CasBurdenGroup_Key] ON [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] (Project, BurdenGroup) WHERE SupersededAt IS NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenRate]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_CasBurdenRate] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_CasBurdenRate] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Pool             NVARCHAR(60)      NOT NULL,
    ValidFrom        DATE              NOT NULL,
    ValidTo          DATE              NULL,
    Rate             DECIMAL(38,18)    NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_CasBurdenRate_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_CasBurdenRate_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_CasBurdenRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenRate]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_CasBurdenRate_Key] ON [$(Schema)].[$(Prefix)DICT_CasBurdenRate] (Project, Pool, ValidFrom) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 29)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (29, N'029_narzuty_cas.sql', '0.46.0');
GO
