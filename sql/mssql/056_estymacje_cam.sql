/* PZL-EV – migracja 056: moduł CAM – estymacje WP dla Finansów (docs/funkcjonalnosc.md, moduł CAM; tasks/F11.1).
   - CAM_Estimate: estymacja WP programu w cyklu (okres zamknięcia RRRR-MM): CAM, status (Szkic, Wysłana, Zatwierdzona,
     Zwrócona), prognozowany start i koniec, waluta kwot materiałów, uzasadnienie CAM, uwaga analityka, kto i kiedy
     zmienił, wysłał, ocenił; jedna na program × WP × cykl.
   - CAM_EstimateLine: wiersze lat estymacji – rodzaj H (godziny) albo M (materiały, waluta estymacji), rok, propozycja
     systemu (z produkcji, vAPD albo budżetu) i wartość CAM.
   - CAM_EstimateEvent: historia estymacji – kto, kiedy, status, opis zmian, komentarz.
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 056_estymacje_cam.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAM_Estimate]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)CAM_Estimate] (
    EstimateId     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)CAM_Estimate] PRIMARY KEY,
    Project        VARCHAR(20)          NOT NULL,
    Wp             NVARCHAR(100)        NOT NULL,
    Cycle          VARCHAR(7)           NOT NULL,
    Cam            NVARCHAR(100)        NOT NULL,
    Status         NVARCHAR(20)         NOT NULL,
    ForecastStart  DATE                 NULL,
    ForecastFinish DATE                 NULL,
    Currency       VARCHAR(3)           NULL,
    Justification  NVARCHAR(2000)       NULL,
    ReviewNote     NVARCHAR(2000)       NULL,
    UpdatedAt      DATETIMEOFFSET(7)    NOT NULL,
    UpdatedBy      NVARCHAR(128)        NOT NULL,
    SubmittedAt    DATETIMEOFFSET(7)    NULL,
    SubmittedBy    NVARCHAR(128)        NULL,
    ReviewedAt     DATETIMEOFFSET(7)    NULL,
    ReviewedBy     NVARCHAR(128)        NULL,
    CONSTRAINT [UQ_$(Prefix)CAM_Estimate_Cycle] UNIQUE (Project, Wp, Cycle)
);
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAM_EstimateLine]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)CAM_EstimateLine] (
    EstimateId     BIGINT               NOT NULL CONSTRAINT [FK_$(Prefix)CAM_EstimateLine_Estimate]
                                         REFERENCES [$(Schema)].[$(Prefix)CAM_Estimate] (EstimateId) ON DELETE CASCADE,
    Kind           CHAR(1)              NOT NULL CONSTRAINT [CK_$(Prefix)CAM_EstimateLine_Kind] CHECK (Kind IN ('H', 'M')),
    Year           INT                  NOT NULL,
    Proposal       DECIMAL(28,8)        NULL,
    Value          DECIMAL(28,8)        NOT NULL,
    CONSTRAINT [PK_$(Prefix)CAM_EstimateLine] PRIMARY KEY (EstimateId, Kind, Year)
);
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAM_EstimateEvent]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)CAM_EstimateEvent] (
    EventId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)CAM_EstimateEvent] PRIMARY KEY,
    EstimateId     BIGINT               NOT NULL CONSTRAINT [FK_$(Prefix)CAM_EstimateEvent_Estimate]
                                         REFERENCES [$(Schema)].[$(Prefix)CAM_Estimate] (EstimateId) ON DELETE CASCADE,
    At             DATETIMEOFFSET(7)    NOT NULL,
    [By]           NVARCHAR(128)        NOT NULL,
    Status         NVARCHAR(20)         NOT NULL,
    Summary        NVARCHAR(1000)       NOT NULL,
    Comment        NVARCHAR(2000)       NULL,
    INDEX [IX_$(Prefix)CAM_EstimateEvent_Estimate] (EstimateId, At)
);
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 56)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (56, N'056_estymacje_cam.sql', '0.62.0');
GO
