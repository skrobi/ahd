/* PZL-EV – migracja 001: tabele etapu 1 (import i parsowanie RABIT, słowniki globalne, projekty, dziennik, problemy).
   Bez metodologii EV – kolejne tabele dochodzą kolejnymi migracjami (002, 003…).

   Nazwy: [$(Schema)].[$(Prefix)<WARSTWA>_<Nazwa>], np. [FINOP].[PZLEV_META_ImportBatch] (docs/model-danych.md, rozdz. 2).
   Zmienne: Schema (np. FINOP), Prefix (np. PZLEV_) – z pzl-ev.json (Environments.<Env>.Sql).
   Uruchomienie: aplikacja (przy starcie, za zgodą użytkownika) albo
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -v Schema=FINOP Prefix=PZLEV_ -i 001_etap1_import_slowniki_projekty.sql
   Skrypt jest idempotentny: tworzy tylko brakujące obiekty i zapisuje wersję schematu 1.
   Czas: DATETIMEOFFSET(7) – pełna precyzja (data modyfikacji pliku porównywana dokładnie); kwoty i ilości: DECIMAL(28,8); DbLogin – konto, którym faktycznie zapisano wiersz. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- wymagane dla indeksów filtrowanych (sqlcmd bez -I ma OFF)
GO

IF SCHEMA_ID(N'$(Schema)') IS NULL EXEC(N'CREATE SCHEMA [$(Schema)]');
GO

/* ---------- META: wersja schematu, identyfikatory wierszy logicznych ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SchemaVersion]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_SchemaVersion] (
    Version          INT               NOT NULL CONSTRAINT [PK_$(Prefix)META_SchemaVersion] PRIMARY KEY,
    Script           NVARCHAR(200)     NOT NULL,
    MinAppVersion    VARCHAR(30)       NOT NULL,
    AppliedAt        DATETIMEOFFSET(7) NOT NULL CONSTRAINT [DF_$(Prefix)META_SchemaVersion_At] DEFAULT SYSDATETIMEOFFSET(),
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_SchemaVersion_Login] DEFAULT ORIGINAL_LOGIN()
);

/* Identyfikator wiersza logicznego (definicja, lokalizacja, projekt, węzeł, wiersz słownika) – stały między wersjami. */
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_LogicalId]') IS NULL
CREATE SEQUENCE [$(Schema)].[$(Prefix)META_LogicalId] AS BIGINT START WITH 1 INCREMENT BY 1;
GO

