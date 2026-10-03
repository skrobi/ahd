/* PZL-EV – migracja 006: rozwiązywanie problemów (docs/pipeline-fazy.md, rozdz. 1.3).
   - META_Problem: kto, kiedy i jak rozwiązał problem (ResolvedAt, ResolvedBy, Resolution) – automatycznie, gdy
     przyczyna zniknęła (kolejny import, przypisanie elementu CES), albo ręcznie na Pulpicie („Rozwiązane”).
   - Problemy importów wcześniejszych niż ostatni zakończony import – rozwiązane (stan z ostatniego importu;
     do wersji 0.14 Pulpit pokazywał tylko problemy ostatniego importu).

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) albo
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 006_problemy_rozwiazywanie.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_Problem]', N'ResolvedAt') IS NULL
ALTER TABLE [$(Schema)].[$(Prefix)META_Problem] ADD ResolvedAt DATETIMEOFFSET(7) NULL;
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_Problem]', N'ResolvedBy') IS NULL
ALTER TABLE [$(Schema)].[$(Prefix)META_Problem] ADD ResolvedBy NVARCHAR(128) NULL;
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)META_Problem]', N'Resolution') IS NULL
ALTER TABLE [$(Schema)].[$(Prefix)META_Problem] ADD Resolution NVARCHAR(400) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_$(Prefix)META_Problem_Open' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)META_Problem]'))
CREATE INDEX [IX_$(Prefix)META_Problem_Open] ON [$(Schema)].[$(Prefix)META_Problem] (Area, Reference) WHERE Resolved = 0;
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();
DECLARE @last BIGINT = (SELECT MAX(BatchId) FROM [$(Schema)].[$(Prefix)META_ImportBatch]
                        WHERE FinishedAt IS NOT NULL AND Status IN (N'zakończony', N'zakończony z błędami'));

UPDATE [$(Schema)].[$(Prefix)META_Problem]
SET Resolved = 1, ResolvedAt = @at, ResolvedBy = @by, Resolution = N'nieaktualny – stan z ostatniego importu (migracja 006)'
WHERE Resolved = 0 AND @last IS NOT NULL AND Reference LIKE N'import:%'
  AND TRY_CAST(SUBSTRING(Reference, 8, 20) AS BIGINT) < @last;

IF @@ROWCOUNT > 0
INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
VALUES (@at, @by, N'Migracje', N'Migracja 006 – rozwiązywanie problemów; problemy importów wcześniejszych niż ostatni oznaczone jako rozwiązane');

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 6)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (6, N'006_problemy_rozwiazywanie.sql', '0.15.0');
GO
