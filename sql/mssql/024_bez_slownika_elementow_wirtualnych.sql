/* PZL-EV – migracja 024: usunięcie tabeli DICT_VirtualP1s (słownik „Elementy wirtualne P1S” z wersji 0.38.0).
   Wydzielenia godzin z elementu P1S (np. Paint) definiuje się teraz w Performance Objectives – węzeł wirtualny z kodem P1S
   ELEMENT_P1S.NAZWA.CPLGR (docs/performance-objectives.md, rozdz. 4.2). Tabela jest usuwana tylko wtedy, gdy jest pusta –
   wpisy wprowadzone w 0.38.0 zostają do przeniesienia ręcznie (węzeł wirtualny z kodem).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 024_bez_slownika_elementow_wirtualnych.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Dynamiczny SQL: bez tabeli (nowa baza) zapytanie o nią nie kompiluje się.
IF OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_VirtualP1s]') IS NOT NULL
    EXEC(N'IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)DICT_VirtualP1s]) DROP TABLE [$(Schema)].[$(Prefix)DICT_VirtualP1s];');
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 24)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (24, N'024_bez_slownika_elementow_wirtualnych.sql', '0.40.0');
GO
