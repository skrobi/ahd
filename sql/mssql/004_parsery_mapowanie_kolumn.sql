/* PZL-EV – migracja 004: parsery jako dane i mapowanie kolumn (docs/zrodla-danych.md, rozdz. 2).
   - META_Parser: parser = tabela danych kanonicznych (CAN_<Tabela>) i jej pola – nazwa kolumny w bazie, nazwa kolumny
     w pliku (podpowiedź mapowania), typ, długość tekstu, dopełnianie zerami – z historią wersji. Edycja w aplikacji
     (Administracja → Parsery) zakłada tabelę albo dokłada w niej kolumny.
   - META_SourceDefinition.Mapping: kolumny pliku → pola parsera z oznaczeniem wymaganych (JSON).
   - CAN_Actuals: tabela parsera ACTUALS. O wymaganych polach decyduje mapowanie, więc kolumny dopuszczają NULL;
     nowe pola: Original Order Number, Item, Purchase order number, Invoice Number.
   - Parser ACTUALS (wersja 1) i mapowanie dla definicji o standardowym układzie ACTUALS (sygnatura 7c59f446fe9c3d5e,
     wymagane WBS Element, Fiscal Year, Period – jak dotychczas). Definicja o innym układzie dostaje mapowanie w aplikacji.

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) albo
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 004_parsery_mapowanie_kolumn.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Parser]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_Parser] (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)META_Parser] PRIMARY KEY,
    ParserId         BIGINT            NOT NULL,
    Version          INT               NOT NULL,
    Code             VARCHAR(30)       NOT NULL,
    Name             NVARCHAR(200)     NOT NULL,
    TableName        VARCHAR(40)       NOT NULL,   -- tabela danych kanonicznych: CAN_<TableName>
    Fields           NVARCHAR(MAX)     NOT NULL,   -- JSON: [{Field, Label, Type, Length, PadDigits}]
    Active           BIT               NOT NULL,
    RecordedAt       DATETIMEOFFSET(7) NOT NULL,
    RecordedBy       NVARCHAR(128)     NOT NULL,
    SupersededAt     DATETIMEOFFSET(7) NULL,
    SupersededBy     NVARCHAR(128)     NULL,
    DbLogin          NVARCHAR(128)     NOT NULL CONSTRAINT [DF_$(Prefix)META_Parser_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)META_Parser_Version] UNIQUE (ParserId, Version)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)META_Parser_Code' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Parser]'))
CREATE UNIQUE INDEX [UX_$(Prefix)META_Parser_Code] ON [$(Schema)].[$(Prefix)META_Parser] (Code) WHERE SupersededAt IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)META_Parser_Table' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Parser]'))
CREATE UNIQUE INDEX [UX_$(Prefix)META_Parser_Table] ON [$(Schema)].[$(Prefix)META_Parser] (TableName) WHERE SupersededAt IS NULL;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_SourceDefinition]', N'Mapping') IS NULL
ALTER TABLE [$(Schema)].[$(Prefix)META_SourceDefinition] ADD Mapping NVARCHAR(MAX) NULL;   -- JSON: [{Column, Field, Required}]
GO

/* ---------- CAN_Actuals → tabela parsera ACTUALS ---------- */

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)CAN_Actuals_Wbs' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_Actuals]'))
DROP INDEX [IX_$(Prefix)CAN_Actuals_Wbs] ON [$(Schema)].[$(Prefix)CAN_Actuals];
ALTER TABLE [$(Schema)].[$(Prefix)CAN_Actuals] ALTER COLUMN WbsElement NVARCHAR(100) NULL;
ALTER TABLE [$(Schema)].[$(Prefix)CAN_Actuals] ALTER COLUMN FiscalYear INT NULL;
ALTER TABLE [$(Schema)].[$(Prefix)CAN_Actuals] ALTER COLUMN Period INT NULL;
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)CAN_Actuals]', N'OriginalOrderNumber') IS NULL
ALTER TABLE [$(Schema)].[$(Prefix)CAN_Actuals] ADD OriginalOrderNumber NVARCHAR(100) NULL;
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)CAN_Actuals]', N'Item') IS NULL
ALTER TABLE [$(Schema)].[$(Prefix)CAN_Actuals] ADD Item NVARCHAR(50) NULL;
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)CAN_Actuals]', N'PurchaseOrderNumber') IS NULL
ALTER TABLE [$(Schema)].[$(Prefix)CAN_Actuals] ADD PurchaseOrderNumber NVARCHAR(100) NULL;
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)CAN_Actuals]', N'InvoiceNumber') IS NULL
ALTER TABLE [$(Schema)].[$(Prefix)CAN_Actuals] ADD InvoiceNumber NVARCHAR(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)CAN_Actuals_Wbs' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_Actuals]'))
CREATE INDEX [IX_$(Prefix)CAN_Actuals_Wbs] ON [$(Schema)].[$(Prefix)CAN_Actuals] (WbsElement, FiscalYear, Period)
    INCLUDE (CostElement, ValueObjCrcy, ValueRepCur, TotalQuantity) WITH (DATA_COMPRESSION = PAGE);
