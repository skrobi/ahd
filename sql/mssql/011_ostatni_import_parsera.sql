/* PZL-EV – migracja 011: ostatni import parsera (docs/model-danych.md, rozdz. 5; docs/zrodla-danych.md).
   Procedura [$(Schema)].[$(Prefix)CAN_LatestImport] zwraca dane kanoniczne parsera z najnowszej wersji każdego
   pliku źródłowego (lokalizacja + nazwa) – kolumny pliku i pola parsera pod własnymi nazwami (sloty CAN_Row według
   META_Parser.Fields). Działa dla każdego parsera, także dodanego później: zapytanie składane w chwili wywołania,
   bez zakładania widoków (aplikacja i użytkownicy nie wykonują DDL).

       EXEC [FINOP].[PZLEV_CAN_LatestImport] @Parser = 'ACTUALS';                              -- wszystkie źródła parsera
       EXEC [FINOP].[PZLEV_CAN_LatestImport] @Parser = 'ACTUALS', @SourceCode = 'ACTUALS_CES'; -- jedno źródło
       EXEC [FINOP].[PZLEV_CAN_LatestImport] @Parser = 'ACTUALS', @AsOf = '2026-10-07T06:00:00+02:00'; -- stan na moment

   Najnowsza wersja pliku: największa data raportu (ModifiedAt), potem czas importu, spośród wersji z utworzonymi
   danymi kanonicznymi (CanonicalStatus = 'utworzone') zaimportowanych do @AsOf. Nocny import w kilku partiach
   (BatchId) daje więc komplet: każdy plik w wersji z ostatniej partii, która go przyniosła; plik, którego RABIT nie
   wygenerował ponownie, zostaje w poprzedniej wersji (O38). W trakcie importu wynik łączy pliki nowe i jeszcze
   poprzednie – kolumny ImportedAt i BatchId pokazują, skąd jest wiersz.

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) – w jednej transakcji; ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 011_ostatni_import_parsera.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)CAN_LatestImport]
    @Parser     VARCHAR(30),
    @SourceCode VARCHAR(60)       = NULL,
    @AsOf       DATETIMEOFFSET(7) = NULL
WITH EXECUTE AS OWNER   -- zapytanie dynamiczne: rola z samym EXECUTE nie ma SELECT na tabelach
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
        WHERE f.rn = 1;';

    EXEC sp_executesql @sql,
        N'@Parser VARCHAR(30), @SourceCode VARCHAR(60), @AsOf DATETIMEOFFSET(7), @parserId BIGINT',
        @Parser = @Parser, @SourceCode = @SourceCode, @AsOf = @AsOf, @parserId = @parserId;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'pzl_ev_user') IS NOT NULL
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)CAN_LatestImport] TO [pzl_ev_user];
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 11)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (11, N'011_ostatni_import_parsera.sql', '0.21.0');
GO
