/* PZL-EV – migracja 034: dane startowe narzutów CAS (migracje 029, 031, 033) – odwzorowanie arkusza finansów z wyliczeniem
   narzutów CAS na koszty Cost Elementów (lata 2025–2026); zastępuje dane startowe 030 i 032:
   - „Pule narzutu CAS”: 17 pul w kolejności kolumn arkusza (12 – Koszt, 5 – FCCOM);
   - „Grupy narzutowe CAS”: 10 grup;
   - „Wskaźniki narzutu CAS”: wskaźniki pul na 2025 i 2026 (od – do = rok; 15 cyfr znaczących, jak w Excelu);
   - „Formuły narzutów CAS”: formuły pul grup z formuł arkusza (klocki: base, p:<pula>, r:<wskaźnik puli>);
   - „Grupa narzutowa CAS” w globalnym Cost Category dla elementów kosztowych arkusza – nowa wersja pozycji bez grupy.
   Pule i grupy z danych startowych dopasowane po nazwie – bieżącej albo z historii (pozycja przemianowana przez
   użytkownika); pozycja, która kiedykolwiek miała daną nazwę, nie jest dodawana ponownie.
   Dopisuje tylko brakujące pozycje; formuły grupy tylko, gdy grupa nie ma jeszcze żadnej formuły (zmiany użytkowników
   zostają). Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 034_dane_narzuty_cas.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();

INSERT INTO [$(Schema)].[$(Prefix)DICT_CasPool] (RowId, Version, Project, Pool, SortOrder, Kind, Description, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, p.Pool, p.SortOrder, p.Kind, NULL, @at, @by
FROM (VALUES
    (N'MFG Fringe', 10, N'Koszt'),
    (N'MFG OH', 20, N'Koszt'),
    (N'ENG Fringe', 30, N'Koszt'),
    (N'ENG OH', 40, N'Koszt'),
    (N'MTL Fringe', 50, N'Koszt'),
    (N'Material Handling (MTL)', 60, N'Koszt'),
    (N'Miscellaneous Material Pool (MMP)', 70, N'Koszt'),
    (N'General Services (GS)', 80, N'Koszt'),
    (N'GS Fringe', 90, N'Koszt'),
    (N'MSV Fringe', 100, N'Koszt'),
    (N'General & Administrative (GA)', 110, N'Koszt'),
    (N'Corporate Flowdowns (FD)', 120, N'Koszt'),
    (N'FCCOM MFG', 130, N'FCCOM'),
    (N'FCCOM ENG', 140, N'FCCOM'),
    (N'FCCOM MTL', 150, N'FCCOM'),
    (N'FCCOM GS', 160, N'FCCOM'),
    (N'FCCOM G&A', 170, N'FCCOM')
) p (Pool, SortOrder, Kind)
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasPool] x WHERE x.Project IS NULL AND x.Pool = p.Pool);   -- także nazwa z historii (zmieniona, usunięta)

INSERT INTO [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] (RowId, Version, Project, BurdenGroup, Description, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, g.BurdenGroup, g.Description, @at, @by
FROM (VALUES
    (N'MFG labor', N'Robocizna produkcji i jakości (MFG Fringe, MFG OH)'),
    (N'ENG labor', N'Robocizna inżynierii i programów (ENG Fringe, ENG OH)'),
    (N'ENG LL labor', N'Inżynieria – praca leasingowana (ENG OH bez ENG Fringe)'),
    (N'MSV labor', N'Robocizna pośrednia i usług produkcyjnych (MSV Fringe)'),
    (N'GS labor', N'Robocizna General Services – finanse, HR, wysyłka (GS Fringe)'),
    (N'MTL labor', N'Robocizna zakupów – Procurement (MTL Fringe)'),
    (N'Direct Materials', N'Materiały bezpośrednie – suche koszty (MTL Handling, MMP; GS i G&A od MTL Handling)'),
    (N'Direct Services', N'Usługi bezpośrednie – suche koszty (MTL Handling; GS i G&A od MTL Handling)'),
    (N'Travel', N'Podróże – suche koszty (GS, G&A, FD, FCCOM G&A od kosztu)'),
    (N'ODC', N'Other Direct Cost – bez narzutów')
) g (BurdenGroup, Description)
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] x WHERE x.Project IS NULL AND x.BurdenGroup = g.BurdenGroup);   -- także nazwa z historii

/* Nazwa puli / grupy z danych startowych → identyfikator (RowId) pozycji istniejącej: bieżąca nazwa, a gdy jej nie ma –
   nazwa z historii (pozycja przemianowana przez użytkownika; najpóźniej zastąpiona wersja). */
