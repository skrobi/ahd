/* PZL-EV – migracja 030: dane startowe narzutów CAS (migracja 029) – odwzorowanie arkusza finansów z wyliczeniem
   narzutów CAS na koszty Cost Elementów (lata 2025–2026):
   - „Grupy narzutowe CAS”: 10 grup z pulami stosowanymi w formułach arkusza;
   - „Wskaźniki narzutu CAS”: wskaźniki pul na 2025 i 2026 (od – do = rok);
   - „Grupa narzutowa CAS” w globalnym słowniku Cost Category dla elementów kosztowych z arkusza – nowa wersja
     pozycji (historia zostaje), tylko gdy grupa nie jest jeszcze ustawiona.
   Dopisuje tylko brakujące pozycje (zmiany użytkowników zostają). Uruchomienie: aplikacja (Diagnostyka → Migracja albo
   pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 030_dane_narzuty_cas.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @at DATETIMEOFFSET(7) = SYSDATETIMEOFFSET(), @by NVARCHAR(128) = ORIGINAL_LOGIN();

INSERT INTO [$(Schema)].[$(Prefix)DICT_CasBurdenGroup]
    (RowId, Version, Project, BurdenGroup, Description, BaseInGsGa, MfgFringe, MfgOh, EngFringe, EngOh, MtlFringe, MtlHandling, Mmp,
     Gs, GsFringe, MsvFringe, Ga, Fd, FccomMfg, FccomEng, FccomMtl, FccomGs, FccomGa, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, g.BurdenGroup, g.Description, g.BaseInGsGa, g.MfgFringe, g.MfgOh,
       g.EngFringe, g.EngOh, g.MtlFringe, g.MtlHandling, g.Mmp, g.Gs, g.GsFringe, g.MsvFringe, g.Ga, g.Fd, g.FccomMfg, g.FccomEng,
       g.FccomMtl, g.FccomGs, g.FccomGa, @at, @by
FROM (VALUES
    --                                                                                  Base MfgF MfgOh EngF EngOh MtlF MtlH Mmp  Gs GsF MsvF Ga Fd  FcMfg FcEng FcMtl FcGs FcGa
    (N'MFG labor',        N'Robocizna produkcji i jakości (MFG Fringe, MFG OH)',                1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 1),
    (N'ENG labor',        N'Robocizna inżynierii i programów (ENG Fringe, ENG OH)',             1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 0, 1, 1),
    (N'ENG LL labor',     N'Inżynieria – praca leasingowana (ENG OH bez ENG Fringe)',           1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 0, 1, 1),
    (N'MSV labor',        N'Robocizna pośrednia i usług produkcyjnych (MSV Fringe)',            1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 0, 0, 1, 1),
    (N'GS labor',         N'Robocizna General Services – finanse, HR, wysyłka (GS Fringe)',     1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0, 1, 1),
    (N'MTL labor',        N'Robocizna zakupów – Procurement (MTL Fringe)',                      1, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 1, 1),
    (N'Direct Materials', N'Materiały bezpośrednie – suche koszty (MTL Handling, MMP; GS i G&A od MTL Handling)', 0, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1),
    (N'Direct Services',  N'Usługi bezpośrednie – suche koszty (MTL Handling; GS i G&A od MTL Handling)',       0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1),
    (N'Travel',           N'Podróże – suche koszty (GS, G&A, FD, FCCOM G&A od kosztu)',         1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 0, 1),
    (N'ODC',              N'Other Direct Cost – bez narzutów',                                  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
) g (BurdenGroup, Description, BaseInGsGa, MfgFringe, MfgOh, EngFringe, EngOh, MtlFringe, MtlHandling, Mmp, Gs, GsFringe, MsvFringe, Ga, Fd,
     FccomMfg, FccomEng, FccomMtl, FccomGs, FccomGa)
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasBurdenGroup] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.BurdenGroup = g.BurdenGroup);

INSERT INTO [$(Schema)].[$(Prefix)DICT_CasBurdenRate] (RowId, Version, Project, Pool, ValidFrom, ValidTo, Rate, Description, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, r.Pool, y.ValidFrom, y.ValidTo, r.Rate, N'Dane startowe – arkusz finansów', @at, @by
FROM (VALUES
    (2025, N'MFG Fringe', 1.0623315957023503), (2025, N'MFG OH', 1.7615025167438414),
    (2025, N'ENG Fringe', 0.7375032960293345), (2025, N'ENG OH', 1.053319181447485),
    (2025, N'MTL Fringe', 0.4308532364360421), (2025, N'Material Handling (MTL)', 0.01787846340545742),
    (2025, N'Miscellaneous Material Pool (MMP)', 0.04635489550214167), (2025, N'General Services (GS)', 0.132711472288235),
    (2025, N'GS Fringe', 0.40761906071391346), (2025, N'MSV Fringe', 0.4731590088263162),
    (2025, N'General & Administrative (GA)', 0.07449689598249593), (2025, N'Corporate Flowdowns (FD)', 0.3158755301356458),
    (2025, N'FCCOM MFG', 0.05747173009285814), (2025, N'FCCOM ENG', 0.009129595620521035),
    (2025, N'FCCOM MTL', 0.00007798280227604987), (2025, N'FCCOM GS', 0.0009174303693338915),
    (2025, N'FCCOM G&A', 0.00006144582413294412),
    (2026, N'MFG Fringe', 0.8655611736744256), (2026, N'MFG OH', 2.2417650653677073),
    (2026, N'ENG Fringe', 0.7107970465931612), (2026, N'ENG OH', 0.9382856483021619),
    (2026, N'MTL Fringe', 0.40567632310669394), (2026, N'Material Handling (MTL)', 0.035011066852654725),
    (2026, N'Miscellaneous Material Pool (MMP)', 0.09023445082424221), (2026, N'General Services (GS)', 0.11904968858856187),
    (2026, N'GS Fringe', 0.4496907388659342), (2026, N'MSV Fringe', 0.39801135785774616),
    (2026, N'General & Administrative (GA)', 0.08997985518408834), (2026, N'Corporate Flowdowns (FD)', 0.3068860686542264),
    (2026, N'FCCOM MFG', 0.05368611427430191), (2026, N'FCCOM ENG', 0.010136856259291433),
    (2026, N'FCCOM MTL', 0.0001260976184139452), (2026, N'FCCOM GS', 0.0006714278154298874),
    (2026, N'FCCOM G&A', 0.000057456819309836995)
) r (Year, Pool, Rate)
CROSS APPLY (SELECT DATEFROMPARTS(r.Year, 1, 1) AS ValidFrom, DATEFROMPARTS(r.Year, 12, 31) AS ValidTo) y
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasBurdenRate] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.Pool = r.Pool
                    AND x.ValidFrom <= y.ValidTo AND ISNULL(x.ValidTo, '9999-12-31') >= y.ValidFrom);

/* Grupa narzutowa elementów kosztowych z arkusza – nowa wersja pozycji globalnej bez grupy. */
DECLARE @mapping TABLE (CostElement VARCHAR(10) PRIMARY KEY, BurdenGroup NVARCHAR(50) NOT NULL);
INSERT INTO @mapping (CostElement, BurdenGroup) VALUES
    ('0096606550', N'GS labor'),
    ('0051105550', N'Direct Materials'),
    ('0051110550', N'Direct Services'),
    ('0057120550', N'Travel'),
    ('0057510550', N'ODC'), ('0057511550', N'ODC'), ('0057712550', N'ODC'), ('0057714550', N'ODC'),
    ('0092210550', N'MFG labor'), ('0092211550', N'MFG labor'), ('0092212550', N'MFG labor'), ('0092213550', N'MFG labor'),
    ('0092214550', N'MFG labor'), ('0092215550', N'MFG labor'), ('0092216550', N'MFG labor'), ('0092217550', N'MFG labor'),
    ('0092218550', N'MFG labor'), ('0092219550', N'MFG labor'), ('0092223550', N'MFG labor'),
    ('9221X550', N'MFG labor'), ('9222X550', N'MFG labor'),
    ('0094410550', N'ENG labor'), ('0094414550', N'ENG labor'), ('0094412550', N'ENG labor'),
    ('0094490550', N'ENG LL labor'),
    ('0096610550', N'MTL labor'),
    ('9441X550', N'ENG labor'),
    ('9229X550', N'MSV labor'), ('9660R550', N'MSV labor');

