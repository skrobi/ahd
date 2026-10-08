using Dapper;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Tests.TestSupport;

/// <summary>
/// PZLPROD do testów: na bazie testowej tabele WBS i WBS_DIC w układzie LOG.WBS / LOG.WBS_DIC (docs/zrodla-danych.md,
/// rozdz. 5) – typy jak w SAP (PSPNR CHAR(8) z zerami, STUFE liczba, znaczniki CHAR(1)). Tabele w schemacie testów
/// (FINOP) z losową sygnaturą (P…_WBS) – na bazie TEST konto nie ma prawa zakładania schematów. Usuwane po teście.
/// </summary>
public sealed class TestPzlProd : IDisposable
{
    public TestPzlProd(IEnumerable<P1sElement> elements)
    {
        Sql = new SqlDatabase(new SqlSettings("", "", TestDatabase.Schema, $"P{Guid.NewGuid():N}"[..9] + "_", ConnectionString: TestDatabase.ConnectionString!), "test");
        var (wbs, dic) = (Sql.Table("WBS"), Sql.Table("WBS_DIC"));
        using var connection = Sql.Open();
        connection.Execute(
            $"""
            CREATE TABLE {wbs} (
                PSPNR CHAR(8) NOT NULL, PARENT CHAR(8) NULL, STUFE TINYINT NULL, WBS_ELEMENT NVARCHAR(24) NULL, PROJORG NVARCHAR(24) NULL,
                PROJECT NVARCHAR(24) NULL, PROJNAME NVARCHAR(40) NULL, LTXA1 NVARCHAR(40) NULL, Z_OPIS NVARCHAR(100) NULL, PRCTR NVARCHAR(10) NULL,
                Z_KAT_ZBIORCZA NVARCHAR(60) NULL, Z_KATEGORIA NVARCHAR(60) NULL, Z_ACTIVE CHAR(1) NULL, LOEKZ CHAR(1) NULL,
                ERDAT DATE NULL, AEDAT DATE NULL);
            CREATE TABLE {dic} (
                Z_PROJECT NVARCHAR(24) NOT NULL, Z_GRP NVARCHAR(10) NOT NULL, Z_OPIS NVARCHAR(100) NULL, Z_KATEGORIA NVARCHAR(60) NULL,
                Z_KAT_ZBIORCZA NVARCHAR(60) NULL, Z_INFO NVARCHAR(200) NULL, ERDAT DATE NULL, Z_USER NVARCHAR(12) NULL);
            """);
        connection.Execute(
            $"""
            INSERT INTO {wbs} (PSPNR, PARENT, STUFE, WBS_ELEMENT, PROJORG, PROJECT, PROJNAME, LTXA1, Z_OPIS, PRCTR, Z_KAT_ZBIORCZA, Z_KATEGORIA, Z_ACTIVE, LOEKZ)
            VALUES (@Pspnr, NULLIF(@Parent, ''), @Level, @WbsElement, @ProjOrg, @Project, @ProjectName, @Name, @GroupDescription, @ProfitCenter,
                    NULLIF(@CategoryGroup, ''), NULLIF(@Category, ''), NULLIF(@ActiveFlag, ''), NULLIF(@DeletionFlag, ''))
            """,
            elements);
        connection.Execute(
            $"""
            INSERT INTO {dic} (Z_PROJECT, Z_GRP, Z_OPIS, Z_KATEGORIA, Z_KAT_ZBIORCZA, Z_INFO)
            VALUES ('AC-I39', 'PROJECT', N'Rozwój', 'Development', 'Internal Work', NULL)
            """);
    }

    public SqlDatabase Sql { get; }

    public void Dispose()
    {
        using var connection = Sql.Open();
        connection.Execute($"DROP TABLE {Sql.Table("WBS")}; DROP TABLE {Sql.Table("WBS_DIC")};");
    }
}
