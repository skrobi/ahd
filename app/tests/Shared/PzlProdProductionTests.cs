using Dapper;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>
/// Odczyt danych produkcyjnych z PZLPROD (SqlPzlProdSource.Production – sql/pzlprod/produkcja.sql): tabele w układzie
/// kolumn LOG.vAHDD, LOG.vAHDD_PL_CPI i LOG.vAPD – godziny z produktywnością IPT i DJK, daty rzeczywiste, elementy
/// wirtualne (Paint), materiały dostarczone, parametry ze słowników.
/// </summary>
public sealed class PzlProdProductionTests : IDisposable
{
    private readonly SqlDatabase _prod = null!;
    private readonly TestDatabase? _database;

    public PzlProdProductionTests()
    {
        if (TestDatabase.ConnectionString is null)
            return;
        _database = new TestDatabase();
        _prod = new SqlDatabase(new SqlSettings("", "", TestDatabase.Schema, $"P{Guid.NewGuid():N}"[..9] + "_", ConnectionString: TestDatabase.ConnectionString!), "test");
        using var connection = _prod.Open();
        connection.Execute(
            $"""
            CREATE TABLE {_prod.Table("vAHDD")} (PSPNR CHAR(8), SWBS NVARCHAR(40), CPLGR NVARCHAR(10), ARBPL NVARCHAR(10), IPT NVARCHAR(10), STAT NVARCHAR(10),
                GSTRI DATE, LTRMI DATE, DATA_REAL DATE, CATS DECIMAL(18,2), TECH DECIMAL(18,2), TECH_PON DECIMAL(18,2), TECH_DO_PON DECIMAL(18,2));
            CREATE TABLE {_prod.Table("vAHDD_PL_CPI")} (IPT NVARCHAR(10), CzTechPon DECIMAL(18,2), CzRzecz DECIMAL(18,2), DataZakonczeniaOperacji DATE);
            CREATE TABLE {_prod.Table("vAPD")} (PSPNR CHAR(8), STATUS NVARCHAR(10), NETWR_USD DECIMAL(18,2), Z_CLO DECIMAL(9,4), MATNR NVARCHAR(40), EINDT_MAX DATE);
            INSERT INTO {_prod.Table("vAHDD")} VALUES
                ('00000101', 'Hangar', 'W51', '3622', 'W51', 'DONE', '2026-01-10', '2026-02-20', '2026-02-15', 12, 100, 40, 60),
                ('00000101', 'Hangar', 'X10', 'K001', 'X10', 'DONE', '2026-01-05', '2026-02-25', '2026-02-24', 3, 10, 10, 0),
                ('00000101', 'Hangar', 'W20', '2010', 'W20', 'OPEN', '2026-03-01', NULL, '2026-03-05', 4, 50, 20, 30),   -- Paint (Hangar W20)
                ('00000102', 'Cabin', 'W30', '3010', 'W30', 'OPEN', '2026-02-01', NULL, NULL, 1, 20, 0, 20),
                ('00000999', 'Hangar', 'W51', '3622', 'W51', 'DONE', '2025-01-01', '2025-02-01', NULL, 99, 99, 99, 0);
            -- W51: produktywność 0,8 (2 mies. temu); operacja sprzed 3 lat – poza oknem; pozostałe IPT – bez produktywności (1)
            INSERT INTO {_prod.Table("vAHDD_PL_CPI")} VALUES ('W51', 80, 100, DATEADD(MONTH, -2, GETDATE())), ('W51', 10, 100, DATEADD(YEAR, -3, GETDATE()));
            INSERT INTO {_prod.Table("vAPD")} VALUES ('00000101', 'DOST', 110, 0.10, 'PN-1', '2026-01-15'), ('00000101', 'WYD', 50, NULL, 'PN-2', '2026-02-01'),
                ('00000101', 'ZAM', 1000, 0, 'PN-3', '2027-05-01'), ('00000101', 'ZAM', 220, 0.10, 'PN-4', '2026-11-30'), ('00000102', 'ZAM', 70, 0, 'PN-5', NULL);
            """);
    }

    public void Dispose()
    {
        if (_database is null)
            return;
        using (var connection = _prod.Open())
            connection.Execute($"DROP TABLE {_prod.Table("vAHDD")}; DROP TABLE {_prod.Table("vAHDD_PL_CPI")}; DROP TABLE {_prod.Table("vAPD")};");
        _database.Dispose();
    }

    private static readonly VirtualRule Paint = new("AC-CAB.6.38.07.PAINT.W20", "101", null, "W20", null);