GO

/* ---------- Parser ACTUALS i mapowanie standardowego układu ---------- */

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();
DECLARE @fields NVARCHAR(MAX) = CAST(N'' AS NVARCHAR(MAX))
  + N'[{"Field":"ProjectDefinition","Label":"Project Definition","Type":"text","Length":100,"PadDigits":null},{"Fiel'
  + N'd":"WbsElement","Label":"WBS Element","Type":"text","Length":100,"PadDigits":null},{"Field":"CostElement","Lab'
  + N'el":"Cost Element","Type":"text","Length":10,"PadDigits":10},{"Field":"CostElementDescr","Label":"Cost element'
  + N' descr.","Type":"text","Length":400,"PadDigits":null},{"Field":"CostElementName","Label":"Cost element name","'
  + N'Type":"text","Length":400,"PadDigits":null},{"Field":"CoObjectName","Label":"CO object name","Type":"text","Le'
  + N'ngth":400,"PadDigits":null},{"Field":"TransactionCurrency","Label":"Transaction Currency","Type":"text","Lengt'
  + N'h":10,"PadDigits":null},{"Field":"ValueTranCurr","Label":"Value TranCurr","Type":"decimal","Length":null,"PadD'
  + N'igits":null},{"Field":"ObjectCurrency","Label":"Object Currency","Type":"text","Length":10,"PadDigits":null},{'
  + N'"Field":"ValueObjCrcy","Label":"Value in Obj. Crcy","Type":"decimal","Length":null,"PadDigits":null},{"Field":'
  + N'"ReportCurrency","Label":"Report currency","Type":"text","Length":10,"PadDigits":null},{"Field":"ValueRepCur",'
  + N'"Label":"Val.in rep.cur.","Type":"decimal","Length":null,"PadDigits":null},{"Field":"TotalQuantity","Label":"T'
  + N'otal Quantity","Type":"decimal","Length":null,"PadDigits":null},{"Field":"PartnerCctr","Label":"Partner-CCtr",'
  + N'"Type":"text","Length":100,"PadDigits":null},{"Field":"SourceObjectName","Label":"Source object name","Type":"'
  + N'text","Length":400,"PadDigits":null},{"Field":"PartnerObjectClass","Label":"Partner Object Class","Type":"text'
  + N'","Length":100,"PadDigits":null},{"Field":"PartnerObject","Label":"Partner object","Type":"text","Length":200,'
  + N'"PadDigits":null},{"Field":"OriginalMaterial","Label":"Original material","Type":"text","Length":100,"PadDigit'
  + N's":null},{"Field":"OriginalMaterialDescription","Label":"Original material description","Type":"text","Length"'
  + N':400,"PadDigits":null},{"Field":"OriginalOrderNumber","Label":"Original Order Number","Type":"text","Length":1'
  + N'00,"PadDigits":null},{"Field":"Item","Label":"Item","Type":"text","Length":50,"PadDigits":null},{"Field":"Purc'
  + N'haseOrderNumber","Label":"Purchase order number","Type":"text","Length":100,"PadDigits":null},{"Field":"Invoic'
  + N'eNumber","Label":"Invoice Number","Type":"text","Length":100,"PadDigits":null},{"Field":"FiscalYear","Label":"'
  + N'Fiscal Year","Type":"integer","Length":null,"PadDigits":null},{"Field":"CreatedOn","Label":"Created on","Type"'
  + N':"date","Length":null,"PadDigits":null},{"Field":"Period","Label":"Period","Type":"integer","Length":null,"Pad'
  + N'Digits":null}]';
