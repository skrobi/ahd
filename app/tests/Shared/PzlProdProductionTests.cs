using Dapper;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>
/// Odczyt danych produkcyjnych z PZLPROD (SqlPzlProdSource.Production): tabele w układzie kolumn LOG.vAHDD,
/// LOG.vAHDD_PL_CPI i LOG.vAPD (raport produkcyjny S70MR) – godziny z produktywnością IPT i DJK, materiały dostarczone.
/// </summary>
public sealed class PzlProdProductionTests
{
    [SqlFact]
    public void Production_hours_use_ipt_productivity_and_djk_and_materials_count_delivered_value()
    {
        using var database = new TestDatabase();
        var prod = new SqlDatabase(new SqlSettings("", "", TestDatabase.Schema, $"P{Guid.NewGuid():N}"[..9] + "_", ConnectionString: TestDatabase.ConnectionString!), "test");
        var (ahdd, cpi, apd) = (prod.Table("vAHDD"), prod.Table("vAHDD_PL_CPI"), prod.Table("vAPD"));
        using (var connection = prod.Open())
        {
            connection.Execute(
                $"""
                CREATE TABLE {ahdd} (PSPNR CHAR(8), IPT NVARCHAR(10), CATS DECIMAL(18,2), TECH DECIMAL(18,2), TECH_PON DECIMAL(18,2));
                CREATE TABLE {cpi} (IPT NVARCHAR(10), CzTechPon DECIMAL(18,2), CzRzecz DECIMAL(18,2), DataZakonczeniaOperacji DATE);
                CREATE TABLE {apd} (PSPNR CHAR(8), STATUS NVARCHAR(10), NETWR_USD DECIMAL(18,2), Z_CLO DECIMAL(9,4));
                INSERT INTO {ahdd} VALUES ('00000101', 'W51', 12, 100, 40), ('00000101', 'X10', 3, 10, 10), ('00000102', 'W30', 1, 20, 0), ('00000999', 'W51', 99, 99, 99);
                -- W51: produktywność 0,8 (rok wstecz); stara operacja pominięta; X10 – bez produktywności (1)
                INSERT INTO {cpi} VALUES ('W51', 80, 100, DATEADD(MONTH, -2, GETDATE())), ('W51', 10, 100, DATEADD(YEAR, -3, GETDATE()));
                INSERT INTO {apd} VALUES ('00000101', 'DOST', 110, 0.10), ('00000101', 'WYD', 50, NULL), ('00000101', 'ZAM', 1000, 0), ('00000102', 'ZAM', 70, 0);
                """);
        }
        try
        {
            var values = new SqlPzlProdSource(prod).Production(["00000101", "102", "abc"]).ToDictionary(v => v.Pspnr);

            Assert.Equal(["101", "102"], values.Keys.Order());   // tylko zakres projektu (999 poza)
            var wp = values["101"];
            // BAC: W51 100/0,8 + 15% DJK z 100 = 140; X10 10/1 = 10 → 150. EV: 40/0,8 + 15% z 40 = 56; X10 10 → 66. AC: 12 + 3.
            Assert.Equal((15m, 150m, 66m), (wp.AcHours, wp.BacHours, wp.EvHours));
            Assert.Equal(150m, wp.ActualMaterial);              // DOST 110/1,10 + WYD 50 (zamówione ZAM – nie)
            Assert.Equal((22m, 0m, 0m), (values["102"].BacHours, values["102"].EvHours, values["102"].ActualMaterial));   // W30: 20 + 10% DJK
            Assert.Empty(new SqlPzlProdSource(prod).Production([]));
        }
        finally
        {
            using var connection = prod.Open();
            connection.Execute($"DROP TABLE {ahdd}; DROP TABLE {cpi}; DROP TABLE {apd};");
        }
    }
}