DECLARE @pools TABLE (Pool NVARCHAR(60) PRIMARY KEY, Id NVARCHAR(20) NOT NULL);
INSERT INTO @pools (Pool, Id)
SELECT n.Pool, CAST(p.RowId AS NVARCHAR(20))
FROM (SELECT DISTINCT Pool FROM [$(Schema)].[$(Prefix)DICT_CasPool] WHERE Project IS NULL) n
CROSS APPLY (SELECT TOP (1) x.RowId FROM [$(Schema)].[$(Prefix)DICT_CasPool] x
             WHERE x.Project IS NULL AND x.Pool = n.Pool
               AND EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasPool] c WHERE c.RowId = x.RowId AND c.SupersededAt IS NULL)
             ORDER BY CASE WHEN x.SupersededAt IS NULL THEN 0 ELSE 1 END, x.SupersededAt DESC) p;
DECLARE @groups TABLE (BurdenGroup NVARCHAR(50) PRIMARY KEY, Id NVARCHAR(20) NOT NULL);
INSERT INTO @groups (BurdenGroup, Id)
SELECT n.BurdenGroup, CAST(g.RowId AS NVARCHAR(20))
FROM (SELECT DISTINCT BurdenGroup FROM [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] WHERE Project IS NULL) n
CROSS APPLY (SELECT TOP (1) x.RowId FROM [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] x
             WHERE x.Project IS NULL AND x.BurdenGroup = n.BurdenGroup
               AND EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] c WHERE c.RowId = x.RowId AND c.SupersededAt IS NULL)
             ORDER BY CASE WHEN x.SupersededAt IS NULL THEN 0 ELSE 1 END, x.SupersededAt DESC) g;

INSERT INTO [$(Schema)].[$(Prefix)DICT_CasBurdenRate] (RowId, Version, Project, PoolId, ValidFrom, ValidTo, Rate, Description, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, p.Id, y.ValidFrom, y.ValidTo, r.Rate, N'Dane startowe – arkusz finansów', @at, @by
FROM (VALUES
    (2025, N'MFG Fringe', 1.06233159570235), (2025, N'MFG OH', 1.76150251674384),
    (2025, N'ENG Fringe', 0.737503296029335), (2025, N'ENG OH', 1.05331918144748),
    (2025, N'MTL Fringe', 0.430853236436042), (2025, N'Material Handling (MTL)', 0.0178784634054574),
    (2025, N'Miscellaneous Material Pool (MMP)', 0.0463548955021417), (2025, N'General Services (GS)', 0.132711472288235),
    (2025, N'GS Fringe', 0.407619060713913), (2025, N'MSV Fringe', 0.473159008826316),
    (2025, N'General & Administrative (GA)', 0.0744968959824959), (2025, N'Corporate Flowdowns (FD)', 0.315875530135646),
    (2025, N'FCCOM MFG', 0.0574717300928581), (2025, N'FCCOM ENG', 0.00912959562052103),
    (2025, N'FCCOM MTL', 0.0000779828022760499), (2025, N'FCCOM GS', 0.000917430369333891),
    (2025, N'FCCOM G&A', 0.0000614458241329441), (2026, N'MFG Fringe', 0.865561173674426),
    (2026, N'MFG OH', 2.24176506536771), (2026, N'ENG Fringe', 0.710797046593161),
    (2026, N'ENG OH', 0.938285648302162), (2026, N'MTL Fringe', 0.405676323106694),
    (2026, N'Material Handling (MTL)', 0.0350110668526547), (2026, N'Miscellaneous Material Pool (MMP)', 0.0902344508242422),
    (2026, N'General Services (GS)', 0.119049688588562), (2026, N'GS Fringe', 0.449690738865934),
    (2026, N'MSV Fringe', 0.398011357857746), (2026, N'General & Administrative (GA)', 0.0899798551840883),
    (2026, N'Corporate Flowdowns (FD)', 0.306886068654226), (2026, N'FCCOM MFG', 0.0536861142743019),
    (2026, N'FCCOM ENG', 0.0101368562592914), (2026, N'FCCOM MTL', 0.000126097618413945),
    (2026, N'FCCOM GS', 0.000671427815429887), (2026, N'FCCOM G&A', 0.000057456819309837)
) r (Year, Pool, Rate)
JOIN @pools p ON p.Pool = r.Pool
CROSS APPLY (SELECT DATEFROMPARTS(r.Year, 1, 1) AS ValidFrom, DATEFROMPARTS(r.Year, 12, 31) AS ValidTo) y
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasBurdenRate] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.PoolId = p.Id
                    AND x.ValidFrom <= y.ValidTo AND ISNULL(x.ValidTo, '9999-12-31') >= y.ValidFrom);

