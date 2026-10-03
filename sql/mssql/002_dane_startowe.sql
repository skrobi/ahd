/* PZL-EV – migracja 002: dane startowe (presety) etapu 1.
   - definicje źródeł ACTUALS_PAF i ACTUALS_CES (parser ACTUALS, układ kolumn – docs/zrodla-danych.md, rozdz. 4),
   - lokalizacja RABIT E456659 (SharePoint przez WebDAV) – aktywna,
   - kalendarz okresów 2026–2027: tygodnie ISO, okres = miesiąc czwartku tygodnia, ostatni tydzień okresu zamykający (O18),
   - Cost Category – załącznik A (docs/slowniki.md).
   Stawki wydziałów, kursy walut, osoby i projekty – bez danych startowych.

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) albo
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 002_dane_startowe.sql
   Skrypt dopisuje tylko brakujące wiersze (istniejące kody, prefiksy, lata kalendarza i elementy kosztowe zostają bez zmian),
   więc nie dubluje danych zapisanych wcześniej w aplikacji. Wyjątek: lokalizacja E456659 zapisana przez wcześniejsze
   dane startowe aplikacji jako nieaktywna (wersja 1) dostaje wersję 2 – aktywną. Wiersze zapisuje konto uruchamiające. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- wymagane dla indeksów filtrowanych (sqlcmd bez -I ma OFF)
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();
DECLARE @definitions INT, @locations INT, @calendar INT, @costCategory INT;

/* ---------- Definicje źródeł ---------- */

DECLARE @actualsColumns NVARCHAR(MAX) =
    N'["Project Definition","WBS Element","Cost Element","Cost element descr.","Cost element name","CO object name",'
  + N'"Transaction Currency","Value TranCurr","Object Currency","Value in Obj. Crcy","Report currency","Val.in rep.cur.",'
  + N'"Total Quantity","Partner-CCtr","Source object name","Partner Object Class","Partner object","Original material",'
  + N'"Original material description","Fiscal Year","Created on","Period"]';

INSERT INTO [$(Schema)].[$(Prefix)META_SourceDefinition]
    (DefinitionId, Version, Code, Prefix, ReportType, Columns, Signature, Parser, ParserVersion, Active, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, d.Code, d.Prefix, d.ReportType, @actualsColumns,
       '7c59f446fe9c3d5e',   -- sygnatura układu kolumn ACTUALS (HeaderSignature)
       'ACTUALS', 1, 1, @at, @by
FROM (VALUES ('ACTUALS_PAF', N'ACTUALS_PAF', N'Koszty rzeczywiste CES – PAF'),
             ('ACTUALS_CES', N'ACTUALS_CES', N'Koszty rzeczywiste CES')) AS d (Code, Prefix, ReportType)
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SourceDefinition] x
                  WHERE x.SupersededAt IS NULL AND (x.Code = d.Code OR x.Prefix = d.Prefix));
SET @definitions = @@ROWCOUNT;

/* ---------- Lokalizacja RABIT ---------- */

DECLARE @locationName NVARCHAR(200) = N'RABIT E456659',
        @locationPath NVARCHAR(1000) = N'\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659';

DECLARE @seededLocation BIGINT = (SELECT LocationId FROM [$(Schema)].[$(Prefix)META_SourceLocation]
                                  WHERE SupersededAt IS NULL AND Path = @locationPath AND Active = 0 AND Version = 1);
IF @seededLocation IS NOT NULL
BEGIN
    UPDATE [$(Schema)].[$(Prefix)META_SourceLocation] SET SupersededAt = @at, SupersededBy = @by
    WHERE LocationId = @seededLocation AND SupersededAt IS NULL;
    INSERT INTO [$(Schema)].[$(Prefix)META_SourceLocation] (LocationId, Version, Name, Path, Active, RecordedAt, RecordedBy)
    SELECT LocationId, 2, Name, Path, 1, @at, @by FROM [$(Schema)].[$(Prefix)META_SourceLocation]
    WHERE LocationId = @seededLocation AND Version = 1;
    SET @locations = @@ROWCOUNT;
END
ELSE
BEGIN
    INSERT INTO [$(Schema)].[$(Prefix)META_SourceLocation] (LocationId, Version, Name, Path, Active, RecordedAt, RecordedBy)
    SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, @locationName, @locationPath, 1, @at, @by
    WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SourceLocation]
                      WHERE SupersededAt IS NULL AND (Name = @locationName OR Path = @locationPath));
    SET @locations = @@ROWCOUNT;
END;

/* ---------- Kalendarz okresów 2026–2027 ---------- */

-- Tygodnie ISO: poniedziałek tygodnia 1 = poniedziałek tygodnia z 4 stycznia (1900-01-01 to poniedziałek);
-- rok tygodnia i okres – według czwartku.
DECLARE @weeks TABLE (Year INT, Monday DATE, Period VARCHAR(7));
DECLARE @firstMonday DATE = DATEADD(DAY, -(DATEDIFF(DAY, '19000101', '20260104') % 7), '20260104');
INSERT INTO @weeks (Year, Monday, Period)
SELECT YEAR(DATEADD(DAY, 3, m.Monday)), m.Monday, CONVERT(CHAR(7), DATEADD(DAY, 3, m.Monday), 126)
FROM (SELECT TOP (110) DATEADD(WEEK, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, @firstMonday) AS Monday
      FROM sys.all_objects) AS m;

