/* PZL-EV – migracja 033: narzuty CAS powiązane identyfikatorem, nie nazwą (docs/slowniki.md, rozdz. 6.1).
   Pula i grupa narzutowa są wskazywane identyfikatorem wiersza słownika (RowId) – zmiana nazwy puli albo grupy jest
   od razu widoczna w formułach, wskaźnikach, Cost Category i raporcie kosztów CES. Nazwa zapisana wcześniej jest
   szukana także w historii wierszy pul i grup (zmiana nazwy zachowuje RowId) – bieżąca nazwa ma pierwszeństwo.
   - DICT_CasBurdenRate.Pool (nazwa) → PoolId (RowId puli z „Pul narzutu CAS”); wskaźnik puli spoza słownika – zamknięty.
   - DICT_CasBurdenRate.Rate – 15 cyfr znaczących (jak w Excelu i w aplikacji).
   - DICT_CostCategory.BurdenGroup (nazwa) → BurdenGroupId (RowId grupy z „Grup narzutowych CAS”); wszystkie wersje,
     nazwa spoza słownika grup – pusta grupa (do ponownego wskazania).
   - DICT_CasFormula – „Formuły narzutów CAS” z klocków: GroupId, PoolId, Formula („( base + p:12 ) * r:15”);
     tabela DICT_CasBurdenFormula (migracja 031, podstawa jako tekst z nazwami) usunięta – formuły z arkusza finansów
     wpisuje migracja 034, pozostałe buduje się w oknie „Formuły narzutów”.
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 033_narzuty_cas_identyfikatory.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ---------- Wskaźniki: pula identyfikatorem ---------- */
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_CasBurdenRate]', N'PoolId') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_CasBurdenRate] ADD PoolId NVARCHAR(20) NULL;
GO
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_CasBurdenRate]', N'Pool') IS NOT NULL
BEGIN
    EXEC (N'UPDATE r SET PoolId = CAST(p.RowId AS NVARCHAR(20))
            FROM [$(Schema)].[$(Prefix)DICT_CasBurdenRate] r
            CROSS APPLY (SELECT TOP (1) x.RowId FROM [$(Schema)].[$(Prefix)DICT_CasPool] x
                         WHERE x.Project IS NULL AND x.Pool = r.Pool
                           AND EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasPool] c WHERE c.RowId = x.RowId AND c.SupersededAt IS NULL)
                         ORDER BY CASE WHEN x.SupersededAt IS NULL THEN 0 ELSE 1 END, x.SupersededAt DESC) p
            WHERE r.PoolId IS NULL;
          UPDATE [$(Schema)].[$(Prefix)DICT_CasBurdenRate] SET SupersededAt = SYSDATETIMEOFFSET(), SupersededBy = ORIGINAL_LOGIN()
            WHERE PoolId IS NULL AND SupersededAt IS NULL;
          UPDATE [$(Schema)].[$(Prefix)DICT_CasBurdenRate] SET PoolId = N''?'' + LEFT(Pool, 18) WHERE PoolId IS NULL;');
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_CasBurdenRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenRate]'))
        DROP INDEX [UX_$(Prefix)DICT_CasBurdenRate_Key] ON [$(Schema)].[$(Prefix)DICT_CasBurdenRate];
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_CasBurdenRate] DROP COLUMN Pool;
END;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenRate]') AND name = N'PoolId' AND is_nullable = 1)
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_CasBurdenRate] ALTER COLUMN PoolId NVARCHAR(20) NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_CasBurdenRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenRate]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_CasBurdenRate_Key] ON [$(Schema)].[$(Prefix)DICT_CasBurdenRate] (Project, PoolId, ValidFrom) WHERE SupersededAt IS NULL;
GO
/* Wskaźniki – 15 cyfr znaczących (jak w Excelu i w aplikacji): dane startowe 030 miały 17 cyfr z arkusza. */
UPDATE [$(Schema)].[$(Prefix)DICT_CasBurdenRate]
SET Rate = ROUND(Rate, 14 - CAST(FLOOR(LOG10(ABS(Rate))) AS INT))
WHERE Rate <> 0 AND 14 - CAST(FLOOR(LOG10(ABS(Rate))) AS INT) BETWEEN 0 AND 18
  AND Rate <> ROUND(Rate, 14 - CAST(FLOOR(LOG10(ABS(Rate))) AS INT));
GO

/* ---------- Cost Category: grupa narzutowa identyfikatorem ---------- */
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_CostCategory]', N'BurdenGroupId') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_CostCategory] ADD BurdenGroupId NVARCHAR(20) NULL;
GO
IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_CostCategory]', N'BurdenGroup') IS NOT NULL
BEGIN
    EXEC (N'UPDATE c SET BurdenGroupId = CAST(g.RowId AS NVARCHAR(20))
            FROM [$(Schema)].[$(Prefix)DICT_CostCategory] c
            CROSS APPLY (SELECT TOP (1) x.RowId FROM [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] x
                         WHERE x.Project IS NULL AND x.BurdenGroup = c.BurdenGroup
                           AND EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] k WHERE k.RowId = x.RowId AND k.SupersededAt IS NULL)
                         ORDER BY CASE WHEN x.SupersededAt IS NULL THEN 0 ELSE 1 END, x.SupersededAt DESC) g
            WHERE c.BurdenGroupId IS NULL;');
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_CostCategory] DROP COLUMN BurdenGroup;
END;
GO

/* ---------- Formuły z klocków ---------- */
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasBurdenFormula]') IS NOT NULL
    DROP TABLE [$(Schema)].[$(Prefix)DICT_CasBurdenFormula];
GO
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasFormula]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)DICT_CasFormula] (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)DICT_CasFormula] PRIMARY KEY,
    RowId BIGINT NOT NULL, Version INT NOT NULL, Project VARCHAR(20) NULL,
    GroupId          NVARCHAR(20)      NOT NULL,
    PoolId           NVARCHAR(20)      NOT NULL,
    Formula          NVARCHAR(2000)    NOT NULL,
    RecordedAt DATETIMEOFFSET(7) NOT NULL, RecordedBy NVARCHAR(128) NOT NULL,
    SupersededAt DATETIMEOFFSET(7) NULL, SupersededBy NVARCHAR(128) NULL,
    DbLogin NVARCHAR(128) NOT NULL CONSTRAINT [DF_$(Prefix)DICT_CasFormula_Login] DEFAULT ORIGINAL_LOGIN(),
    CONSTRAINT [UQ_$(Prefix)DICT_CasFormula_Version] UNIQUE (RowId, Version)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_CasFormula_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_CasFormula]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_CasFormula_Key] ON [$(Schema)].[$(Prefix)DICT_CasFormula] (Project, GroupId, PoolId) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 33)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (33, N'033_narzuty_cas_identyfikatory.sql', '0.48.0');
GO
