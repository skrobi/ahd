/* PZL-EV – migracja 046 (dane startowe): kursy USD w globalnym słowniku „Kursy walut” – miesiące od 2024-01 do 2026-10
   (Od – pierwszy dzień miesiąca, Do – ostatni), Kurs i Kurs CAS równe.
   UWAGA: kursy PRZYBLIŻONE (średnie miesięczne USD/PLN zbliżone do NBP, nie kursy urzędowe) – dane do testów
   i uruchomienia projektów w USD; przed rozliczeniem zastąp je kursami finansów. Wiersze rozpoznawalne po
   RecordedBy = N'migracja 046 – kursy przybliżone'.
   Miesiąc, w którym obowiązuje już jakikolwiek kurs USD słownika globalnego (także bezterminowy), zostaje bez zmian –
   migracja niczego nie nadpisuje. Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie
   przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 046_dane_kursy_usd.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = N'migracja 046 – kursy przybliżone';
DECLARE @rates TABLE (Year INT NOT NULL, Month INT NOT NULL, Rate DECIMAL(28,8) NOT NULL, PRIMARY KEY (Year, Month));
INSERT INTO @rates (Year, Month, Rate) VALUES
    (2024,  1, 4.0000),
    (2024,  2, 4.0000),
    (2024,  3, 3.9700),
    (2024,  4, 4.0000),
    (2024,  5, 3.9700),
    (2024,  6, 4.0000),
    (2024,  7, 3.9500),
    (2024,  8, 3.8800),
    (2024,  9, 3.8400),
    (2024, 10, 3.9500),
    (2024, 11, 4.0600),
    (2024, 12, 4.0700),
    (2025,  1, 4.1000),
    (2025,  2, 4.0000),
    (2025,  3, 3.8600),
    (2025,  4, 3.8100),
    (2025,  5, 3.7700),
    (2025,  6, 3.7000),
    (2025,  7, 3.6500),
    (2025,  8, 3.6700),
    (2025,  9, 3.6400),
    (2025, 10, 3.6600),
    (2025, 11, 3.6700),
    (2025, 12, 3.6200),
    (2026,  1, 3.6000),
    (2026,  2, 3.5800),
    (2026,  3, 3.6200),
    (2026,  4, 3.6500),
    (2026,  5, 3.6300),
    (2026,  6, 3.6200),
    (2026,  7, 3.6400),
    (2026,  8, 3.6600),
    (2026,  9, 3.6500),
    (2026, 10, 3.6700);

INSERT INTO [$(Schema)].[$(Prefix)DICT_FxRate]
    (RowId, Version, Project, Currency, Period, Rate, ValidFrom, ValidTo, CasRate, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, 'USD', NULL, r.Rate,
       DATEFROMPARTS(r.Year, r.Month, 1), EOMONTH(DATEFROMPARTS(r.Year, r.Month, 1)), r.Rate, @at, @by
FROM @rates r
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_FxRate] f
                  WHERE f.Project IS NULL AND f.Currency = 'USD' AND f.SupersededAt IS NULL
                    AND f.ValidFrom <= EOMONTH(DATEFROMPARTS(r.Year, r.Month, 1))
                    AND (f.ValidTo IS NULL OR f.ValidTo >= DATEFROMPARTS(r.Year, r.Month, 1)));
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 46)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (46, N'046_dane_kursy_usd.sql', '0.55.0');
GO
