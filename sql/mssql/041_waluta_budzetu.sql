/* PZL-EV – migracja 041: waluta kwot „Harmonogram i budżet” (docs/performance-objectives.md, rozdz. 4.2; docs/slowniki.md).
   - DICT_ScheduleBudget.Currency – kod waluty projektu (kolumna waluty zestawu walutowego ACTUALS) zapisywany przy wpisaniu
     BAC MATERIAL, BAC, ETC MATERIAL; puste – waluta projektu. Struktura przelicza kwoty w innej walucie kursem z „Kursy walut”.
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 041_waluta_budzetu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_ScheduleBudget]', N'Currency') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_ScheduleBudget] ADD Currency NVARCHAR(3) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 41)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (41, N'041_waluta_budzetu.sql', '0.54.0');
GO
