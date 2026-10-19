/* PZL-EV – migracja 051: reguły walidacji ACTUALS zapisane formułami (docs/pipeline-fazy.md, rozdz. 4.1).
   - DICT_RunValidation – „Reguły walidacji” (globalny): nazwa, kontrola (wiersz macierzy wyników), aktywna, filtr wierszy,
     grupowanie, warunek, kwota, komunikat, komunikat OK, najwyżej N wyników, poziom, próg, opis. Formuły – język reguł walidacji
     aplikacji (pola wiersza w nawiasach kwadratowych, agregaty SUMA / LICZBA / MAKS …, POPRZ. – poprzedni przebieg).
   Bez danych startowych – pusty słownik oznacza reguły startowe aplikacji (okno Kontrole na ekranie Przebiegi zapisuje je do edycji).
   Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 051_reguly_walidacji.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunValidation]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_RunValidation] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_RunValidation] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    Name             NVARCHAR(200)     NOT NULL,
    CheckName        NVARCHAR(200)     NOT NULL,
    IsActive         BIT               NOT NULL,
    RowFilter        NVARCHAR(4000)    NULL,
    GroupBy          NVARCHAR(400)     NULL,
    Condition        NVARCHAR(4000)    NOT NULL,
    AmountFormula    NVARCHAR(4000)    NULL,
    Message          NVARCHAR(4000)    NOT NULL,
    OkMessage        NVARCHAR(4000)    NULL,
    TopN             INT               NULL,
    Level            NVARCHAR(10)      NOT NULL,
    Threshold        DECIMAL(28,8)     NULL,
    Description      NVARCHAR(1000)    NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_RunValidation_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_RunValidation_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_RunValidation_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_RunValidation]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_RunValidation_Key] ON [$(Schema)].[$(Prefix)DICT_RunValidation] (Project, Name) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 51)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (51, N'051_reguly_walidacji.sql', '0.58.0');
GO
