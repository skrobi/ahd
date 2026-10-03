/* PZL-EV – migracja 007: dane kanoniczne w jednej stałej tabeli (sloty, columnstore) i skompresowana treść plików.
   - CAN_Row: wszystkie parsery w jednej tabeli – kolumny stałe (FileId, RowNumber, ParserId, ParserVersion) i sloty
     typowane: T01–T40 tekst do 400 znaków, L01–L05 tekst do 4000, N01–N20 kwota / liczba, I01–I10 liczba całkowita,
     D01–D10 data. Parser przydziela polu slot na stałe (META_Parser.Fields: Slot) – nowe pole albo nowy parser to tylko
     zapis parsera, bez zmian w tabelach (na PROD użytkownik nie ma prawa tworzenia i zmiany tabel). Indeks kolumnowy
     (clustered columnstore) – kompresja i szybkie grupowanie przy milionach wierszy.
   - Pola istniejących parserów dostają sloty (kolejność pól w parserze, osobno dla każdego typu); dane z tabel CAN_<Tabela>
     przechodzą do CAN_Row, a tabele CAN_<Tabela> są usuwane.
   - META_SourceFileContent: treść wersji pliku skompresowana GZip (oryginalny plik) zamiast wiersza surowego JSON na każdy
     wiersz; dawne wiersze surowe (STG_RawRow) przechodzą jako JSON-lines (UTF-16, GZip), tabela STG_RawRow jest usuwana.
   Wymaga SQL Server 2016+ i poziomu zgodności bazy co najmniej 130 (OPENJSON, COMPRESS).

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) – w jednej transakcji; ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 007_sloty_danych_kanonicznych.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) < 130
    THROW 50007, N'Migracja 007 wymaga poziomu zgodności bazy co najmniej 130 (SQL Server 2016): ALTER DATABASE <baza> SET COMPATIBILITY_LEVEL = 130.', 1;
GO

/* ---------- CAN_Row: sloty typowane + clustered columnstore ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAN_Row]') IS NULL
BEGIN
CREATE TABLE [$(Schema)].[$(Prefix)CAN_Row] (
    FileId BIGINT NOT NULL, RowNumber INT NOT NULL, ParserId BIGINT NOT NULL, ParserVersion INT NOT NULL,
    T01 NVARCHAR(400) NULL, T02 NVARCHAR(400) NULL, T03 NVARCHAR(400) NULL, T04 NVARCHAR(400) NULL, T05 NVARCHAR(400) NULL, T06 NVARCHAR(400) NULL, T07 NVARCHAR(400) NULL, T08 NVARCHAR(400) NULL,
    T09 NVARCHAR(400) NULL, T10 NVARCHAR(400) NULL, T11 NVARCHAR(400) NULL, T12 NVARCHAR(400) NULL, T13 NVARCHAR(400) NULL, T14 NVARCHAR(400) NULL, T15 NVARCHAR(400) NULL, T16 NVARCHAR(400) NULL,
    T17 NVARCHAR(400) NULL, T18 NVARCHAR(400) NULL, T19 NVARCHAR(400) NULL, T20 NVARCHAR(400) NULL, T21 NVARCHAR(400) NULL, T22 NVARCHAR(400) NULL, T23 NVARCHAR(400) NULL, T24 NVARCHAR(400) NULL,
    T25 NVARCHAR(400) NULL, T26 NVARCHAR(400) NULL, T27 NVARCHAR(400) NULL, T28 NVARCHAR(400) NULL, T29 NVARCHAR(400) NULL, T30 NVARCHAR(400) NULL, T31 NVARCHAR(400) NULL, T32 NVARCHAR(400) NULL,
    T33 NVARCHAR(400) NULL, T34 NVARCHAR(400) NULL, T35 NVARCHAR(400) NULL, T36 NVARCHAR(400) NULL, T37 NVARCHAR(400) NULL, T38 NVARCHAR(400) NULL, T39 NVARCHAR(400) NULL, T40 NVARCHAR(400) NULL,
    L01 NVARCHAR(4000) NULL, L02 NVARCHAR(4000) NULL, L03 NVARCHAR(4000) NULL, L04 NVARCHAR(4000) NULL, L05 NVARCHAR(4000) NULL,
    N01 DECIMAL(28,8) NULL, N02 DECIMAL(28,8) NULL, N03 DECIMAL(28,8) NULL, N04 DECIMAL(28,8) NULL, N05 DECIMAL(28,8) NULL, N06 DECIMAL(28,8) NULL, N07 DECIMAL(28,8) NULL, N08 DECIMAL(28,8) NULL,
    N09 DECIMAL(28,8) NULL, N10 DECIMAL(28,8) NULL, N11 DECIMAL(28,8) NULL, N12 DECIMAL(28,8) NULL, N13 DECIMAL(28,8) NULL, N14 DECIMAL(28,8) NULL, N15 DECIMAL(28,8) NULL, N16 DECIMAL(28,8) NULL,
    N17 DECIMAL(28,8) NULL, N18 DECIMAL(28,8) NULL, N19 DECIMAL(28,8) NULL, N20 DECIMAL(28,8) NULL,
    I01 INT NULL, I02 INT NULL, I03 INT NULL, I04 INT NULL, I05 INT NULL, I06 INT NULL, I07 INT NULL, I08 INT NULL,
    I09 INT NULL, I10 INT NULL,
    D01 DATE NULL, D02 DATE NULL, D03 DATE NULL, D04 DATE NULL, D05 DATE NULL, D06 DATE NULL, D07 DATE NULL, D08 DATE NULL,
    D09 DATE NULL, D10 DATE NULL
);
CREATE CLUSTERED COLUMNSTORE INDEX [CCI_$(Prefix)CAN_Row] ON [$(Schema)].[$(Prefix)CAN_Row];
END
GO

/* ---------- META_SourceFileContent: treść wersji pliku (GZip) ---------- */

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)META_SourceFileContent]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)META_SourceFileContent] (
    FileId   BIGINT         NOT NULL CONSTRAINT [PK_$(Prefix)META_SourceFileContent] PRIMARY KEY
                                     CONSTRAINT [FK_$(Prefix)META_SourceFileContent_File] REFERENCES [$(Schema)].[$(Prefix)META_SourceFile] (FileId),
    Format   VARCHAR(20)    NOT NULL,   -- gzip: oryginalny plik; jsonl-utf16-gzip: dawne wiersze surowe (JSON na wiersz)
    Size     BIGINT         NOT NULL,   -- rozmiar przed kompresją (bajty)
    Content  VARBINARY(MAX) NOT NULL
);
GO

