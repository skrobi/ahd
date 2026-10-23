/* PZL-EV – migracja 059: łączenie źródeł w przebiegu (docs/pipeline-fazy.md, P3; tasks/F6.1).
   - RUN_CostAssignment: wynik klocka „Łączenie źródeł” – koszt elementu WBS z danych uruchomienia (tabela „ev”) przypisany do WP, CAM
     i Cost Category jak w strukturze programu; WP NULL – koszt bez przypisania, Gap – powód (bez WP, kilka WP, spoza nakładki).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 059_laczenie_zrodel.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)RUN_CostAssignment]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)RUN_CostAssignment] (
    PinId         BIGINT          NOT NULL,
    WbsElement    NVARCHAR(400)   NOT NULL,
    Wp            NVARCHAR(100)   NULL,
    Cam           NVARCHAR(128)   NULL,
    CostCategory  NVARCHAR(100)   NULL,
    Gap           NVARCHAR(200)   NULL,
    Amount        DECIMAL(28,8)   NOT NULL,
    INDEX [IX_$(Prefix)RUN_CostAssignment_Pin] CLUSTERED (PinId, WbsElement)
);
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 59)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (59, N'059_laczenie_zrodel.sql', '0.65.0');
GO
