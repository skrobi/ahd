/* PZL-EV – migracja 010: stawki wydziałów według MPK (docs/slowniki.md, rozdz. 2, 5.4).
   Kluczem stawki jest MPK (miejsce powstawania kosztów) + rok; Department to opis MPK (opcjonalny); Overhead jest
   opcjonalny. DICT_DepartmentRate dostaje kolumnę CostCenter (MPK), Department i Overhead dopuszczają brak wartości,
   unikalny klucz bieżących wersji: (Project, CostCenter, Year). Istniejące wiersze: MPK = dotychczasowy Department
   (był kluczem) – do poprawy w słowniku, jeśli Department był opisem.

   Uruchomienie: aplikacja (Diagnostyka → Migracja albo pytanie przy starcie) – w jednej transakcji; ręcznie:
       sqlcmd -S pzltestdb.intl.lmco.com -d PZLTEST -E -f 65001 -v Schema=FINOP Prefix=PZLEV_ -i 010_stawki_mpk.sql
   Skrypt jest idempotentny. */

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'[$(Schema)].[$(Prefix)DICT_DepartmentRate]', N'CostCenter') IS NULL
    ALTER TABLE [$(Schema)].[$(Prefix)DICT_DepartmentRate] ADD CostCenter NVARCHAR(40) NULL;
GO

DECLARE @filled INT;
UPDATE [$(Schema)].[$(Prefix)DICT_DepartmentRate] SET CostCenter = LEFT(Department, 40) WHERE CostCenter IS NULL;
SET @filled = @@ROWCOUNT;
IF @filled > 0
INSERT INTO [$(Schema)].[$(Prefix)META_Journal] (At, UserName, Area, Message)
VALUES (SYSDATETIMEOFFSET(), ORIGINAL_LOGIN(), N'Migracje',
        N'Migracja 010 – stawki wydziałów według MPK: wierszy z MPK przepisanym z Department: ' + CAST(@filled AS NVARCHAR(20)));
GO

ALTER TABLE [$(Schema)].[$(Prefix)DICT_DepartmentRate] ALTER COLUMN CostCenter NVARCHAR(40) NOT NULL;
ALTER TABLE [$(Schema)].[$(Prefix)DICT_DepartmentRate] ALTER COLUMN Department NVARCHAR(100) NULL;
ALTER TABLE [$(Schema)].[$(Prefix)DICT_DepartmentRate] ALTER COLUMN Overhead DECIMAL(28,8) NULL;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_DepartmentRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_DepartmentRate]'))
   AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                   JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE i.name = N'UX_$(Prefix)DICT_DepartmentRate_Key' AND i.object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_DepartmentRate]')
                     AND c.name = N'CostCenter')
    DROP INDEX [UX_$(Prefix)DICT_DepartmentRate_Key] ON [$(Schema)].[$(Prefix)DICT_DepartmentRate];
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_$(Prefix)DICT_DepartmentRate_Key' AND object_id = OBJECT_ID(N'[$(Schema)].[$(Prefix)DICT_DepartmentRate]'))
CREATE UNIQUE INDEX [UX_$(Prefix)DICT_DepartmentRate_Key] ON [$(Schema)].[$(Prefix)DICT_DepartmentRate] (Project, CostCenter, Year) WHERE SupersededAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM [$(Schema)].[$(Prefix)META_SchemaVersion] WHERE Version = 10)
INSERT INTO [$(Schema)].[$(Prefix)META_SchemaVersion] (Version, Script, MinAppVersion)
VALUES (10, N'010_stawki_mpk.sql', '0.20.0');
GO
