/* PZL-EV – migracja 058: zamknięcie okresu – zamrożenie przebiegu (docs/pipeline-fazy.md, Z; tasks/F5.5).
   - META_Run.FrozenPinId / FrozenAt / FrozenBy: uruchomienie zatwierdzone w klocku „Zamknięcie okresu” – przebieg tygodnia tylko
     do odczytu (bez ponownego wykonania, potwierdzeń i nowych uruchomień); „Otwórz zamknięcie” z komentarzem czyści kolumny.
   - META_Journal: indeks (Scope, Id) – dziennik przebiegu na panelu (wpisy przebiegu po jego kodzie; tasks/F5.6).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 058_zamkniecie_okresu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_Run]', N'FrozenPinId') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)META_Run] ADD FrozenPinId BIGINT NULL, FrozenAt DATETIMEOFFSET(7) NULL, FrozenBy NVARCHAR(128) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Journal]') AND name = N'IX_$(Prefix)META_Journal_Scope')
    CREATE INDEX [IX_$(Prefix)META_Journal_Scope] ON [$(Schema)].[$(Prefix)META_Journal] (Scope, Id);
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 58)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (58, N'058_zamkniecie_okresu.sql', '0.64.0');
GO
