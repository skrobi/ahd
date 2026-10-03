/* PZL-EV – migracja 005: mapowanie CES ↔ P1S (docs/mapowanie-ces-p1s.md).
   - Parser MAPOWANIA i jego tabela CAN_MappingReport: raport mapowań SAP↔CES trafia do bazy importem z Excela
     (definicję źródła z prefiksem nazwy pliku dodaje się w Administracji). Mapowanie czyta najnowszy zaimportowany
     plik. Kolumny pliku jak w docs/mapowanie-ces-p1s.md, rozdz. 2; parser czyta kolumny potrzebne do rozstrzygania,
     pozostałe zostają w wierszach surowych.
   - DICT_MappingCorrection: korekty mapowania (elementu CES i projektu CES) z historią wersji, uzasadnieniem
     i okresem obowiązywania (ValidFrom / ValidTo – usunięcie korekty zamyka ValidTo).
   Struktura P1S (LOG.WBS, LOG.WBS_DIC) nie jest kopiowana – aplikacja czyta ją z PZLPROD (pzl-ev.json, PzlProd).

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) albo
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 005_mapowanie_ces_p1s.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------- Tabela parsera MAPOWANIA (jak zakłada ją zapis parsera w aplikacji – SqlCanonical) ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_MappingReport]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)CAN_MappingReport] (
    FileId BIGINT NOT NULL CONSTRAINT [FK_$(Prefix)CAN_MappingReport_File] REFERENCES [$(Schema)].[$(Prefix)META_SourceFile] (FileId),
    RowNumber INT NOT NULL,
    ParserVersion INT NOT NULL,
    [Src] NVARCHAR(10) NULL,
    [Pspnr] NVARCHAR(50) NULL,
    [PspnrSap] NVARCHAR(50) NULL,
    [PspnrCes] NVARCHAR(50) NULL,
    [PspnrParent] NVARCHAR(50) NULL,
    [Project] NVARCHAR(100) NULL,
    [ProjectSap] NVARCHAR(100) NULL,
    [ProjectCes] NVARCHAR(100) NULL,
    [Wbs] NVARCHAR(100) NULL,
    [WbsSap] NVARCHAR(100) NULL,
    [WbsCes] NVARCHAR(100) NULL,
    CONSTRAINT [PK_$(Prefix)CAN_MappingReport] PRIMARY KEY (FileId, RowNumber, ParserVersion) WITH (DATA_COMPRESSION = PAGE)
);
GO

/* ---------- Korekty mapowania ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_MappingCorrection]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_MappingCorrection] (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_MappingCorrection] PRIMARY KEY,
    RowId            BIGINT            NOT NULL,
    Version          INT               NOT NULL,
    Kind             VARCHAR(10)       NOT NULL CONSTRAINT [CK_$(Prefix)DICT_MappingCorrection_Kind] CHECK (Kind IN ('ELEMENT', 'PROJEKT')),
    CesKey           NVARCHAR(100)     NOT NULL,   -- element CES (WBS Element) albo projekt CES (Project Definition)
    TargetPspnr      NVARCHAR(50)      NOT NULL,   -- cel P1S: LOG.WBS.PSPNR
    TargetWbs        NVARCHAR(100)     NOT NULL,   -- kod WBS celu w chwili zapisu
    PreviousTarget   NVARCHAR(200)     NULL,       -- przypisanie przed korektą (status i cel) w chwili zapisu
    Justification    NVARCHAR(1000)    NULL,       -- wymagane przy zmianie przypisania z raportu
    ValidFrom        DATE              NOT NULL,
    ValidTo          DATE              NULL,       -- usunięcie korekty: data zamknięcia
    RecordedAt       DATETIMEOFFSET(7) NOT NULL,
    RecordedBy       NVARCHAR(128)     NOT NULL,
    SupersededAt     DATETIMEOFFSET(7) NULL,
    SupersededBy     NVARCHAR(128)     NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)DICT_MappingCorrection_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_MappingCorrection_Version] UNIQUE (RowId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_MappingCorrection_Active' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_MappingCorrection]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_MappingCorrection_Active] ON [$(Schema)].[$(Prefix)DICT_MappingCorrection] (Kind, CesKey)
    WHERE SupersededAt IS NULL AND ValidTo IS NULL;
GO

/* ---------- Parser MAPOWANIA ---------- */

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();
DECLARE @fields NVARCHAR(MAX) = CAST(N'' AS NVARCHAR(MAX))
  + N'[{"Field":"Src","Column":"src","Type":"text","Length":10,"PadDigits":null,"Required":true},{"Field":"Pspnr","C'
  + N'olumn":"pspnr","Type":"text","Length":50,"PadDigits":null,"Required":false},{"Field":"PspnrSap","Column":"pspn'
  + N'r_sap","Type":"text","Length":50,"PadDigits":null,"Required":false},{"Field":"PspnrCes","Column":"pspnr_ces","'
  + N'Type":"text","Length":50,"PadDigits":null,"Required":false},{"Field":"PspnrParent","Column":"pspnr_parent","Ty'
  + N'pe":"text","Length":50,"PadDigits":null,"Required":false},{"Field":"Project","Column":"project","Type":"text",'
  + N'"Length":100,"PadDigits":null,"Required":false},{"Field":"ProjectSap","Column":"project_sap","Type":"text","Le'
  + N'ngth":100,"PadDigits":null,"Required":false},{"Field":"ProjectCes","Column":"project_ces","Type":"text","Lengt'
  + N'h":100,"PadDigits":null,"Required":false},{"Field":"Wbs","Column":"wbs","Type":"text","Length":100,"PadDigits"'
  + N':null,"Required":false},{"Field":"WbsSap","Column":"wbs_sap","Type":"text","Length":100,"PadDigits":null,"Requ'
  + N'ired":false},{"Field":"WbsCes","Column":"wbs_ces","Type":"text","Length":100,"PadDigits":null,"Required":false'
  + N'}]';

INSERT INTO [$(Schema)].[$(Prefix)META_Parser] (ParserId, Version, Code, Name, TableName, Fields, Active, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, 'MAPOWANIA', N'Raport mapowań SAP↔CES', 'MappingReport', @fields, 1, @at, @by
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Parser] WHERE SupersededAt IS NULL AND Code = 'MAPOWANIA');

IF @@ROWCOUNT > 0
INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
VALUES (@at, @by, N'Migracje', N'Migracja 005 – parser MAPOWANIA (raport mapowań SAP↔CES z Excela, tabela CAN_MappingReport) i korekty mapowania CES ↔ P1S');

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 5)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (5, N'005_mapowanie_ces_p1s.sql', '0.14.0');
GO
