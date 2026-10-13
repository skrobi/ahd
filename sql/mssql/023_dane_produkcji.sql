/* PZL-EV – migracja 023: dane startowe słowników „Wskaźniki DJK” i „Parametry produkcji” (migracja 022) – wartości
   raportu produkcyjnego S70MR: DJK W2–W4 10%, W5 15%, W6 20%; produktywność IPT z 12 miesięcy; materiały dostarczone –
   statusy DOST, WYD; wartość ÷ (1 + Z_CLO). Dopisuje tylko brakujące pozycje (zmiany użytkowników zostają).
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 023_dane_produkcji.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();

INSERT INTO [$(Schema)].[$(Prefix)DICT_DjkRate] (RowId, Version, Project, WorkCenterGroup, Share, Description, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, d.WorkCenterGroup, d.Share, d.Description, @at, @by
FROM (VALUES
    (N'W2', 0.10, N'Wskaźnik jakości do godzin MFG – W20'),
    (N'W3', 0.10, N'Wskaźnik jakości do godzin MFG – W30'),
    (N'W4', 0.10, N'Wskaźnik jakości do godzin MFG – W40'),
    (N'W5', 0.15, N'Wskaźnik jakości do godzin MFG – W50'),
    (N'W6', 0.20, N'Wskaźnik jakości do godzin MFG – W60')
) d (WorkCenterGroup, Share, Description)
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_DjkRate] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.WorkCenterGroup = d.WorkCenterGroup);

INSERT INTO [$(Schema)].[$(Prefix)DICT_ProductionParameter] (RowId, Version, Project, Name, Value, Description, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, p.Name, p.Value, p.Description, @at, @by
FROM (VALUES
    (N'Okno produktywności IPT (mies.)', N'12', N'Produktywność IPT = Σ CzTechPon ÷ Σ CzRzecz z operacji zakończonych w tylu ostatnich miesiącach (vAHDD_PL_CPI)'),
    (N'Statusy materiałów dostarczonych', N'DOST, WYD', N'Statusy vAPD liczone jako materiał dostarczony (Actual Material)'),
    (N'Wartość materiałów ÷ (1 + Z_CLO)', N'tak', N'Wartość materiałów bez narzutu Z_CLO (jak raport S70MR)')
) p (Name, Value, Description)
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_ProductionParameter] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.Name = p.Name);

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 23)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (23, N'023_dane_produkcji.sql', '0.38.0');
GO
