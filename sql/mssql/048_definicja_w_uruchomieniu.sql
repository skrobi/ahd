/* PZL-EV – migracja 048: definicja przebiegu zapisana w uruchomieniu (docs/pipeline-fazy.md, rozdz. 4.1; docs/funkcjonalnosc.md, F06).
   - META_RunPin.CheckSettings – ustawienia reguł kontroli użyte w uruchomieniu (JSON: kod reguły → aktywna, poziom, próg) –
     słownik „Kontrole przebiegu” w chwili przebiegu; ekran Przebiegi pokazuje je obok bieżącej definicji. NULL – uruchomienie
     sprzed migracji. Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 048_definicja_w_uruchomieniu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_RunPin]', N'CheckSettings') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)META_RunPin] ADD CheckSettings NVARCHAR(MAX) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 48)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (48, N'048_definicja_w_uruchomieniu.sql', '0.56.0');
GO
