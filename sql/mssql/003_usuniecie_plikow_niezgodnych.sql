/* PZL-EV – migracja 003: usunięcie plików zapisanych mimo niezgodności z definicją źródła.
   Do wersji 0.11 import zapisywał plik i jego wiersze surowe także wtedy, gdy układ kolumn był niezgodny z definicją
   albo wartości nie pasowały do typów (dane kanoniczne nie powstawały). Od wersji 0.12 taki plik nie trafia do bazy;
   ten skrypt usuwa zapisane wcześniej: wiersz pliku (META_SourceFile) i jego wiersze surowe (STG_RawRow; po migracji 007
   – treść pliku META_SourceFileContent).
   Historia decyzji (META_SourceFileSeen) zostaje. Przy kolejnym imporcie te pliki zostaną pobrane ponownie
   (pomijane są tylko pliki, których treść jest w bazie).

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) albo
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 003_usuniecie_plikow_niezgodnych.sql
   (-f 65001 – plik w UTF-8). Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @files TABLE (FileId BIGINT PRIMARY KEY);
INSERT INTO @files (FileId)
SELECT FileId FROM [$(Schema)].[$(Prefix)META_SourceFile]
WHERE CanonicalStatus = N'brak – sygnatura kolumn niezgodna z definicją'
   OR CanonicalStatus LIKE N'brak – % błędów wartości';

-- Tabele sprzed migracji 007 (CAN_Actuals, STG_RawRow) albo po niej (CAN_Row, META_SourceFileContent) – skrypt działa w obu stanach.
DECLARE @rawRows INT = 0;
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_Actuals]') IS NOT NULL
    DELETE c FROM [$(Schema)].[$(Prefix)CAN_Actuals] c JOIN @files f ON f.FileId = c.FileId;
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)STG_RawRow]') IS NOT NULL
BEGIN
    DELETE r FROM [$(Schema)].[$(Prefix)STG_RawRow] r JOIN @files f ON f.FileId = r.FileId;
    SET @rawRows = @@ROWCOUNT;
END
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_Row]') IS NOT NULL
    DELETE c FROM [$(Schema)].[$(Prefix)CAN_Row] c JOIN @files f ON f.FileId = c.FileId;
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceFileContent]') IS NOT NULL
BEGIN
    DELETE c FROM [$(Schema)].[$(Prefix)META_SourceFileContent] c JOIN @files f ON f.FileId = c.FileId;
    SET @rawRows += @@ROWCOUNT;
END
DELETE s FROM [$(Schema)].[$(Prefix)META_SourceFile] s JOIN @files f ON f.FileId = s.FileId;
DECLARE @removed INT = @@ROWCOUNT;

IF @removed > 0
INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
VALUES (SYSDATETIMEOFFSET(), ORIGINAL_LOGIN(), N'Migracje',
        CONCAT(N'Migracja 003 – usunięte pliki niezgodne z definicją źródła: ', @removed, N' (wiersze surowe: ', @rawRows,
               N'); kolejny import pobierze je ponownie'));

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 3)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (3, N'003_usuniecie_plikow_niezgodnych.sql', '0.12.0');
GO