    [SqlFact]
    public void Hours_use_ipt_productivity_and_djk_dates_and_delivered_materials()
    {
        var values = new SqlPzlProdSource(_prod).Production(["00000101", "102", "abc"], [], ProductionParameters.Default).ToDictionary(v => v.Pspnr);

        Assert.Equal(["101", "102"], values.Keys.Order());   // tylko zakres projektu (999 poza)
        var wp = values["101"];
        // BAC: W51 100/0,8 + 15% z 100 = 140; X10 10; W20 50 + 10% = 55 → 205. EV: 40/0,8 + 6 = 56; 10; 20 + 2 = 22 → 88. AC: 12 + 3 + 4.
        Assert.Equal((19m, 205m, 88m), (wp.AcHours, wp.BacHours, wp.EvHours));
        Assert.Equal(150m, wp.ActualMaterial);                                       // DOST 110/1,10 + WYD 50 (ZAM – nie)
        // Wartość wszystkich części: 100 + 50 + 1000 + 220/1,10 = 1350; fee: 10 + 20; części 4, dostarczone 2; najbliższa dostawa niedostarczonych – 2026.
        Assert.Equal((1350m, 30m, 4, 2, 2026), (wp.MaterialValue, wp.MaterialFee, wp.Parts, wp.PartsDelivered, wp.NextDeliveryYear));
        // ETC: W51 60/0,8 + 15% z 60 = 84; X10 0; W20 30 + 10% = 33 → 117.
        Assert.Equal(117m, wp.EtcHours);
        Assert.Equal((new DateOnly(2026, 1, 5), (DateOnly?)null), (wp.ActualStart, wp.ActualFinish));   // W20 otwarta – bez końca
        Assert.Equal((22m, 0m, 0m), (values["102"].BacHours, values["102"].EvHours, values["102"].ActualMaterial));   // W30: 20 + 10% DJK
        Assert.Empty(new SqlPzlProdSource(_prod).Production([], [Paint], ProductionParameters.Default));
    }

    [SqlFact]
    public void Virtual_element_takes_operations_of_its_rule_and_they_are_subtracted_from_parent()
    {
        var values = new SqlPzlProdSource(_prod).Production(["101"], [Paint], ProductionParameters.Default);

        var paint = Assert.Single(values, v => v.VirtualCode == Paint.Code);
        Assert.Equal((4m, 55m, 22m, (decimal?)null), (paint.AcHours, paint.BacHours, paint.EvHours, paint.ActualMaterial));   // DJK W20 idzie z Paint; materiały – nadrzędny
        Assert.Equal((new DateOnly(2026, 3, 5), (DateOnly?)null), (paint.ActualStart, paint.ActualFinish));                    // DATA_REAL; otwarta
        var parent = Assert.Single(values, v => v.VirtualCode is null);
        Assert.Equal((15m, 150m, 66m, 150m), (parent.AcHours, parent.BacHours, parent.EvHours, parent.ActualMaterial));          // bez operacji Paint
        Assert.Equal((new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 25)), (parent.ActualStart, parent.ActualFinish));        // wszystkie DONE – koniec LTRMI
    }

    [SqlFact]
    public void Split_cplgr_is_a_prefix_and_the_longest_rule_wins()
    {
        // MACHINING.W5 – W51 (i inne W5x); FORMING.W51 – dłuższy prefiks wygrywa dla W51.
        var values = new SqlPzlProdSource(_prod).Production(["101"],
            [new("E.MACHINING.W", "101", null, "W", null), new("E.ASSEMBLY.W51", "101", null, "W51", null)], ProductionParameters.Default);

        Assert.Equal(140m, values.Single(v => v.VirtualCode == "E.ASSEMBLY.W51").BacHours);   // W51: 100/0,8 + 15%
        Assert.Equal(55m, values.Single(v => v.VirtualCode == "E.MACHINING.W").BacHours);     // W20: 50 + 10%
        Assert.Equal(10m, values.Single(v => v.VirtualCode is null).BacHours);                // reszta – X10
    }

    [SqlFact]
    public void Parameters_from_dictionaries_change_djk_productivity_window_and_materials()
    {
        var parameters = new ProductionParameters([("W5", 0.5m)], 48, ["ZAM"], DivideByZClo: false);
        var values = new SqlPzlProdSource(_prod).Production(["101"], [], parameters).Single();

        // W51: produktywność z 4 lat (80+10)/(100+100) = 0,45 → 100/0,45 + 50% z 100; X10 10; W20 50 (bez DJK – brak W2).
        Assert.Equal(Math.Round(100m / 0.45m + 50m + 10m + 50m, 4), Math.Round(values.BacHours!.Value, 4));
        Assert.Equal(1220m, values.ActualMaterial);   // status ZAM (1000 + 220), bez dzielenia przez Z_CLO
    }
}
