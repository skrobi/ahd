/* PZL-EV – migracja 035: daty rzeczywiste WP w słowniku projektu „Harmonogram i budżet” (docs/slowniki.md, rozdz. 3).
   Actual Start / Actual Koniec – nadpisanie dat rzeczywistych z raportu produkcyjnego (vAHDD) dla WP: WP bez danych
   produkcyjnych (daty podaje CAM) albo zakończenie prac przed zamknięciem ostatniej operacji. Puste – data z produkcji.
   Edycja także w tabeli Struktura projektu (docs/performance-objectives.md, rozdz. 4.2).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 035_daty_rzeczywiste_wp.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_ScheduleBudget]', N'ActualStart') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_ScheduleBudget] ADD ActualStart DATE NULL;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_ScheduleBudget]', N'ActualEnd') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_ScheduleBudget] ADD ActualEnd DATE NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 35)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (35, N'035_daty_rzeczywiste_wp.sql', '0.49.0');
GO
