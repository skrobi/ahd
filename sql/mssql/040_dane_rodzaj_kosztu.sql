/* PZL-EV – migracja 040 (dane startowe): konta materiałowe w globalnym Cost Category – rodzaj kosztu Materials
   (ACWP Materials w strukturze projektu): 0051105550 PZL Mat Consump, 0051102550 PZL Mat Comsump-SAC, 0051103550 IC Profit
   Mat-SAC. Pozycja istniejąca bez rodzaju – nowa wersja z rodzajem Materials (historia zostaje; rodzaj ustawiony wcześniej
   zostaje), brakująca – dodana (Direct Materials, grupa narzutowa jak 0051105550).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 040_dane_rodzaj_kosztu.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();
DECLARE @materials TABLE (CostElement VARCHAR(10) PRIMARY KEY, Description NVARCHAR(400) NOT NULL);
INSERT INTO @materials (CostElement, Description) VALUES
    ('0051105550', N'PZL Mat Consump'),
    ('0051102550', N'PZL Mat Comsump-SAC'),
    ('0051103550', N'IC Profit Mat-SAC');

-- Pozycje istniejące bez rodzaju – nowa wersja z rodzajem Materials (historia zostaje).
DECLARE @current TABLE (RowId BIGINT PRIMARY KEY, Version INT NOT NULL, CostElement VARCHAR(10) NOT NULL, Description NVARCHAR(400) NULL,
                        Area NVARCHAR(100) NULL, CostCategory NVARCHAR(100) NULL, IsSettlement BIT NULL, BurdenGroupId NVARCHAR(20) NULL);
UPDATE c SET SupersededAt = @at, SupersededBy = @by
OUTPUT deleted.RowId, deleted.Version, deleted.CostElement, deleted.Description, deleted.Area, deleted.CostCategory, deleted.IsSettlement, deleted.BurdenGroupId
INTO @current (RowId, Version, CostElement, Description, Area, CostCategory, IsSettlement, BurdenGroupId)
FROM [$(Schema)].[$(Prefix)DICT_CostCategory] c
JOIN @materials m ON m.CostElement = c.CostElement
WHERE c.Project IS NULL AND c.SupersededAt IS NULL AND c.CostKind IS NULL;   -- rodzaj ustawiony wcześniej zostaje

INSERT INTO [$(Schema)].[$(Prefix)DICT_CostCategory]
    (RowId, Version, Project, CostElement, Description, Area, CostCategory, IsSettlement, CostKind, BurdenGroupId, RecordedAt, RecordedBy)
SELECT RowId, Version + 1, NULL, CostElement, Description, Area, CostCategory, IsSettlement, N'Materials', BurdenGroupId, @at, @by
FROM @current;

-- Brakujące pozycje – dodane jako Direct Materials z grupą narzutową jak 0051105550.
INSERT INTO [$(Schema)].[$(Prefix)DICT_CostCategory]
    (RowId, Version, Project, CostElement, Description, CostCategory, CostKind, BurdenGroupId, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, m.CostElement, m.Description, N'Direct Materials', N'Materials',
       (SELECT TOP (1) d.BurdenGroupId FROM [$(Schema)].[$(Prefix)DICT_CostCategory] d
        WHERE d.Project IS NULL AND d.SupersededAt IS NULL AND d.CostElement = '0051105550'), @at, @by
FROM @materials m
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CostCategory] c
                  WHERE c.Project IS NULL AND c.SupersededAt IS NULL AND c.CostElement = m.CostElement);
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 40)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (40, N'040_dane_rodzaj_kosztu.sql', '0.53.0');
GO
