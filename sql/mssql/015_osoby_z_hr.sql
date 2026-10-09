/* PZL-EV – migracja 015: słownik Osoby z HR (docs/slowniki.md, rozdz. 2).
   Osoby wczytywane z PZLHRPROD (HR.ORG) przyciskiem „Wczytaj z HR” na ekranie Słowniki: klucz USRID (numer znaczka,
   login – dotychczasowa kolumna AdAccount), imię i nazwisko oraz dane HR: imię, nazwisko, e-mail, MPK (KOSTL), dział
   (SHORT, LONG), stanowisko (STEXT), pion, manager, PERNR. CAM w słowniku „WP i CAM” to USRID.
   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) – w jednej transakcji; ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 015_osoby_z_hr.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_Person]', N'FirstName') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_Person] ADD
        FirstName       NVARCHAR(100) NULL,
        LastName        NVARCHAR(100) NULL,
        Email           NVARCHAR(200) NULL,
        CostCenter      NVARCHAR(40)  NULL,
        DepartmentShort NVARCHAR(100) NULL,
        DepartmentName  NVARCHAR(400) NULL,
        Position        NVARCHAR(200) NULL,
        Division        NVARCHAR(100) NULL,
        IsManager       NVARCHAR(10)  NULL,
        Pernr           NVARCHAR(20)  NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 15)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (15, N'015_osoby_z_hr.sql', '0.24.0');
GO
