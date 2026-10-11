/* PZL-EV – migracja 025: usunięcie tabel mapowania CES ↔ P1S – DICT_MappingReport (słownik „Raport mapowań CES ↔ P1S”)
   i DICT_MappingCorrection (korekty mapowania). Aplikacja ich nie używa: strona P1S projektu to Legacy WBS
   z Performance Objectives i poddrzewa LOG.WBS (docs/performance-objectives.md, rozdz. 2).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 025_bez_mapowania_ces_p1s.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_MappingReport]') IS NOT NULL
    DROP TABLE [$(Schema)].[$(Prefix)DICT_MappingReport];
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_MappingCorrection]') IS NOT NULL
    DROP TABLE [$(Schema)].[$(Prefix)DICT_MappingCorrection];
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 25)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (25, N'025_bez_mapowania_ces_p1s.sql', '0.41.0');
GO
