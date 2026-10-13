/* PZL-EV – migracja 032: dane startowe formuł narzutów CAS (migracja 031) – odwzorowanie formuł arkusza finansów:
   - „Pule narzutu CAS”: 17 pul w kolejności kolumn arkusza (12 – Koszt, 5 – FCCOM);
   - „Formuły narzutów CAS”: podstawy pul 10 grup narzutowych (migracja 030) – składniki w kolejności z arkusza.
   Dopisuje tylko brakujące pozycje; formuły grupy tylko, gdy grupa nie ma jeszcze żadnej formuły (zmiany użytkowników
   zostają). Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 032_dane_formuly_narzutow_cas.sql */

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
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasPool] x WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.Pool = p.Pool);

INSERT INTO [$(Schema)].[$(Prefix)DICT_CasBurdenFormula] (RowId, Version, Project, BurdenGroup, Pool, Basis, RecordedAt, RecordedBy)
SELECT NEXT VALUE FOR [$(Schema)].[$(Prefix)META_LogicalId], 1, NULL, f.BurdenGroup, f.Pool, f.Basis, @at, @by
FROM (VALUES
    (N'GS labor', N'General Services (GS)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'GS labor', N'GS Fringe', N'Base cost elements'),
    (N'GS labor', N'General & Administrative (GA)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'GS labor', N'Corporate Flowdowns (FD)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'GS labor', N'FCCOM GS', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe'),
    (N'GS labor', N'FCCOM G&A', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'Direct Materials', N'Material Handling (MTL)', N'Base cost elements'),
    (N'Direct Materials', N'Miscellaneous Material Pool (MMP)', N'Base cost elements'),
    (N'Direct Materials', N'General Services (GS)', N'Material Handling (MTL)'),
    (N'Direct Materials', N'General & Administrative (GA)', N'Material Handling (MTL) + General Services (GS)'),
    (N'Direct Materials', N'Corporate Flowdowns (FD)', N'Material Handling (MTL) + General Services (GS)'),
    (N'Direct Materials', N'FCCOM MTL', N'Base cost elements'),
    (N'Direct Materials', N'FCCOM GS', N'Material Handling (MTL)'),
    (N'Direct Materials', N'FCCOM G&A', N'Material Handling (MTL) + General Services (GS)'),
    (N'Direct Services', N'Material Handling (MTL)', N'Base cost elements'),
    (N'Direct Services', N'General Services (GS)', N'Material Handling (MTL)'),
    (N'Direct Services', N'General & Administrative (GA)', N'Material Handling (MTL) + General Services (GS)'),
    (N'Direct Services', N'Corporate Flowdowns (FD)', N'Material Handling (MTL) + General Services (GS)'),
    (N'Direct Services', N'FCCOM MTL', N'Base cost elements'),
    (N'Direct Services', N'FCCOM GS', N'Material Handling (MTL)'),
    (N'Direct Services', N'FCCOM G&A', N'Material Handling (MTL) + General Services (GS)'),
    (N'Travel', N'General Services (GS)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'Travel', N'General & Administrative (GA)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'Travel', N'Corporate Flowdowns (FD)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'Travel', N'FCCOM G&A', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'MFG labor', N'MFG Fringe', N'Base cost elements'),
    (N'MFG labor', N'MFG OH', N'Base cost elements + MFG Fringe'),
    (N'MFG labor', N'General Services (GS)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'MFG labor', N'General & Administrative (GA)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'MFG labor', N'Corporate Flowdowns (FD)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'MFG labor', N'FCCOM MFG', N'Base cost elements + MFG Fringe'),
    (N'MFG labor', N'FCCOM GS', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe'),
    (N'MFG labor', N'FCCOM G&A', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'ENG labor', N'ENG Fringe', N'Base cost elements'),
    (N'ENG labor', N'ENG OH', N'Base cost elements + ENG Fringe'),
    (N'ENG labor', N'General Services (GS)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'ENG labor', N'General & Administrative (GA)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'ENG labor', N'Corporate Flowdowns (FD)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'ENG labor', N'FCCOM ENG', N'Base cost elements + ENG Fringe'),
    (N'ENG labor', N'FCCOM GS', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe'),
    (N'ENG labor', N'FCCOM G&A', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'ENG LL labor', N'ENG OH', N'Base cost elements + ENG Fringe'),
    (N'ENG LL labor', N'General Services (GS)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'ENG LL labor', N'General & Administrative (GA)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'ENG LL labor', N'Corporate Flowdowns (FD)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'ENG LL labor', N'FCCOM ENG', N'Base cost elements + ENG Fringe'),
    (N'ENG LL labor', N'FCCOM GS', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe'),
    (N'ENG LL labor', N'FCCOM G&A', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'MTL labor', N'MTL Fringe', N'Base cost elements'),
    (N'MTL labor', N'General Services (GS)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'MTL labor', N'General & Administrative (GA)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'MTL labor', N'Corporate Flowdowns (FD)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'MTL labor', N'FCCOM GS', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'MTL labor', N'FCCOM G&A', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'MSV labor', N'General Services (GS)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe'),
    (N'MSV labor', N'MSV Fringe', N'Base cost elements'),
    (N'MSV labor', N'General & Administrative (GA)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'MSV labor', N'Corporate Flowdowns (FD)', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe'),
    (N'MSV labor', N'FCCOM GS', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe'),
    (N'MSV labor', N'FCCOM G&A', N'Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe')
) f (BurdenGroup, Pool, Basis)
WHERE NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasBurdenFormula] x
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.BurdenGroup = f.BurdenGroup AND x.Pool = f.Pool)
  AND NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_CasBurdenFormula] x   -- grupa ma już formuły
                  WHERE x.Project IS NULL AND x.SupersededAt IS NULL AND x.BurdenGroup = f.BurdenGroup);

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 32)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (32, N'032_dane_formuly_narzutow_cas.sql', '0.47.0');
GO
