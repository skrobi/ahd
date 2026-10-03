/* PZL-EV – migracja 008: bez przechowywania treści plików źródłowych.
   Każdy import z RABIT to pełny raport (te same kolumny, dane narastająco), a po udanym imporcie dane są w CAN_Row –
   kopia pliku w bazie nie jest potrzebna. Usuwa tabelę META_SourceFileContent (migracja 007) razem z jej zawartością.
   Wersje plików (META_SourceFile: SHA-256, kolumny, liczba wierszy) i dane kanoniczne zostają.

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) – w jednej transakcji; ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 008_bez_tresci_plikow.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceFileContent]') IS NOT NULL
BEGIN
    DECLARE @files BIGINT, @bytes BIGINT;
    SELECT @files = COUNT_BIG(*), @bytes = ISNULL(SUM(CAST(DATALENGTH(Content) AS BIGINT)), 0)
    FROM [$(Schema)].[$(Prefix)META_SourceFileContent];

    DROP TABLE [$(Schema)].[$(Prefix)META_SourceFileContent];

    INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
    VALUES (SYSDATETIMEOFFSET(), ORIGINAL_LOGIN(), N'Migracje',
            N'Migracja 008 – usunięta treść plików źródłowych (META_SourceFileContent): plików ' + CAST(@files AS NVARCHAR(20))
            + N', ' + CAST(@bytes / 1048576 AS NVARCHAR(20)) + N' MB; dane kanoniczne zostają w CAN_Row');
END
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 8)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (8, N'008_bez_tresci_plikow.sql', '0.18.0');
GO
