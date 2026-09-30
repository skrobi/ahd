/* PZL-EV – MVP etapu 1: rejestr importów plików SAP i surowe wiersze.
   Uruchamia administrator na bazie PZL-EV (TEST / PROD). Skrypt jest idempotentny.
   Aplikacja łączy się kontem Windows (AD) użytkownika z roli pzl_ev_user. */

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
    SygnaturaKolumn       CHAR(16)       NULL,   -- odcisk układu kolumn (zmiana układu raportu)
    KodZrodla             VARCHAR(60)    NULL,   -- źródło RABIT rozpoznane po prefiksie nazwy pliku
    LiczbaWierszy         INT            NULL
);

GO

-- Wcześniejsza wersja skryptu: kolumna TypRaportu → KodZrodla.
IF COL_LENGTH('meta.SourceFile', 'TypRaportu') IS NOT NULL AND COL_LENGTH('meta.SourceFile', 'KodZrodla') IS NULL
    EXEC sp_rename 'meta.SourceFile.TypRaportu', 'KodZrodla', 'COLUMN';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SourceFile_KodZrodla')
CREATE INDEX IX_SourceFile_KodZrodla ON meta.SourceFile (KodZrodla, Zaimportowano);

IF OBJECT_ID('meta.SourceFileSeen') IS NULL
CREATE TABLE meta.SourceFileSeen (
    Id                    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SourceFileSeen PRIMARY KEY,
    BatchId               VARCHAR(40)    NOT NULL CONSTRAINT FK_Seen_Batch REFERENCES meta.ImportBatch(BatchId),
    NazwaPliku            NVARCHAR(400)  NOT NULL,
    Zrodlo                NVARCHAR(800)  NOT NULL,  -- klucz indeksu ≤ 1700 B
    Rozmiar               BIGINT         NOT NULL,
    ZmodyfikowanyWZrodle  VARCHAR(40)    NULL,
    Sha256                CHAR(64)       NULL,
    Decyzja               VARCHAR(30)    NOT NULL,  -- zaimportowany / duplikat / pominiety (metadane) / nierozpoznany / blad
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

/* Wiersze ze wskazaniem źródła i importu – do odtwarzania stanu i porównań. */
CREATE OR ALTER VIEW stg.vRawRowZrodlo AS
SELECT f.KodZrodla, f.NazwaPliku, f.ZmodyfikowanyWZrodle, f.BatchId, f.Zaimportowano, r.Sha256, r.NrWiersza, r.Dane
FROM stg.RawRow r
JOIN meta.SourceFile f ON f.Sha256 = r.Sha256;
GO

/* Rola aplikacji: MVP zapisuje bezpośrednio do tabel (docelowo przez procedury). */
IF DATABASE_PRINCIPAL_ID('pzl_ev_user') IS NULL CREATE ROLE pzl_ev_user;
GRANT SELECT, INSERT, UPDATE ON SCHEMA::meta TO pzl_ev_user;
GRANT SELECT, INSERT ON SCHEMA::stg TO pzl_ev_user;
-- ALTER ROLE pzl_ev_user ADD MEMBER [DOMENA\Grupa_Finanse_PZL_EV];
GO
