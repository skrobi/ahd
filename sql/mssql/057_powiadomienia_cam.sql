/* PZL-EV – migracja 057: powiadomienia CAM w przebiegu i tygodniowy cykl estymacji (docs/pipeline-fazy.md, rozdz. 4.1; tasks/F11.7).
   - CAM_Estimate.Cycle: kod cyklu do 10 znaków – okres RRRR-MM (cykl miesięczny) albo tydzień RRRR-Tnn (cykl tygodniowy).
   - DICT_RunStep.Condition: warunek wykonania etapu szablonu – „Każdy przebieg” (puste) albo „Przebieg zamykający” (tydzień zamknięcia
     okresu w kalendarzu); DICT_RunStep.Notify – powiadomienie CAM-ów na początku etapu; klocek Zatwierdzenie ETC.
   - CAM_Estimate.ActualStart / ActualFinish: rzeczywisty start i koniec WP podane przez CAM-a (po zatwierdzeniu – „Harmonogram i budżet”).
   - CAM_Notification: wysłane powiadomienia CAM – program, cykl, CAM, adres, kto i kiedy, uruchomienie przebiegu, WP w wiadomości,
     wynik (wysłano, brak adresu, błąd).
   Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 057_powiadomienia_cam.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)CAM_Estimate]', N'Cycle') < 10
BEGIN
    ALTER TABLE [$(Schema)].[$(Prefix)CAM_Estimate] DROP CONSTRAINT [UQ_$(Prefix)CAM_Estimate_Cycle];
    ALTER TABLE [$(Schema)].[$(Prefix)CAM_Estimate] ALTER COLUMN Cycle VARCHAR(10) NOT NULL;
    ALTER TABLE [$(Schema)].[$(Prefix)CAM_Estimate] ADD CONSTRAINT [UQ_$(Prefix)CAM_Estimate_Cycle] UNIQUE (Project, Wp, Cycle);
END
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_RunStep]', N'Condition') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_RunStep] ADD Condition NVARCHAR(30) NULL;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_RunStep]', N'Notify') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_RunStep] ADD Notify BIT NULL;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)CAM_Estimate]', N'ActualStart') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)CAM_Estimate] ADD ActualStart DATE NULL, ActualFinish DATE NULL;
GO

IF OBJECT_ID(N'[$(Schema)].[$(Prefix)CAM_Notification]') IS NULL
CREATE TABLE [$(Schema)].[$(Prefix)CAM_Notification] (
    NotificationId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_$(Prefix)CAM_Notification] PRIMARY KEY,
    Project        VARCHAR(20)          NOT NULL,
    Cycle          VARCHAR(10)          NOT NULL,
    Cam            NVARCHAR(100)        NOT NULL,
    Email          NVARCHAR(256)        NULL,
    SentAt         DATETIMEOFFSET(7)    NOT NULL,
    SentBy         NVARCHAR(128)        NOT NULL,
    PinId          BIGINT               NULL,
    Wps            NVARCHAR(2000)       NOT NULL,
    Result         NVARCHAR(20)         NOT NULL,
    Message        NVARCHAR(1000)       NULL,
    INDEX [IX_$(Prefix)CAM_Notification_Cycle] (Project, Cycle, Cam)
);
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 57)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (57, N'057_powiadomienia_cam.sql', '0.63.0');
GO
