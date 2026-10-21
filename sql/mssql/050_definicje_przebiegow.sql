/* PZL-EV – migracja 050: definicje przebiegów (workflow) i przebieg etapowy (docs/pipeline-fazy.md, rozdz. 1.1–1.2 i 4.1).
   - DICT_RunDefinition – „Definicje przebiegów” (globalny): definicja, typ programu, program (NULL – wszystkie programy typu),
     wariant przebiegu, aktywna.
   - DICT_RunStep – „Etapy przebiegów”: definicja, kolejność, etap, akcja (import źródeł / przypięcie stanu / kontrole /
     zatwierdzenie), bramka przy WARNING (potwierdzenie / informacja), zatwierdzenie ręczne.
   - DICT_RunStepElement – „Elementy kontrolne etapów”: definicja, etap, element (reguła katalogu albo „Uzgodnienie: nazwa”),
     aktywny, poziom, próg.
   - META_RunStep – etapy uruchomienia przebiegu: status (Oczekuje, Do wykonania, W toku, Zakończony, Wymaga akcji, Błąd,
     Nieaktualny), kto i kiedy, liczba błędów i ostrzeżeń, potwierdzenie z komentarzem.
   - META_RunPin.Definition – definicja przebiegu użyta w uruchomieniu (JSON); RUN_Issue.StepNo – etap wyniku kontroli.
   Słowniki „Kontrole przebiegu” (047) i „Reguły uzgodnień” (049) zostają: ustawienia domyślne reguł i biblioteka reguł
   uzgodnień. Bez danych startowych – bez definicji obowiązuje definicja wbudowana aplikacji. Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 050_definicje_przebiegow.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunDefinition]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_RunDefinition] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_RunDefinition] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Code             NVARCHAR(100)     NOT NULL,
    ProjectType      VARCHAR(20)       NOT NULL,
    ProgramCode      VARCHAR(20)       NULL,
    RunKind          NVARCHAR(20)      NOT NULL,
    IsActive         BIT               NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_RunDefinition_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_RunDefinition_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_RunDefinition_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunDefinition]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_RunDefinition_Key] ON [$(Schema)].[$(Prefix)DICT_RunDefinition] (Project, Code) WHERE SupersededAt IS NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunStep]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_RunStep] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_RunStep] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Definition       NVARCHAR(100)     NOT NULL,
    StepNo           INT               NOT NULL,
    Name             NVARCHAR(100)     NOT NULL,
    Action           NVARCHAR(40)      NOT NULL,
    WarningGate      NVARCHAR(20)      NOT NULL,
    Approval         BIT               NOT NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_RunStep_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_RunStep_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_RunStep_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunStep]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_RunStep_Key] ON [$(Schema)].[$(Prefix)DICT_RunStep] (Project, Definition, StepNo) WHERE SupersededAt IS NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunStepElement]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_RunStepElement] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_RunStepElement] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Definition       NVARCHAR(100)     NOT NULL,
    Step             NVARCHAR(100)     NOT NULL,
    Element          NVARCHAR(250)     NOT NULL,
    IsActive         BIT               NOT NULL,
    Level            NVARCHAR(10)      NULL,
    Threshold        DECIMAL(28,8)     NULL,
    Description      NVARCHAR(400)     NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_RunStepElement_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_RunStepElement_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_RunStepElement_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunStepElement]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_RunStepElement_Key] ON [$(Schema)].[$(Prefix)DICT_RunStepElement] (Project, Definition, Step, Element) WHERE SupersededAt IS NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_RunStep]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_RunStep] (
    PinId        BIGINT               NOT NULL CONSTRAINT [FK_$(Prefix)META_RunStep_Pin] REFERENCES [$(Schema)].[$(Prefix)META_RunPin] (PinId),
    StepNo       INT                  NOT NULL,
    Name         NVARCHAR(100)        NOT NULL,
    Action       NVARCHAR(40)         NOT NULL,
    Status       NVARCHAR(30)         NOT NULL,
    StartedAt    DATETIMEOFFSET(7)    NULL,
    FinishedAt   DATETIMEOFFSET(7)    NULL,
    ActedBy      NVARCHAR(128)        NULL,
    Errors       INT                  NOT NULL CONSTRAINT [DF_$(Prefix)META_RunStep_Errors] DEFAULT 0,
    Warnings     INT                  NOT NULL CONSTRAINT [DF_$(Prefix)META_RunStep_Warnings] DEFAULT 0,
    Comment      NVARCHAR(2000)       NULL,
    CONSTRAINT [PK_$(Prefix)META_RunStep] PRIMARY KEY (PinId, StepNo)
);
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_RunPin]', N'Definition') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)META_RunPin] ADD Definition NVARCHAR(MAX) NULL;
GO
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)RUN_Issue]', N'StepNo') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)RUN_Issue] ADD StepNo INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 50)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (50, N'050_definicje_przebiegow.sql', '0.57.0');
GO