/* ---------- META: konfiguracja importu (historia na osi technicznej) ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceDefinition]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_SourceDefinition] (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_SourceDefinition] PRIMARY KEY,
    DefinitionId     BIGINT            NOT NULL,
    Version          INT               NOT NULL,
    Code             VARCHAR(60)       NOT NULL,
    Prefix           NVARCHAR(100)     NOT NULL,
    ReportType       NVARCHAR(200)     NOT NULL,
    Columns          NVARCHAR(MAX)     NOT NULL,   -- JSON: oczekiwane kolumny w kolejności z pliku
    Signature        VARCHAR(16)       NOT NULL,
    Parser           VARCHAR(30)       NOT NULL,
    ParserVersion    INT               NOT NULL,
    Active           BIT               NOT NULL,
    RecordedAt       DATETIMEOFFSET(7) NOT NULL,
    RecordedBy       NVARCHAR(128)     NOT NULL,
    SupersededAt     DATETIMEOFFSET(7) NULL,
    SupersededBy     NVARCHAR(128)     NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_SourceDefinition_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)META_SourceDefinition_Version] UNIQUE (DefinitionId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)META_SourceDefinition_Code' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceDefinition]'))
CREATE UNIQUE INDEX [UX_$(Prefix)META_SourceDefinition_Code] ON [$(Schema)].[$(Prefix)META_SourceDefinition] (Code) WHERE SupersededAt IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)META_SourceDefinition_Prefix' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceDefinition]'))
CREATE UNIQUE INDEX [UX_$(Prefix)META_SourceDefinition_Prefix] ON [$(Schema)].[$(Prefix)META_SourceDefinition] (Prefix) WHERE SupersededAt IS NULL;

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceLocation]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_SourceLocation] (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_SourceLocation] PRIMARY KEY,
    LocationId       BIGINT            NOT NULL,
    Version          INT               NOT NULL,
    Name             NVARCHAR(200)     NOT NULL,
    Path             NVARCHAR(1000)    NOT NULL,
    Active           BIT               NOT NULL,
    RecordedAt       DATETIMEOFFSET(7) NOT NULL,
    RecordedBy       NVARCHAR(128)     NOT NULL,
    SupersededAt     DATETIMEOFFSET(7) NULL,
    SupersededBy     NVARCHAR(128)     NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_SourceLocation_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)META_SourceLocation_Version] UNIQUE (LocationId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)META_SourceLocation_Name' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceLocation]'))
CREATE UNIQUE INDEX [UX_$(Prefix)META_SourceLocation_Name] ON [$(Schema)].[$(Prefix)META_SourceLocation] (Name) WHERE SupersededAt IS NULL;
GO

/* ---------- META: import (G1) ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_ImportBatch]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_ImportBatch] (
    BatchId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_ImportBatch] PRIMARY KEY,
    StartedAt        DATETIMEOFFSET(7) NOT NULL,
    FinishedAt       DATETIMEOFFSET(7) NULL,
    UserName         NVARCHAR(128)     NOT NULL,
    Machine          NVARCHAR(128)     NOT NULL,
    AppVersion       VARCHAR(30)       NOT NULL,
    Files            INT               NOT NULL CONSTRAINT [DF_$(Prefix)META_ImportBatch_Files] DEFAULT 0,
    Imported         INT               NOT NULL CONSTRAINT [DF_$(Prefix)META_ImportBatch_Imported] DEFAULT 0,
    Skipped          INT               NOT NULL CONSTRAINT [DF_$(Prefix)META_ImportBatch_Skipped] DEFAULT 0,
    Duplicates       INT               NOT NULL CONSTRAINT [DF_$(Prefix)META_ImportBatch_Duplicates] DEFAULT 0,
    Unrecognized     INT               NOT NULL CONSTRAINT [DF_$(Prefix)META_ImportBatch_Unrecognized] DEFAULT 0,
    Errors           INT               NOT NULL CONSTRAINT [DF_$(Prefix)META_ImportBatch_Errors] DEFAULT 0,
    Status           NVARCHAR(60)      NOT NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_ImportBatch_Login] DEFAULT ORIGINAL_LOGIN()
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)META_ImportBatch_Status' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_ImportBatch]'))
CREATE INDEX [IX_$(Prefix)META_ImportBatch_Status] ON [$(Schema)].[$(Prefix)META_ImportBatch] (Status);

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceFile]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_SourceFile] (
    FileId           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_SourceFile] PRIMARY KEY,
    Sha256           CHAR(64)          NOT NULL CONSTRAINT [UQ_$(Prefix)META_SourceFile_Sha256] UNIQUE,
    Location         NVARCHAR(1000)    NOT NULL,
    FileName         NVARCHAR(400)     NOT NULL,
    SourceCode       VARCHAR(60)       NOT NULL,
    Size             BIGINT            NOT NULL,
    ModifiedAt       DATETIMEOFFSET(7) NOT NULL,   -- data raportu: modyfikacja pliku w RABIT
    FileType         VARCHAR(10)       NOT NULL,
    Sheet            NVARCHAR(128)     NULL,
    Encoding         VARCHAR(30)       NULL,
    Delimiter        NVARCHAR(5)       NULL,
    Columns          NVARCHAR(MAX)     NOT NULL,   -- JSON: nagłówki
    Signature        VARCHAR(16)       NOT NULL,
    DataRows         INT               NOT NULL,
    BatchId          BIGINT            NOT NULL CONSTRAINT [FK_$(Prefix)META_SourceFile_Batch] REFERENCES [$(Schema)].[$(Prefix)META_ImportBatch] (BatchId),
    ImportedAt       DATETIMEOFFSET(7) NOT NULL,
    ImportedBy       NVARCHAR(128)     NOT NULL,
    CanonicalStatus  NVARCHAR(400)     NOT NULL,   -- utworzone albo powód braku danych kanonicznych
    CanonicalRows    INT               NOT NULL,
    ParserVersion    INT               NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_SourceFile_Login] DEFAULT ORIGINAL_LOGIN()
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)META_SourceFile_SourceCode' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceFile]'))
CREATE INDEX [IX_$(Prefix)META_SourceFile_SourceCode] ON [$(Schema)].[$(Prefix)META_SourceFile] (SourceCode, ImportedAt);

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceFileSeen]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_SourceFileSeen] (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_SourceFileSeen] PRIMARY KEY,
    BatchId          BIGINT            NOT NULL CONSTRAINT [FK_$(Prefix)META_SourceFileSeen_Batch] REFERENCES [$(Schema)].[$(Prefix)META_ImportBatch] (BatchId),
    Location         NVARCHAR(1000)    NOT NULL,
    FileName         NVARCHAR(400)     NOT NULL,
    Size             BIGINT            NOT NULL,
    ModifiedAt       DATETIMEOFFSET(7) NOT NULL,
    Sha256           CHAR(64)          NULL,
    Decision         NVARCHAR(30)      NOT NULL,   -- zaimportowany / pominięty / duplikat / nierozpoznany / błąd
    SourceCode       VARCHAR(60)       NULL,
    DataRows         INT               NULL,
    Description      NVARCHAR(4000)    NOT NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_SourceFileSeen_Login] DEFAULT ORIGINAL_LOGIN()
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)META_SourceFileSeen_File' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceFileSeen]'))
CREATE INDEX [IX_$(Prefix)META_SourceFileSeen_File] ON [$(Schema)].[$(Prefix)META_SourceFileSeen] (FileName, Id) INCLUDE (Location, Decision, Size, ModifiedAt, Sha256);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)META_SourceFileSeen_Batch' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceFileSeen]'))
CREATE INDEX [IX_$(Prefix)META_SourceFileSeen_Batch] ON [$(Schema)].[$(Prefix)META_SourceFileSeen] (BatchId);
GO

/* ---------- STG: wiersze surowe ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)STG_RawRow]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)STG_RawRow] (
    FileId           BIGINT            NOT NULL CONSTRAINT [FK_$(Prefix)STG_RawRow_File] REFERENCES [$(Schema)].[$(Prefix)META_SourceFile] (FileId),
    RowNumber        INT               NOT NULL,
    Data             NVARCHAR(MAX)     NOT NULL,   -- JSON: wartości wiersza w kolejności kolumn
    CONSTRAINT [PK_$(Prefix)STG_RawRow] PRIMARY KEY (FileId, RowNumber) WITH (DATA_COMPRESSION = PAGE)
);
GO

/* ---------- CAN: dane kanoniczne ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_Actuals]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)CAN_Actuals] (
    FileId                      BIGINT          NOT NULL CONSTRAINT [FK_$(Prefix)CAN_Actuals_File] REFERENCES [$(Schema)].[$(Prefix)META_SourceFile] (FileId),
    RowNumber                   INT             NOT NULL,
    ParserVersion               INT             NOT NULL,
    ProjectDefinition           NVARCHAR(100)   NULL,
    WbsElement                  NVARCHAR(100)   NOT NULL,
    CostElement                 VARCHAR(10)     NULL,
    CostElementDescr            NVARCHAR(400)   NULL,
    CostElementName             NVARCHAR(400)   NULL,
    CoObjectName                NVARCHAR(400)   NULL,
    TransactionCurrency         VARCHAR(10)     NULL,
    ValueTranCurr               DECIMAL(28,8)   NULL,
    ObjectCurrency              VARCHAR(10)     NULL,
    ValueObjCrcy                DECIMAL(28,8)   NULL,
    ReportCurrency              VARCHAR(10)     NULL,
    ValueRepCur                 DECIMAL(28,8)   NULL,
    TotalQuantity               DECIMAL(28,8)   NULL,
    PartnerCctr                 NVARCHAR(100)   NULL,
    SourceObjectName            NVARCHAR(400)   NULL,
    PartnerObjectClass          NVARCHAR(100)   NULL,
    PartnerObject               NVARCHAR(200)   NULL,
    OriginalMaterial            NVARCHAR(100)   NULL,
    OriginalMaterialDescription NVARCHAR(400)   NULL,
    FiscalYear                  INT             NOT NULL,
    CreatedOn                   DATE            NULL,
    Period                      INT             NOT NULL,
    CONSTRAINT [PK_$(Prefix)CAN_Actuals] PRIMARY KEY (FileId, RowNumber, ParserVersion) WITH (DATA_COMPRESSION = PAGE)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)CAN_Actuals_Wbs' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_Actuals]'))
CREATE INDEX [IX_$(Prefix)CAN_Actuals_Wbs] ON [$(Schema)].[$(Prefix)CAN_Actuals] (WbsElement, FiscalYear, Period)
    INCLUDE (CostElement, ValueObjCrcy, ValueRepCur, TotalQuantity) WITH (DATA_COMPRESSION = PAGE);
GO

/* ---------- META: dziennik i problemy ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Journal]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_Journal] (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_Journal] PRIMARY KEY,
    At               DATETIMEOFFSET(7) NOT NULL,
    UserName         NVARCHAR(128)     NOT NULL,
    Area             NVARCHAR(60)      NOT NULL,
    Scope            NVARCHAR(200)     NULL,
    Message          NVARCHAR(4000)    NOT NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_Journal_Login] DEFAULT ORIGINAL_LOGIN()
);

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Problem]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_Problem] (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_Problem] PRIMARY KEY,
    At               DATETIMEOFFSET(7) NOT NULL,
    Level            VARCHAR(10)       NOT NULL,   -- Error / Warning / Pass
    CheckName        NVARCHAR(100)     NOT NULL,
    Area             NVARCHAR(60)      NOT NULL,
    Element          NVARCHAR(400)     NULL,
    Message          NVARCHAR(4000)    NOT NULL,
    Reference        NVARCHAR(100)     NULL,       -- np. import:12
    Resolved         BIT               NOT NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_Problem_Login] DEFAULT ORIGINAL_LOGIN()
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)META_Problem_Reference' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Problem]'))
CREATE INDEX [IX_$(Prefix)META_Problem_Reference] ON [$(Schema)].[$(Prefix)META_Problem] (Reference);
GO

/* ---------- META: projekty (F4.1) i nakładka Performance Objectives (F4.2) ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Project]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_Project] (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_Project] PRIMARY KEY,
    ProjectId        BIGINT            NOT NULL,
    Version          INT               NOT NULL,
    Code             VARCHAR(20)       NOT NULL,
    Name             NVARCHAR(200)     NOT NULL,
    ProjectType      VARCHAR(20)       NOT NULL CONSTRAINT [CK_$(Prefix)META_Project_Type] CHECK (ProjectType IN ('SAC', 'CAS', 'WEWNETRZNY')),
    Active           BIT               NOT NULL,
    RecordedAt       DATETIMEOFFSET(7) NOT NULL,
    RecordedBy       NVARCHAR(128)     NOT NULL,
    SupersededAt     DATETIMEOFFSET(7) NULL,
    SupersededBy     NVARCHAR(128)     NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_Project_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)META_Project_Version] UNIQUE (ProjectId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)META_Project_Code' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Project]'))
CREATE UNIQUE INDEX [UX_$(Prefix)META_Project_Code] ON [$(Schema)].[$(Prefix)META_Project] (Code) WHERE SupersededAt IS NULL;

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_PerformanceObjective]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_PerformanceObjective] (
    Id                    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_PerformanceObjective] PRIMARY KEY,
    NodeId                BIGINT            NOT NULL,
    Version               INT               NOT NULL,
    Project               VARCHAR(20)       NOT NULL,   -- kod projektu
    ParentNodeId          BIGINT            NULL,
    NodeLevel             INT               NOT NULL,   -- Level z Excela (1 = korzeń)
    SortOrder             INT               NOT NULL,
    IsVirtual             BIT               NOT NULL,   -- węzeł dodany w PZL-EV (bez elementu CES)
    ProjectDefinition     NVARCHAR(100)     NULL,
    WbsElement            NVARCHAR(100)     NULL,
    Name                  NVARCHAR(400)     NOT NULL,
    PersonResponsible     NVARCHAR(200)     NULL,
    ProfitCenter          NVARCHAR(100)     NULL,
    LegacyWbs             NVARCHAR(200)     NULL,
    PerformanceObligation NVARCHAR(100)     NULL,
    SacObjNumber          NVARCHAR(60)      NULL,
    IsStatistical         BIT               NOT NULL,
    IsAcctAsstElement     BIT               NOT NULL,
    RecordedAt            DATETIMEOFFSET(7) NOT NULL,
    RecordedBy            NVARCHAR(128)     NOT NULL,
    SupersededAt          DATETIMEOFFSET(7) NULL,
    SupersededBy          NVARCHAR(128)     NULL,
    DbLogin               NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_PerformanceObjective_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)META_PerformanceObjective_Version] UNIQUE (NodeId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)META_PerformanceObjective_Wbs' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_PerformanceObjective]'))
CREATE UNIQUE INDEX [UX_$(Prefix)META_PerformanceObjective_Wbs] ON [$(Schema)].[$(Prefix)META_PerformanceObjective] (Project, WbsElement)
    WHERE SupersededAt IS NULL AND WbsElement IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)META_PerformanceObjective_Tree' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_PerformanceObjective]'))
CREATE INDEX [IX_$(Prefix)META_PerformanceObjective_Tree] ON [$(Schema)].[$(Prefix)META_PerformanceObjective] (Project, ParentNodeId, SortOrder);
GO

/* ---------- DICT: słowniki globalne i projektu (Project NULL = globalny); historia na osi technicznej ----------
   RowId – wiersz logiczny (sekwencja META_LogicalId); unikalny klucz biznesowy wśród bieżących wersji. */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_Calendar]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_Calendar] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_Calendar] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Year             INT               NOT NULL,
    Week             INT               NOT NULL,
    Period           VARCHAR(7)        NOT NULL,   -- RRRR-MM
    DateFrom         DATE              NOT NULL,
    DateTo           DATE              NOT NULL,
    IsClosing        BIT               NOT NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_Calendar_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_Calendar_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_Calendar_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_Calendar]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_Calendar_Key] ON [$(Schema)].[$(Prefix)DICT_Calendar] (Project, Year, Week) WHERE SupersededAt IS NULL;

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_DepartmentRate]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_DepartmentRate] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_DepartmentRate] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Department       NVARCHAR(100)     NOT NULL,
    Year             INT               NOT NULL,
    LaborRate        DECIMAL(28,8)     NOT NULL,
    Overhead         DECIMAL(28,8)     NOT NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_DepartmentRate_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_DepartmentRate_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_DepartmentRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_DepartmentRate]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_DepartmentRate_Key] ON [$(Schema)].[$(Prefix)DICT_DepartmentRate] (Project, Department, Year) WHERE SupersededAt IS NULL;

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_FxRate]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_FxRate] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_FxRate] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Currency         VARCHAR(10)       NOT NULL,
    Period           VARCHAR(7)        NOT NULL,   -- RRRR-MM
    Rate             DECIMAL(28,8)     NOT NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_FxRate_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_FxRate_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_FxRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_FxRate]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_FxRate_Key] ON [$(Schema)].[$(Prefix)DICT_FxRate] (Project, Currency, Period) WHERE SupersededAt IS NULL;

