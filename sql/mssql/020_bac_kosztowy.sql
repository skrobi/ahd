/* PZL-EV – migracja 020: BAC – budżet kosztowy WP w słowniku projektu „Harmonogram i budżet”
   (docs/slowniki.md, rozdz. 3). Obok BAC HOURS (godziny) i BAC MATERIAL (materiały) – kwota budżetu WP; edycja także
   w tabeli Struktura projektu (docs/performance-objectives.md, rozdz. 4.2).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 020_bac_kosztowy.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_ScheduleBudget]', N'Bac') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_ScheduleBudget] ADD Bac DECIMAL(28,8) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 20)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (20, N'020_bac_kosztowy.sql', '0.32.0');
GO