/* ---------- Sloty pól istniejących parserów i przeniesienie danych ---------- */

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 7)
BEGIN
    -- Pole parsera (wszystkie wersje): definicja z najnowszej wersji, w której występuje; kolejność – pola wersji bieżącej
    -- według kolejności w parserze, potem pola tylko z wcześniejszych wersji (ich dane też przechodzą do CAN_Row).
    CREATE TABLE #slots (ParserId BIGINT NOT NULL, Field NVARCHAR(64) NOT NULL, Slot VARCHAR(3) NOT NULL, Kind CHAR(1) NOT NULL);
    WITH f AS (
        SELECT p.ParserId, p.Version, CAST(j.[key] AS INT) AS Idx, x.Field, x.Type, x.Length,
               ROW_NUMBER() OVER (PARTITION BY p.ParserId, x.Field ORDER BY p.Version DESC) AS rn,
               CASE WHEN p.SupersededAt IS NULL THEN 0 ELSE 1 END AS Hist
        FROM [$(Schema)].[$(Prefix)META_Parser] p
        CROSS APPLY OPENJSON(p.Fields) j
        CROSS APPLY OPENJSON(j.value) WITH (Field NVARCHAR(64) '$.Field', Type VARCHAR(20) '$.Type', Length INT '$.Length') x
    ), k AS (
        SELECT ParserId, Field, Version, Idx, Hist,
               CASE Type WHEN 'decimal' THEN 'N' WHEN 'integer' THEN 'I' WHEN 'date' THEN 'D'
                         ELSE CASE WHEN ISNULL(Length, 400) > 400 THEN 'L' ELSE 'T' END END AS Kind
        FROM f WHERE rn = 1
    )
    INSERT INTO #slots (ParserId, Field, Slot, Kind)
    SELECT ParserId, Field,
           Kind + RIGHT('0' + CAST(ROW_NUMBER() OVER (PARTITION BY ParserId, Kind ORDER BY Hist, Version DESC, Idx) AS VARCHAR(3)), 2), Kind
    FROM k;

    IF EXISTS (SELECT 1 FROM #slots WHERE CAST(RIGHT(Slot, 2) AS INT) > CASE Kind WHEN 'T' THEN 40 WHEN 'L' THEN 5 WHEN 'N' THEN 20 ELSE 10 END)
        THROW 50007, N'Migracja 007: parser ma więcej pól danego typu niż slotów w CAN_Row (T 40, L 5, N 20, I 10, D 10).', 1;

    -- Slot w polach każdej wersji parsera (META_Parser.Fields)
    UPDATE p SET Fields = (
        SELECT x.Field, x.[Column], x.Type, x.Length, x.PadDigits, x.Required, s.Slot
        FROM OPENJSON(p.Fields) j
        CROSS APPLY OPENJSON(j.value) WITH (Field NVARCHAR(64) '$.Field', [Column] NVARCHAR(400) '$.Column', Type VARCHAR(20) '$.Type',
                                           Length INT '$.Length', PadDigits INT '$.PadDigits', Required BIT '$.Required') x
        LEFT JOIN #slots s ON s.ParserId = p.ParserId AND s.Field = x.Field
        ORDER BY CAST(j.[key] AS INT)
        FOR JSON PATH, INCLUDE_NULL_VALUES)
    FROM [$(Schema)].[$(Prefix)META_Parser] p;

    -- Dane z tabel CAN_<Tabela> do CAN_Row; liczba wierszy sprawdzana przed usunięciem tabeli
    DECLARE @parser BIGINT = (SELECT MIN(ParserId) FROM [$(Schema)].[$(Prefix)META_Parser]), @table NVARCHAR(300), @targets NVARCHAR(MAX),
            @sources NVARCHAR(MAX), @sql NVARCHAR(MAX), @before BIGINT, @after BIGINT, @moved BIGINT = 0, @tables NVARCHAR(MAX) = N'';
    WHILE @parser IS NOT NULL
    BEGIN
        SET @table = N'[$(Schema)].[$(Prefix)CAN_' + (SELECT TOP (1) TableName FROM [$(Schema)].[$(Prefix)META_Parser] WHERE ParserId = @parser ORDER BY Version DESC) + N']';
        IF OBJECT_ID(@table) IS NOT NULL
        BEGIN
            SET @targets = (SELECT N', ' + QUOTENAME(s.Slot) FROM #slots s WHERE s.ParserId = @parser AND COL_LENGTH(@table, s.Field) IS NOT NULL
                            ORDER BY s.Slot FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)');
            SET @sources = (SELECT N', ' + QUOTENAME(s.Field) FROM #slots s WHERE s.ParserId = @parser AND COL_LENGTH(@table, s.Field) IS NOT NULL
                            ORDER BY s.Slot FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)');
            SET @sql = N'SELECT @n = COUNT_BIG(*) FROM ' + @table;
            EXEC sp_executesql @sql, N'@n BIGINT OUTPUT', @n = @before OUTPUT;
            SET @sql = N'INSERT INTO [$(Schema)].[$(Prefix)CAN_Row] WITH (TABLOCK) (FileId, RowNumber, ParserId, ParserVersion' + ISNULL(@targets, N'') + N') '
                     + N'SELECT FileId, RowNumber, @parser, ParserVersion' + ISNULL(@sources, N'') + N' FROM ' + @table;
            EXEC sp_executesql @sql, N'@parser BIGINT', @parser = @parser;
            SET @after = (SELECT COUNT_BIG(*) FROM [$(Schema)].[$(Prefix)CAN_Row] WHERE ParserId = @parser);
            IF @after <> @before
                THROW 50007, N'Migracja 007: liczba wierszy po przeniesieniu do CAN_Row niezgodna – migracja wycofana.', 1;
            SET @sql = N'DROP TABLE ' + @table;
            EXEC sp_executesql @sql;
            SET @moved += @after;
            SET @tables += CASE WHEN @tables = N'' THEN N'' ELSE N', ' END + REPLACE(REPLACE(@table, N'[$(Schema)].[$(Prefix)', N''), N']', N'');
        END
        SET @parser = (SELECT MIN(ParserId) FROM [$(Schema)].[$(Prefix)META_Parser] WHERE ParserId > @parser);
    END

    -- Dawne wiersze surowe → treść pliku (JSON-lines, UTF-16, GZip); tabela STG_RawRow usuwana
    DECLARE @files INT = 0;
    IF OBJECT_ID(N'[$(Schema)].[$(Prefix)STG_RawRow]') IS NOT NULL
    BEGIN
        INSERT INTO [$(Schema)].[$(Prefix)META_SourceFileContent] (FileId, Format, Size, Content)
        SELECT t.FileId, 'jsonl-utf16-gzip', DATALENGTH(t.Lines), COMPRESS(t.Lines)
        FROM (
            SELECT f.FileId,
                   (SELECT r.Data + NCHAR(10) FROM [$(Schema)].[$(Prefix)STG_RawRow] r WHERE r.FileId = f.FileId ORDER BY r.RowNumber
                    FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)') AS Lines
            FROM (SELECT DISTINCT FileId FROM [$(Schema)].[$(Prefix)STG_RawRow]) f
            WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SourceFileContent] c WHERE c.FileId = f.FileId)
        ) t;
        SET @files = @@ROWCOUNT;
        DROP TABLE [$(Schema)].[$(Prefix)STG_RawRow];
    END

    INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
    VALUES (SYSDATETIMEOFFSET(), ORIGINAL_LOGIN(), N'Migracje',
            N'Migracja 007 – dane kanoniczne w jednej tabeli CAN_Row (sloty, columnstore): przeniesione wiersze '
            + CAST(@moved AS NVARCHAR(20)) + CASE WHEN @tables = N'' THEN N'' ELSE N' z ' + @tables END
            + N'; treść plików skompresowana (dawne wiersze surowe: ' + CAST(@files AS NVARCHAR(20)) + N' plików)');

    DROP TABLE #slots;
END
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 7)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (7, N'007_sloty_danych_kanonicznych.sql', '0.16.0');
GO
