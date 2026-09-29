/* AHD – MVP etapu 1: rejestr importów plików SAP i surowe wiersze.
   Uruchamia administrator na bazie AHD (TEST / PROD). Skrypt jest idempotentny.
   Aplikacja łączy się kontem Windows (AD) użytkownika z roli ahd_user. */

IF SCHEMA_ID('meta') IS NULL EXEC('CREATE SCHEMA meta');
IF SCHEMA_ID('stg')  IS NULL EXEC('CREATE SCHEMA stg');
GO

IF OBJECT_ID('meta.ImportBatch') IS NULL
CREATE TABLE meta.ImportBatch (
    BatchId               VARCHAR(40)    NOT NULL CONSTRAINT PK_ImportBatch PRIMARY KEY,
    Zrodlo                NVARCHAR(1000) NOT NULL,
    Uzytkownik            NVARCHAR(128)  NOT NULL,
    Komputer              NVARCHAR(128)  NULL,
    WersjaAplikacji       VARCHAR(20)    NULL,
    Start                 DATETIME2(3)   NOT NULL,
    Koniec                DATETIME2(3)   NULL,
    PlikowWidzianych      INT            NULL,
    PlikowZaimportowanych INT            NULL,
    PlikowPominietych     INT            NULL,
    Bledow                INT            NULL,
    Status                VARCHAR(20)    NOT NULL,
    LoginBazy             NVARCHAR(128)  NOT NULL CONSTRAINT DF_ImportBatch_Login DEFAULT ORIGINAL_LOGIN()
);

IF OBJECT_ID('meta.SourceFile') IS NULL
CREATE TABLE meta.SourceFile (
    Sha256                CHAR(64)       NOT NULL CONSTRAINT PK_SourceFile PRIMARY KEY,
    NazwaPliku            NVARCHAR(400)  NOT NULL,
    Zrodlo                NVARCHAR(1000) NOT NULL,
    Rozmiar               BIGINT         NOT NULL,
    ZmodyfikowanyWZrodle  VARCHAR(40)    NULL,
    SciezkaLandingZone    NVARCHAR(1000) NOT NULL,
    BatchId               VARCHAR(40)    NOT NULL CONSTRAINT FK_SourceFile_Batch REFERENCES meta.ImportBatch(BatchId),
    Zaimportowano         DATETIME2(3)   NOT NULL,
    Uzytkownik            NVARCHAR(128)  NOT NULL,
    TypPliku              VARCHAR(10)    NULL,
    Arkusz                NVARCHAR(128)  NULL,
    Kodowanie             VARCHAR(20)    NULL,
    Separator             VARCHAR(5)     NULL,
    Kolumny               NVARCHAR(MAX)  NULL,   -- JSON: lista nagłówków
    SygnaturaKolumn       CHAR(16)       NULL,   -- odcisk układu kolumn (rozpoznawanie typu raportu)
    TypRaportu            VARCHAR(30)    NULL,
    LiczbaWierszy         INT            NULL
);

IF OBJECT_ID('meta.SourceFileSeen') IS NULL
CREATE TABLE meta.SourceFileSeen (
    Id                    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SourceFileSeen PRIMARY KEY,
    BatchId               VARCHAR(40)    NOT NULL CONSTRAINT FK_Seen_Batch REFERENCES meta.ImportBatch(BatchId),
    NazwaPliku            NVARCHAR(400)  NOT NULL,
    Zrodlo                NVARCHAR(800)  NOT NULL,  -- klucz indeksu ≤ 1700 B
    Rozmiar               BIGINT         NOT NULL,
    ZmodyfikowanyWZrodle  VARCHAR(40)    NULL,
    Sha256                CHAR(64)       NULL,
    Decyzja               VARCHAR(30)    NOT NULL,  -- zaimportowany / duplikat / pominiety (metadane) / blad
    Opis                  NVARCHAR(1000) NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Seen_Zrodlo')
CREATE INDEX IX_Seen_Zrodlo ON meta.SourceFileSeen (Zrodlo, Rozmiar, ZmodyfikowanyWZrodle) INCLUDE (Decyzja, Sha256);

IF OBJECT_ID('stg.RawRow') IS NULL
CREATE TABLE stg.RawRow (
    Sha256                CHAR(64)       NOT NULL CONSTRAINT FK_RawRow_File REFERENCES meta.SourceFile(Sha256),
    NrWiersza             INT            NOT NULL,
    Dane                  NVARCHAR(MAX)  NOT NULL,  -- JSON: wartości wiersza w kolejności kolumn
    CONSTRAINT PK_RawRow PRIMARY KEY (Sha256, NrWiersza) WITH (DATA_COMPRESSION = PAGE)
);
GO

/* Rola aplikacji: MVP zapisuje bezpośrednio do tabel (docelowo przez procedury). */
IF DATABASE_PRINCIPAL_ID('ahd_user') IS NULL CREATE ROLE ahd_user;
GRANT SELECT, INSERT, UPDATE ON SCHEMA::meta TO ahd_user;
GRANT SELECT, INSERT ON SCHEMA::stg TO ahd_user;
-- ALTER ROLE ahd_user ADD MEMBER [DOMENA\Grupa_Finanse_AHD];
GO
