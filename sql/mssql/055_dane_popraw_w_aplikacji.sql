/* PZL-EV – migracja 055 (dane): „Popraw w aplikacji” w regułach startowych (docs/pipeline-fazy.md, rozdz. 4.1; tasks/F5.4).
   - „Reguły walidacji” (DICT_RunValidation): reguły startowe „Cost element spoza słownika Cost Category”, „Cost element bez Cost
     Category”, „MPK bez stawki wydziału na rok”, „WP bez MPK”, „WP bez budżetu” – zapytanie z kolumnami Slownik (słownik do poprawy)
     i Filtr (wartość filtra tabeli słownika). Nowa wersja reguły (historia zostaje) tylko, gdy zapytanie jest takie jak z migracji
     053 (reguła zmieniona przez użytkownika – bez zmian). Treść jak RunRules.Starters (test SqlPresetsTests porównuje).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 055_dane_popraw_w_aplikacji.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = N'migracja 055 – Popraw w aplikacji';

-- Zapytanie z migracji 053 (OldSql) → zapytanie z kolumnami Slownik i Filtr (NewSql).
DECLARE @rules TABLE (Name NVARCHAR(200) PRIMARY KEY, OldSql NVARCHAR(MAX) NOT NULL, NewSql NVARCHAR(MAX) NOT NULL);
INSERT INTO @rules (Name, OldSql, NewSql) VALUES
    (N'Cost element spoza słownika Cost Category',
     N'SELECT b.CostElement, SUM(b.Wiersze) AS Wiersze, SUM(b.Kwota) AS Kwota
FROM biezace b
WHERE b.CostElement IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM cost_category c WHERE c.CostElement = b.CostElement)
GROUP BY b.CostElement
ORDER BY ABS(SUM(b.Kwota)) DESC, b.CostElement',
     N'SELECT b.CostElement, SUM(b.Wiersze) AS Wiersze, SUM(b.Kwota) AS Kwota, N''Cost Category'' AS Slownik, b.CostElement AS Filtr
FROM biezace b
WHERE b.CostElement IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM cost_category c WHERE c.CostElement = b.CostElement)
GROUP BY b.CostElement
ORDER BY ABS(SUM(b.Kwota)) DESC, b.CostElement'),
    (N'Cost element bez Cost Category',
     N'SELECT e.CostElement, c.Opis, SUM(e.Kwota) AS Kwota
FROM ev e
JOIN cost_category c ON c.CostElement = e.CostElement
WHERE c.CostCategory IS NULL
GROUP BY e.CostElement, c.Opis
ORDER BY e.CostElement',
     N'SELECT e.CostElement, c.Opis, SUM(e.Kwota) AS Kwota, N''Cost Category'' AS Slownik, e.CostElement AS Filtr
FROM ev e
JOIN cost_category c ON c.CostElement = e.CostElement
WHERE c.CostCategory IS NULL
GROUP BY e.CostElement, c.Opis
ORDER BY e.CostElement'),
    (N'MPK bez stawki wydziału na rok',
     N'SELECT w.MPK, CAST(LEFT(@BiezacyOkres, 4) AS INT) AS Rok, COUNT(*) AS LiczbaWP,
       STRING_AGG(CAST(w.WP AS NVARCHAR(MAX)), N'', '') WITHIN GROUP (ORDER BY w.WP) AS ListaWP
FROM (SELECT DISTINCT WP, MPK FROM wp_cam WHERE MPK IS NOT NULL) w
WHERE NOT EXISTS (SELECT 1 FROM stawki s WHERE s.MPK = w.MPK AND s.Rok = CAST(LEFT(@BiezacyOkres, 4) AS INT))
GROUP BY w.MPK
ORDER BY w.MPK',
     N'SELECT w.MPK, CAST(LEFT(@BiezacyOkres, 4) AS INT) AS Rok, COUNT(*) AS LiczbaWP,
       STRING_AGG(CAST(w.WP AS NVARCHAR(MAX)), N'', '') WITHIN GROUP (ORDER BY w.WP) AS ListaWP,
       N''Stawki wydziałów'' AS Slownik, w.MPK AS Filtr
FROM (SELECT DISTINCT WP, MPK FROM wp_cam WHERE MPK IS NOT NULL) w
WHERE NOT EXISTS (SELECT 1 FROM stawki s WHERE s.MPK = w.MPK AND s.Rok = CAST(LEFT(@BiezacyOkres, 4) AS INT))
GROUP BY w.MPK
ORDER BY w.MPK'),
    (N'WP bez MPK',
     N'SELECT WP
FROM wp_cam
GROUP BY WP
HAVING COUNT(MPK) = 0
ORDER BY WP',
     N'SELECT WP, N''WP i CAM'' AS Slownik, WP AS Filtr
FROM wp_cam
GROUP BY WP
HAVING COUNT(MPK) = 0
ORDER BY WP'),
    (N'WP bez budżetu',
     N'SELECT DISTINCT w.WP
FROM wp_cam w
WHERE NOT EXISTS (SELECT 1 FROM harmonogram h
                  WHERE h.WP = w.WP AND (ISNULL(h.BAC, 0) <> 0 OR ISNULL(h.BacHours, 0) <> 0 OR ISNULL(h.BacMaterial, 0) <> 0))
ORDER BY w.WP',
     N'SELECT DISTINCT w.WP, N''Harmonogram i budżet'' AS Slownik, w.WP AS Filtr
FROM wp_cam w
WHERE NOT EXISTS (SELECT 1 FROM harmonogram h
                  WHERE h.WP = w.WP AND (ISNULL(h.BAC, 0) <> 0 OR ISNULL(h.BacHours, 0) <> 0 OR ISNULL(h.BacMaterial, 0) <> 0))
ORDER BY w.WP');

DECLARE @current TABLE (RowId BIGINT PRIMARY KEY, Version INT NOT NULL, Name NVARCHAR(200) NOT NULL, Description NVARCHAR(1000) NULL,
                        Level NVARCHAR(10) NOT NULL, IsActive BIT NOT NULL, ResultTemplate NVARCHAR(2000) NULL);
UPDATE v SET SupersededAt = @at, SupersededBy = @by
OUTPUT deleted.RowId, deleted.Version, deleted.Name, deleted.Description, deleted.Level, deleted.IsActive, deleted.ResultTemplate
INTO @current (RowId, Version, Name, Description, Level, IsActive, ResultTemplate)
FROM [$(Schema)].[$(Prefix)DICT_RunValidation] v
JOIN @rules r ON r.Name = v.Name
WHERE v.Project IS NULL AND v.SupersededAt IS NULL
  AND REPLACE(v.SqlText, NCHAR(13), N'') = REPLACE(r.OldSql, NCHAR(13), N'');   -- zapytanie zmienione przez użytkownika zostaje

INSERT INTO [$(Schema)].[$(Prefix)DICT_RunValidation] (RowId, Version, Project, Name, Description, Level, IsActive, SqlText, ResultTemplate, RecordedAt, RecordedBy)
SELECT c.RowId, c.Version + 1, NULL, c.Name, c.Description, c.Level, c.IsActive, r.NewSql, c.ResultTemplate, @at, @by
FROM @current c
JOIN @rules r ON r.Name = c.Name;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 55)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (55, N'055_dane_popraw_w_aplikacji.sql', '0.61.0');
GO