DECLARE @mapping NVARCHAR(MAX) = CAST(N'' AS NVARCHAR(MAX))
  + N'[{"Column":"Project Definition","Field":"ProjectDefinition","Required":false},{"Column":"WBS Element","Field":'
  + N'"WbsElement","Required":true},{"Column":"Cost Element","Field":"CostElement","Required":false},{"Column":"Cost'
  + N' element descr.","Field":"CostElementDescr","Required":false},{"Column":"Cost element name","Field":"CostEleme'
  + N'ntName","Required":false},{"Column":"CO object name","Field":"CoObjectName","Required":false},{"Column":"Trans'
  + N'action Currency","Field":"TransactionCurrency","Required":false},{"Column":"Value TranCurr","Field":"ValueTran'
  + N'Curr","Required":false},{"Column":"Object Currency","Field":"ObjectCurrency","Required":false},{"Column":"Valu'
  + N'e in Obj. Crcy","Field":"ValueObjCrcy","Required":false},{"Column":"Report currency","Field":"ReportCurrency",'
  + N'"Required":false},{"Column":"Val.in rep.cur.","Field":"ValueRepCur","Required":false},{"Column":"Total Quantit'
  + N'y","Field":"TotalQuantity","Required":false},{"Column":"Partner-CCtr","Field":"PartnerCctr","Required":false},'
  + N'{"Column":"Source object name","Field":"SourceObjectName","Required":false},{"Column":"Partner Object Class","'
  + N'Field":"PartnerObjectClass","Required":false},{"Column":"Partner object","Field":"PartnerObject","Required":fa'
  + N'lse},{"Column":"Original material","Field":"OriginalMaterial","Required":false},{"Column":"Original material d'
  + N'escription","Field":"OriginalMaterialDescription","Required":false},{"Column":"Fiscal Year","Field":"FiscalYea'
  + N'r","Required":true},{"Column":"Created on","Field":"CreatedOn","Required":false},{"Column":"Period","Field":"P'
  + N'eriod","Required":true}]';

INSERT INTO [$(Schema)].[$(Prefix)META_Parser] (ParserId, Version, Code, Name, TableName, Fields, Active, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, 'ACTUALS', N'Koszty rzeczywiste CES (ACTUALS_*)', 'Actuals', @fields, 1, @at, @by
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Parser] WHERE SupersededAt IS NULL AND Code = 'ACTUALS');
DECLARE @parsers INT = @@ROWCOUNT;

UPDATE [$(Schema)].[$(Prefix)META_SourceDefinition] SET Mapping = @mapping, ParserVersion = 1
WHERE SupersededAt IS NULL AND Parser = 'ACTUALS' AND Mapping IS NULL AND Signature = '7c59f446fe9c3d5e';
DECLARE @mapped INT = @@ROWCOUNT;
DECLARE @unmapped INT = (SELECT COUNT(*) FROM [$(Schema)].[$(Prefix)META_SourceDefinition]
                         WHERE SupersededAt IS NULL AND Parser <> '' AND Mapping IS NULL);

IF @parsers + @mapped + @unmapped > 0
INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
VALUES (@at, @by, N'Migracje', CONCAT(N'Migracja 004 – parsery w bazie: nowe ', @parsers, N' (ACTUALS); mapowanie kolumn ustawione dla definicji: ', @mapped,
        CASE WHEN @unmapped > 0 THEN CONCAT(N'; definicje z parserem bez mapowania (do uzupełnienia w Administracji): ', @unmapped) ELSE N'' END));

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 4)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (4, N'004_parsery_mapowanie_kolumn.sql', '0.13.0');
GO