/* Formuły: klocki p{nazwa puli} / r{nazwa puli} zamieniane na identyfikatory; grupa z nazwą puli spoza słownika pul – pominięta. */
DECLARE @formulas TABLE (BurdenGroup NVARCHAR(50) NOT NULL, Pool NVARCHAR(60) NOT NULL, Formula NVARCHAR(2000) NOT NULL);
INSERT INTO @formulas (BurdenGroup, Pool, Formula) VALUES
    (N'GS labor', N'General Services (GS)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}'),
    (N'GS labor', N'GS Fringe', N'base * r{GS Fringe}'),
    (N'GS labor', N'General & Administrative (GA)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}'),
    (N'GS labor', N'Corporate Flowdowns (FD)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}'),
    (N'GS labor', N'FCCOM GS', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}'),
    (N'GS labor', N'FCCOM G&A', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}'),
    (N'Direct Materials', N'Material Handling (MTL)', N'base * r{Material Handling (MTL)}'),
    (N'Direct Materials', N'Miscellaneous Material Pool (MMP)', N'base * r{Miscellaneous Material Pool (MMP)}'),
    (N'Direct Materials', N'General Services (GS)', N'p{Material Handling (MTL)} * r{General Services (GS)}'),
    (N'Direct Materials', N'General & Administrative (GA)', N'( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{General & Administrative (GA)}'),
    (N'Direct Materials', N'Corporate Flowdowns (FD)', N'( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{Corporate Flowdowns (FD)}'),
    (N'Direct Materials', N'FCCOM MTL', N'base * r{FCCOM MTL}'),
    (N'Direct Materials', N'FCCOM GS', N'p{Material Handling (MTL)} * r{FCCOM GS}'),
    (N'Direct Materials', N'FCCOM G&A', N'( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{FCCOM G&A}'),
    (N'Direct Services', N'Material Handling (MTL)', N'base * r{Material Handling (MTL)}'),
    (N'Direct Services', N'General Services (GS)', N'p{Material Handling (MTL)} * r{General Services (GS)}'),
    (N'Direct Services', N'General & Administrative (GA)', N'( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{General & Administrative (GA)}'),
    (N'Direct Services', N'Corporate Flowdowns (FD)', N'( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{Corporate Flowdowns (FD)}'),
    (N'Direct Services', N'FCCOM MTL', N'base * r{FCCOM MTL}'),
    (N'Direct Services', N'FCCOM GS', N'p{Material Handling (MTL)} * r{FCCOM GS}'),
    (N'Direct Services', N'FCCOM G&A', N'( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{FCCOM G&A}'),
    (N'Travel', N'General Services (GS)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}'),
    (N'Travel', N'General & Administrative (GA)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}'),
    (N'Travel', N'Corporate Flowdowns (FD)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}'),
    (N'Travel', N'FCCOM G&A', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}'),
    (N'MFG labor', N'MFG Fringe', N'base * r{MFG Fringe}'),
    (N'MFG labor', N'MFG OH', N'( base + p{MFG Fringe} ) * r{MFG OH}'),
    (N'MFG labor', N'General Services (GS)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}'),
    (N'MFG labor', N'General & Administrative (GA)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}'),
    (N'MFG labor', N'Corporate Flowdowns (FD)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}'),
    (N'MFG labor', N'FCCOM MFG', N'( base + p{MFG Fringe} ) * r{FCCOM MFG}'),
    (N'MFG labor', N'FCCOM GS', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}'),
    (N'MFG labor', N'FCCOM G&A', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}'),
    (N'ENG labor', N'ENG Fringe', N'base * r{ENG Fringe}'),
    (N'ENG labor', N'ENG OH', N'( base + p{ENG Fringe} ) * r{ENG OH}'),
    (N'ENG labor', N'General Services (GS)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}'),
    (N'ENG labor', N'General & Administrative (GA)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}'),
    (N'ENG labor', N'Corporate Flowdowns (FD)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}'),
    (N'ENG labor', N'FCCOM ENG', N'( base + p{ENG Fringe} ) * r{FCCOM ENG}'),
    (N'ENG labor', N'FCCOM GS', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}'),
    (N'ENG labor', N'FCCOM G&A', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}'),
    (N'ENG LL labor', N'ENG OH', N'( base + p{ENG Fringe} ) * r{ENG OH}'),
    (N'ENG LL labor', N'General Services (GS)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}'),
    (N'ENG LL labor', N'General & Administrative (GA)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}'),
    (N'ENG LL labor', N'Corporate Flowdowns (FD)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}'),
    (N'ENG LL labor', N'FCCOM ENG', N'( base + p{ENG Fringe} ) * r{FCCOM ENG}'),
    (N'ENG LL labor', N'FCCOM GS', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}'),
    (N'ENG LL labor', N'FCCOM G&A', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}'),
    (N'MTL labor', N'MTL Fringe', N'base * r{MTL Fringe}'),
    (N'MTL labor', N'General Services (GS)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}'),
    (N'MTL labor', N'General & Administrative (GA)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General & Administrative (GA)}'),
    (N'MTL labor', N'Corporate Flowdowns (FD)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{Corporate Flowdowns (FD)}'),
    (N'MTL labor', N'FCCOM GS', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{FCCOM GS}'),
    (N'MTL labor', N'FCCOM G&A', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{FCCOM G&A}'),
    (N'MSV labor', N'General Services (GS)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}'),
    (N'MSV labor', N'MSV Fringe', N'base * r{MSV Fringe}'),
    (N'MSV labor', N'General & Administrative (GA)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}'),
    (N'MSV labor', N'Corporate Flowdowns (FD)', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}'),
    (N'MSV labor', N'FCCOM GS', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}'),
    (N'MSV labor', N'FCCOM G&A', N'( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}');
DECLARE @pool NVARCHAR(60), @id NVARCHAR(20);
DECLARE names CURSOR LOCAL FAST_FORWARD FOR SELECT Pool, Id FROM @pools;
OPEN names;
FETCH NEXT FROM names INTO @pool, @id;
WHILE @@FETCH_STATUS = 0
BEGIN
    UPDATE @formulas SET Formula = REPLACE(REPLACE(Formula, N'p{' + @pool + N'}', N'p:' + @id), N'r{' + @pool + N'}', N'r:' + @id);
    FETCH NEXT FROM names INTO @pool, @id;
END;
CLOSE names;
DEALLOCATE names;

INSERT INTO [$(Schema)].[$(Prefix)DICT_CasFormula] (RowId, Version, Project, GroupId, PoolId, Formula, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, g.Id, p.Id, f.Formula, @at, @by
FROM @formulas f
JOIN @groups g ON g.BurdenGroup = f.BurdenGroup
JOIN @pools p ON p.Pool = f.Pool
WHERE NOT EXISTS (SELECT 1 FROM @formulas u WHERE u.BurdenGroup = f.BurdenGroup AND CHARINDEX(N'{', u.Formula) > 0)   -- cała grupa albo nic
  AND NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasFormula] x WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.GroupId = g.Id);

/* Grupa narzutowa elementów kosztowych arkusza – nowa wersja pozycji globalnej bez grupy. */
DECLARE @mapping TABLE (CostElement VARCHAR(10) PRIMARY KEY, BurdenGroup NVARCHAR(50) NOT NULL);
INSERT INTO @mapping (CostElement, BurdenGroup) VALUES
    ('0096606550', N'GS labor'),
    ('0051105550', N'Direct Materials'),
    ('0051110550', N'Direct Services'),
    ('0057120550', N'Travel'),
    ('0057510550', N'ODC'),
    ('0057511550', N'ODC'),
    ('0057712550', N'ODC'),
    ('0057714550', N'ODC'),
    ('0094410550', N'ENG labor'),
    ('0094414550', N'ENG labor'),
    ('0094412550', N'ENG labor'),
    ('0094490550', N'ENG LL labor'),
    ('0096610550', N'MTL labor'),
    ('9441X550', N'ENG labor'),
    ('9229X550', N'MSV labor'),
    ('9660R550', N'MSV labor'),
    ('0092210550', N'MFG labor'),
    ('0092211550', N'MFG labor'),
    ('0092212550', N'MFG labor'),
    ('0092213550', N'MFG labor'),
    ('0092214550', N'MFG labor'),
    ('0092215550', N'MFG labor'),
    ('0092216550', N'MFG labor'),
    ('0092217550', N'MFG labor'),
    ('0092218550', N'MFG labor'),
    ('0092219550', N'MFG labor'),
    ('0092223550', N'MFG labor'),
    ('9221X550', N'MFG labor'),
    ('9222X550', N'MFG labor');

DECLARE @current TABLE (RowId BIGINT PRIMARY KEY, Version INT NOT NULL, CostElement VARCHAR(10) NOT NULL, Description NVARCHAR(400) NULL,
                        Area NVARCHAR(100) NULL, CostCategory NVARCHAR(100) NULL, IsSettlement BIT NULL, BurdenGroupId NVARCHAR(20) NOT NULL);
UPDATE c SET SupersededAt = @at, SupersededBy = @by
OUTPUT deleted.RowId, deleted.Version, deleted.CostElement, deleted.Description, deleted.Area, deleted.CostCategory, deleted.IsSettlement, g.Id
INTO @current (RowId, Version, CostElement, Description, Area, CostCategory, IsSettlement, BurdenGroupId)
FROM [$(Schema)].[$(Prefix)DICT_CostCategory] c
JOIN @mapping m ON m.CostElement = c.CostElement
JOIN @groups g ON g.BurdenGroup = m.BurdenGroup
WHERE c.Project IS NULL AND c.SupersededAt IS NULL AND c.BurdenGroupId IS NULL;

INSERT INTO [$(Schema)].[$(Prefix)DICT_CostCategory]
    (RowId, Version, Project, CostElement, Description, Area, CostCategory, IsSettlement, BurdenGroupId, RecordedAt, RecordedBy)
SELECT RowId, Version + 1, NULL, CostElement, Description, Area, CostCategory, IsSettlement, BurdenGroupId, @at, @by
FROM @current;

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 34)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (34, N'034_dane_narzuty_cas.sql', '0.48.0');
GO
