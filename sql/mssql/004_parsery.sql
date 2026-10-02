/* PZL-EV – migracja 004: parsery jako dane – parser pilnuje układu pliku (docs/zrodla-danych.md, rozdz. 2).
   - META_Parser: parser = tabela danych kanonicznych (CAN_<Tabela>) i jej pola – nazwa kolumny w bazie, nazwa kolumny
     w pliku (pusta – pole nie jest czytane z pliku), typ, długość tekstu, dopełnianie zerami, wymagane – z historią
     wersji. Edycja w aplikacji (Administracja → Parsery) zakłada tabelę albo dokłada w niej kolumny.
   - META_SourceDefinition: definicja źródła to kod, prefiks, typ raportu, parser i aktywność; kolumny, sygnatura
     i wersja parsera definicji przestają być używane (układ pilnuje parser) – kolumny dopuszczają NULL.
   - CAN_Actuals: tabela parsera ACTUALS. O wymaganych polach decyduje parser, więc kolumny dopuszczają NULL;
     nowe pola: Original Order Number, Item, Purchase order number, Invoice Number.
   - Parser ACTUALS (wersja 1) w układzie raportu ACTUALS_* z 2026-10 (23 kolumny; bez Cost element descr.,
     Partner-CCtr, Source object name – te pola zostają w tabeli, nieczytane z pliku); wymagane WBS Element,
     Fiscal Year, Period.

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) albo
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 004_parsery.sql
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
    Fields           NVARCHAR(MAX)     NOT NULL,   -- JSON: [{Field, Column, Type, Length, PadDigits, Required}]
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

/* ---------- META_SourceDefinition: układ pliku pilnuje parser ---------- */

ALTER TABLE [$(Schema)].[$(Prefix)META_SourceDefinition] ALTER COLUMN Columns NVARCHAR(MAX) NULL;
ALTER TABLE [$(Schema)].[$(Prefix)META_SourceDefinition] ALTER COLUMN Signature VARCHAR(16) NULL;
ALTER TABLE [$(Schema)].[$(Prefix)META_SourceDefinition] ALTER COLUMN ParserVersion INT NULL;
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

/* ---------- Parser ACTUALS ---------- */

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();
DECLARE @fields NVARCHAR(MAX) = CAST(N'' AS NVARCHAR(MAX))
  + N'[{"Field":"ProjectDefinition","Column":"Project Definition","Type":"text","Length":100,"PadDigits":null,"Requi'
  + N'red":false},{"Field":"WbsElement","Column":"WBS Element","Type":"text","Length":100,"PadDigits":null,"Required'
  + N'":true},{"Field":"CostElement","Column":"Cost Element","Type":"text","Length":10,"PadDigits":10,"Required":fal'
  + N'se},{"Field":"CostElementName","Column":"Cost element name","Type":"text","Length":400,"PadDigits":null,"Requi'
  + N'red":false},{"Field":"CoObjectName","Column":"CO object name","Type":"text","Length":400,"PadDigits":null,"Req'
  + N'uired":false},{"Field":"TransactionCurrency","Column":"Transaction Currency","Type":"text","Length":10,"PadDig'
  + N'its":null,"Required":false},{"Field":"ValueTranCurr","Column":"Value TranCurr","Type":"decimal","Length":null,'
  + N'"PadDigits":null,"Required":false},{"Field":"ObjectCurrency","Column":"Object Currency","Type":"text","Length"'
  + N':10,"PadDigits":null,"Required":false},{"Field":"ValueObjCrcy","Column":"Value in Obj. Crcy","Type":"decimal",'
  + N'"Length":null,"PadDigits":null,"Required":false},{"Field":"ReportCurrency","Column":"Report currency","Type":"'
  + N'text","Length":10,"PadDigits":null,"Required":false},{"Field":"ValueRepCur","Column":"Val.in rep.cur.","Type":'
  + N'"decimal","Length":null,"PadDigits":null,"Required":false},{"Field":"TotalQuantity","Column":"Total Quantity",'
  + N'"Type":"decimal","Length":null,"PadDigits":null,"Required":false},{"Field":"PartnerObjectClass","Column":"Part'
  + N'ner Object Class","Type":"text","Length":100,"PadDigits":null,"Required":false},{"Field":"PartnerObject","Colu'
  + N'mn":"Partner object","Type":"text","Length":200,"PadDigits":null,"Required":false},{"Field":"OriginalMaterial"'
  + N',"Column":"Original material","Type":"text","Length":100,"PadDigits":null,"Required":false},{"Field":"Original'
  + N'MaterialDescription","Column":"Original material description","Type":"text","Length":400,"PadDigits":null,"Req'
  + N'uired":false},{"Field":"OriginalOrderNumber","Column":"Original Order Number","Type":"text","Length":100,"PadD'
  + N'igits":null,"Required":false},{"Field":"Item","Column":"Item","Type":"text","Length":50,"PadDigits":null,"Requ'
  + N'ired":false},{"Field":"PurchaseOrderNumber","Column":"Purchase order number","Type":"text","Length":100,"PadDi'
  + N'gits":null,"Required":false},{"Field":"FiscalYear","Column":"Fiscal Year","Type":"integer","Length":null,"PadD'
  + N'igits":null,"Required":true},{"Field":"CreatedOn","Column":"Created on","Type":"date","Length":null,"PadDigits'
  + N'":null,"Required":false},{"Field":"Period","Column":"Period","Type":"integer","Length":null,"PadDigits":null,"'
  + N'Required":true},{"Field":"InvoiceNumber","Column":"Invoice Number","Type":"text","Length":100,"PadDigits":null'
  + N',"Required":false},{"Field":"CostElementDescr","Column":"","Type":"text","Length":400,"PadDigits":null,"Requir'
  + N'ed":false},{"Field":"PartnerCctr","Column":"","Type":"text","Length":100,"PadDigits":null,"Required":false},{"'
  + N'Field":"SourceObjectName","Column":"","Type":"text","Length":400,"PadDigits":null,"Required":false}]';

INSERT INTO [$(Schema)].[$(Prefix)META_Parser] (ParserId, Version, Code, Name, TableName, Fields, Active, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, 'ACTUALS', N'Koszty rzeczywiste CES (ACTUALS_*)', 'Actuals', @fields, 1, @at, @by
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Parser] WHERE SupersededAt IS NULL AND Code = 'ACTUALS');

IF @@ROWCOUNT > 0
INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
VALUES (@at, @by, N'Migracje', N'Migracja 004 – parser ACTUALS w bazie (układ ACTUALS_* z 2026-10: 23 kolumny, wymagane WBS Element, Fiscal Year, Period); układ pliku pilnuje parser, definicje źródeł wskazują tylko parser');

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 4)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (4, N'004_parsery.sql', '0.13.0');
GO
