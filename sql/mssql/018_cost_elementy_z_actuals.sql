/* PZL-EV – migracja 018: cost elementy z ostatniego importu ACTUALS (docs/slowniki.md, rozdz. 6).
   CAN_ActualsCostElements @Value – wszystkie cost elementy całego zbioru ACTUALS (najnowsza wersja każdego pliku –
   CAN_LatestFiles, wszystkie projekty): nazwa z danych, liczba wierszy, suma kwoty. Ekran Słowniki → Cost Category →
   „Uzupełnij z ACTUALS” dopisuje do słownika cost elementy, których w nim nie ma (Cost Category i „Rozliczeniowy”
   określa finansista). Skrypt jest idempotentny.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 018_cost_elementy_z_actuals.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [$(Schema)].[$(Prefix)CAN_ActualsCostElements]
    @Value NVARCHAR(64) = N'ValueObjCrcy'   -- pole kwoty parsera ACTUALS (suma – informacyjnie)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @parserId BIGINT, @fields NVARCHAR(MAX);
    SELECT @parserId = ParserId, @fields = Fields FROM [$(Schema)].[$(Prefix)META_Parser] WHERE SupersededAt IS NULL AND Code = 'ACTUALS';
    IF @parserId IS NULL
        THROW 50011, N'Nie ma parsera ACTUALS.', 1;

    DECLARE @slots TABLE (Field NVARCHAR(64) PRIMARY KEY, Slot VARCHAR(3));
    INSERT INTO @slots (Field, Slot)
    SELECT x.Field, x.Slot FROM OPENJSON(@fields) WITH (Field NVARCHAR(64) '$.Field', Slot VARCHAR(3) '$.Slot') x
    WHERE x.Slot LIKE '[TLNID][0-9][0-9]';
    DECLARE @ce NVARCHAR(20) = (SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElement'),
            @name NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = N'CostElementName'), N'NULL'),
            @val NVARCHAR(20) = ISNULL((SELECT N'r.' + QUOTENAME(Slot) FROM @slots WHERE Field = @Value AND Slot LIKE 'N%'), N'NULL');
    IF @ce IS NULL
        THROW 50012, N'Parser ACTUALS musi mieć pole CostElement.', 1;

    DECLARE @sql NVARCHAR(MAX) = N'
        SELECT ' + @ce + N' AS [CostElement], MAX(' + @name + N') AS [Name], COUNT(*) AS [Rows], SUM(' + @val + N') AS [Amount]
        FROM [$(Schema)].[$(Prefix)CAN_LatestFiles](''ACTUALS'', NULL, NULL) f
        JOIN [$(Schema)].[$(Prefix)CAN_Row] r ON r.FileId = f.FileId AND r.ParserId = @parserId
        WHERE ' + @ce + N' IS NOT NULL
        GROUP BY ' + @ce + N'
        ORDER BY 1;';
    EXEC sp_executesql @sql, N'@parserId BIGINT', @parserId = @parserId;
END;
GO

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'pzl_ev_user' AND type = 'R')
    GRANT EXECUTE ON [$(Schema)].[$(Prefix)CAN_ActualsCostElements] TO [pzl_ev_user];
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 18)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (18, N'018_cost_elementy_z_actuals.sql', '0.29.0');
GO
