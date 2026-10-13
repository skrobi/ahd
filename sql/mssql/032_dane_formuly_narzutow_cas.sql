/* PZL-EV – migracja 032: dane startowe formuł narzutów CAS – zastąpione migracją 034 (narzuty CAS powiązane identyfikatorem, migracja 033).
   Wcześniejsza treść wpisywała dane z nazwami pul i grup; baza, w której już się wykonała, ma je przekształcone przez 033.
   Skrypt tylko zapisuje wersję – dane startowe narzutów CAS są w 034. Uruchomienie: aplikacja (Diagnostyka → Migracja
   albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 032_dane_formuly_narzutow_cas.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 32)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (32, N'032_dane_formuly_narzutow_cas.sql', '0.47.0');
GO
