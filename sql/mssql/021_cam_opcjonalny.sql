/* PZL-EV – migracja 021: CAM w słowniku „WP i CAM” uzupełniany później (docs/slowniki.md, rozdz. 5.2).
   Najpierw wskazuje się WP (znacznik w strukturze projektu), CAM dopisuje się potem: DICT_WpCam.Cam dopuszcza NULL.
   WP bez CAM – ostrzeżenie przy zapisie i brak w strukturze; gotowość projektu blokuje przebieg, dopóki CAM nie jest
   uzupełniony. Skrypt jest idempotentny. Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie); ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 021_cam_opcjonalny.sql */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_WpCam]') AND name = N'Cam' AND is_nullable = 0)
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_WpCam] ALTER COLUMN Cam NVARCHAR(128) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 21)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (21, N'021_cam_opcjonalny.sql', '0.34.0');
GO
