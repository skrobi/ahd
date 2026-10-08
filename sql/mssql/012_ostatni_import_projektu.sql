/* PZL-EV – migracja 012: ostatni import parsera z filtrem projektu (docs/model-danych.md, rozdz. 5).
   Procedura CAN_LatestImport dostaje parametr @Project (kod projektu PZL-EV): zwraca tylko wiersze, których
   Project definition jest w nakładce Performance Objectives projektu (bieżące wersje). Pełny zrzut ACTUALS (ok.
   1,5 mln wierszy, 35 kolumn) to minuty samego przesyłania – raporty czytają dane jednego projektu.

       EXEC [FINOP].[PZLEV_CAN_LatestImport] @Parser = 'ACTUALS', @Project = 'M28';

   Uprawnienia jak w migracji 011 (prawa wywołującego). Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) – w jednej transakcji; ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 012_ostatni_import_projektu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)CAN_LatestImport]
    @Parser     VARCHAR(30),
    @SourceCode VARCHAR(60)       = NULL,
    @AsOf       DATETIMEOFFSET(7) = NULL,
    @Project    VARCHAR(20)       = NULL   -- projekt PZL-EV: tylko Project definition z jego nakładki
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @parserId BIGINT, @fields NVARCHAR(MAX), @message NVARCHAR(400);
    SELECT @parserId = ParserId, @fields = Fields
    FROM [$(Schema)].[$(Prefix)META_Parser]
    WHERE SupersededAt IS NULL AND Code = @Parser;
    IF @parserId IS NULL
    BEGIN
        SET @message = N'Nie ma parsera ' + ISNULL(@Parser, N'(brak)') + N'.';
        THROW 50011, @message, 1;
    END;

    -- Projekt PZL-EV: Project definition elementów jego nakładki (bieżące wersje); filtr po polu ProjectDefinition parsera.
    DECLARE @projectSlot VARCHAR(3);
    CREATE TABLE #definitions (ProjectDefinition NVARCHAR(400) NOT NULL PRIMARY KEY);
    IF @Project IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_Project] WHERE Code = @Project AND SupersededAt IS NULL)
        BEGIN
            SET @message = N'Nie ma projektu ' + @Project + N'.';
            THROW 50013, @message, 1;
        END;
        SELECT @projectSlot = x.Slot
        FROM OPENJSON(@fields) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
        WHERE x.Field = N'ProjectDefinition' AND x.Slot LIKE '[TL][0-9][0-9]';
        IF @projectSlot IS NULL
        BEGIN
            SET @message = N'Parser ' + @Parser + N' nie ma pola ProjectDefinition – nie można filtrować po projekcie.';
            THROW 50012, @message, 1;
        END;
        INSERT INTO #definitions (ProjectDefinition)
        SELECT DISTINCT ProjectDefinition
        FROM [$(Schema)].[$(Prefix)META_PerformanceObjective]
        WHERE Project = @Project AND SupersededAt IS NULL AND ProjectDefinition IS NOT NULL;
    END;

    -- Pola bieżącej wersji parsera ze slotem, w kolejności parsera: r.<slot> AS [<pole>]
    DECLARE @list NVARCHAR(MAX) = (
        SELECT N', r.' + QUOTENAME(x.Slot) + N' AS ' + QUOTENAME(x.Field)
        FROM OPENJSON(@fields) j
        CROSS APPLY OPENJSON(j.value) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
        WHERE x.Slot LIKE '[TLNID][0-9][0-9]' AND COL_LENGTH(N'[$(Schema)].[$(Prefix)CAN_Row]', x.Slot) IS NOT NULL
        ORDER BY CAST(j.[key] AS INT)
        FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)');

    DECLARE @sql NVARCHAR(MAX) = N'
        WITH files AS (
            SELECT f.FileId, f.SourceCode, f.Location, f.FileName, f.ModifiedAt, f.ImportedAt, f.BatchId,
                   ROW_NUMBER() OVER (PARTITION BY f.Location, f.FileName ORDER BY f.ModifiedAt DESC, f.ImportedAt DESC, f.FileId DESC) AS rn
            FROM [$(Schema)].[$(Prefix)META_SourceFile] f
            WHERE f.CanonicalStatus = N''utworzone''
              AND (@AsOf IS NULL OR f.ImportedAt <= @AsOf)
              AND (@SourceCode IS NULL OR f.SourceCode = @SourceCode)
              AND f.SourceCode IN (SELECT d.Code FROM [$(Schema)].[$(Prefix)META_SourceDefinition] d WHERE d.Parser = @Parser))
        SELECT f.SourceCode, f.Location, f.FileName, f.ModifiedAt, f.ImportedAt, f.BatchId, r.FileId, r.RowNumber, r.ParserVersion'
        + ISNULL(@list, N'') + N'
        FROM files f
        JOIN [$(Schema)].[$(Prefix)CAN_Row] r ON r.FileId = f.FileId AND r.ParserId = @parserId
        WHERE f.rn = 1'
        + CASE WHEN @projectSlot IS NULL THEN N'' ELSE N' AND r.' + QUOTENAME(@projectSlot) + N' IN (SELECT d.ProjectDefinition FROM #definitions d)' END + N';';

    EXEC sp_executesql @sql,
        N'@Parser VARCHAR(30), @SourceCode VARCHAR(60), @AsOf DATETIMEOFFSET(7), @parserId BIGINT',
        @Parser = @Parser, @SourceCode = @SourceCode, @AsOf = @AsOf, @parserId = @parserId;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'pzl_ev_user') IS NOT NULL
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)CAN_LatestImport] TO [pzl_ev_user];
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 12)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (12, N'012_ostatni_import_projektu.sql', '0.23.0');
GO