DECLARE @calendarRows TABLE (Year INT, Week INT, Period VARCHAR(7), DateFrom DATE, DateTo DATE, IsClosing BIT);
INSERT INTO @calendarRows (Year, Week, Period, DateFrom, DateTo, IsClosing)
SELECT Year, ROW_NUMBER() OVER (PARTITION BY Year ORDER BY Monday), Period, Monday, DATEADD(DAY, 6, Monday),
       CASE WHEN LEAD(Period) OVER (PARTITION BY Year ORDER BY Monday) = Period THEN 0 ELSE 1 END
FROM @weeks
WHERE Year IN (2026, 2027);

INSERT INTO [$(Schema)].[$(Prefix)DICT_Calendar]
    (RowId, Version, Project, Year, Week, Period, DateFrom, DateTo, IsClosing, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, c.Year, c.Week, c.Period, c.DateFrom, c.DateTo, c.IsClosing, @at, @by
FROM @calendarRows c
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_Calendar] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.Year = c.Year);
SET @calendar = @@ROWCOUNT;

/* ---------- Cost Category – załącznik A (docs/slowniki.md) ---------- */

INSERT INTO [$(Schema)].[$(Prefix)DICT_CostCategory]
    (RowId, Version, Project, CostElement, Description, Area, CostCategory, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, a.CostElement, a.Description, a.Area, a.CostCategory, @at, @by
FROM (VALUES
    ('0051105550', N'PZL Mat Consump', NULL, N'Direct Materials'),
    ('0057100000', N'Proj Sttlmnt Bill', NULL, NULL),
    ('0051110550', N'PZL Oth Dir Serv ODS', NULL, N'Direct Services'),
    ('0057511550', N'PZL ODC NVA', NULL, N'Other Direct Cost'),
    ('0057712550', N'PZL Direct MTS BFM', NULL, N'Chemicals'),
    ('0057714550', N'PZL Dir Packaging NV', NULL, N'Packaging Materials'),
    ('0057120550', N'PZL Travel', NULL, N'Travel costs'),
    ('0092210550', N'PZL Quality Control', N'Quality Control', N'Manufacturing and QA labor'),
    ('0092211550', N'PZL Pain Spec Proc', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092212550', N'PZL Machining (W30)', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092213550', N'PZL Sheet metl W40', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092214550', N'PZL Sub assem W51', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092216550', N'PZL Fnl assem W53', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092215550', N'PZL Sub assem W52', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092217550', N'PZL Hangar Ops W60', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092218550', N'PZL Svc ctr W70 DUS', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092219550', N'Tooling', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0092223550', N'PZL LM Aero Coop W54', N'Manufacturing', N'Manufacturing and QA labor'),
    ('0094410550', N'PZL Des Eng/Proc Eng', N'Engineering', N'Engineering labor'),
    ('0094414550', N'PZL Des Industrial E', N'Engineering', N'Engineering labor'),
    ('0094412550', N'PZL Programs', N'Programs', N'Programs labor'),
    ('0094490550', N'PZL LL Des Eng/Proc', N'Engineering', N'Engineering labor'),
    ('0096606550', N'PZL Gen Svcs labor', N'General Services', N'General Services'),
    ('9221X550', N'PZL MFG Indirect', N'Manufacturing', N'Manufacturing and QA labor'),
    ('9222X550', N'PZL Quality Indirect', N'Quality Control', N'Manufacturing and QA labor'),
    ('9229X550', N'LL Mfg Indirect', N'Manufacturing', N'Manufacturing and QA labor'),
    ('9229D550', N'PZL LL LM Aero C W54', N'LL Manufacturing', N'Manufacturing and QA LL labor'),
    ('9441X550', N'PZL ENG Indirect', N'Engineering', N'Engineering labor'),
    ('9660R550', N'PZL Mfg Svcs labor', N'Manufacturing Services', N'Manufacturing Services'),
    ('0096610550', N'Procurement', N'Procurement', N'Procurement Labor'),
    ('9662R550', N'PZL Customs and Tran', N'Manufacturing Services', N'Manufacturing Services'),
    ('0096626550', N'PZL Shipping', N'General Services', N'General Services'),
    ('0096616550', N'PZL Finance', N'Finance', N'Finance')
) AS a (CostElement, Description, Area, CostCategory)
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CostCategory] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.CostElement = a.CostElement);
SET @costCategory = @@ROWCOUNT;

/* ---------- Dziennik i wersja ---------- */

INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
VALUES (@at, @by, N'Migracje', CONCAT(N'Migracja 002 – dane startowe: definicje źródeł ', @definitions, N', lokalizacje RABIT ', @locations,
        N', kalendarz okresów ', @calendar, N' tyg., Cost Category ', @costCategory, N' wierszy'));

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 2)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (2, N'002_dane_startowe.sql', '0.11.0');
GO