/* Cost Category: Project NULL – słownik globalny; Project = kod – zmiany i uzupełnienia w projekcie (F4.3). */
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CostCategory]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_CostCategory] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_CostCategory] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    CostElement      VARCHAR(10)       NOT NULL,
    Description      NVARCHAR(400)     NULL,
    Area             NVARCHAR(100)     NULL,
    CostCategory     NVARCHAR(100)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_CostCategory_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_CostCategory_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_CostCategory_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CostCategory]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_CostCategory_Key] ON [$(Schema)].[$(Prefix)DICT_CostCategory] (Project, CostElement) WHERE SupersededAt IS NULL;

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_Person]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_Person] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_Person] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    AdAccount        NVARCHAR(128)     NOT NULL,
    FullName         NVARCHAR(200)     NOT NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_Person_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_Person_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_Person_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_Person]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_Person_Key] ON [$(Schema)].[$(Prefix)DICT_Person] (Project, AdAccount) WHERE SupersededAt IS NULL;
GO

/* Słowniki projektu (F4.3; docs/slowniki.md, rozdz. 3). Stawki CAS – po ustaleniu zawartości (O37). */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_WpCam]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_WpCam] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_WpCam] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NOT NULL,
    P1sElement       NVARCHAR(100)     NOT NULL,
    Wp               NVARCHAR(100)     NOT NULL,
    Cam              NVARCHAR(128)     NOT NULL,
    CostCategory     NVARCHAR(100)     NULL,       -- Labor / Material / Subcontract (O24)
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_WpCam_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_WpCam_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_WpCam_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_WpCam]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_WpCam_Key] ON [$(Schema)].[$(Prefix)DICT_WpCam] (Project, P1sElement) WHERE SupersededAt IS NULL;

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_ScheduleBudget]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_ScheduleBudget] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_ScheduleBudget] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NOT NULL,
    Wp               NVARCHAR(100)     NOT NULL,
    BacHours         DECIMAL(28,8)     NULL,
    BacMaterial      DECIMAL(28,8)     NULL,
    PlannedStart     DATE              NULL,
    PlannedEnd       DATE              NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_ScheduleBudget_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_ScheduleBudget_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_ScheduleBudget_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_ScheduleBudget]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_ScheduleBudget_Key] ON [$(Schema)].[$(Prefix)DICT_ScheduleBudget] (Project, Wp) WHERE SupersededAt IS NULL;

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_Exclusion]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_Exclusion] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_Exclusion] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NOT NULL,
    CostElement      VARCHAR(10)       NULL,
    WbsElement       NVARCHAR(100)     NULL,
    PartnerObject    NVARCHAR(200)     NULL,
    Description      NVARCHAR(400)     NOT NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_Exclusion_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_Exclusion_Version] UNIQUE (RowId, Version),
    CONSTRAINT [CK_$(Prefix)DICT_Exclusion_Any] CHECK (CostElement IS NOT NULL OR WbsElement IS NOT NULL OR PartnerObject IS NOT NULL)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_Exclusion_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_Exclusion]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_Exclusion_Key] ON [$(Schema)].[$(Prefix)DICT_Exclusion] (Project, CostElement, WbsElement, PartnerObject) WHERE SupersededAt IS NULL;
GO

/* ---------- wersja schematu ---------- */

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 1)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (1, N'001_etap1_import_slowniki_projekty.sql', '0.8.0');
GO