DECLARE @current TABLE (RowId BIGINT PRIMARY KEY, Version INT NOT NULL, CostElement VARCHAR(10) NOT NULL, Description NVARCHAR(400) NULL,
                        Area NVARCHAR(100) NULL, CostCategory NVARCHAR(100) NULL, IsSettlement BIT NULL, BurdenGroup NVARCHAR(50) NOT NULL);
UPDATE c SET SupersededAt = @at, SupersededBy = @by
OUTPUT deleted.RowId, deleted.Version, deleted.CostElement, deleted.Description, deleted.Area, deleted.CostCategory, deleted.IsSettlement, m.BurdenGroup
INTO @current (RowId, Version, CostElement, Description, Area, CostCategory, IsSettlement, BurdenGroup)
FROM [$(Schema)].[$(Prefix)DICT_CostCategory] c
JOIN @mapping m ON m.CostElement = c.CostElement
WHERE c.Project IS NULL AND c.SupersededAt IS NULL AND c.BurdenGroup IS NULL;

INSERT INTO [$(Schema)].[$(Prefix)DICT_CostCategory]
    (RowId, Version, Project, CostElement, Description, Area, CostCategory, IsSettlement, BurdenGroup, RecordedAt, RecordedBy)
SELECT RowId, Version + 1, NULL, CostElement, Description, Area, CostCategory, IsSettlement, BurdenGroup, @at, @by
FROM @current;

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 30)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (30, N'030_dane_narzuty_cas.sql', '0.46.0');
GO
