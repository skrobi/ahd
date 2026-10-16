using Dapper;
using ClosedXML.Excel;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Process;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>
/// Koszt projektu CAS po narzutach (CasCosts) – te same reguły co raport kosztów CES: TOTAL COST jak w arkuszu finansów,
/// przeliczenie kursem CAS w okresie kosztu, brak kursu – błąd.
/// </summary>
public sealed partial class CasCostReportTests
{
    private static DictRow Fx(string currency, string from, string? to, string? cas) =>
        new(null, null, new Dictionary<string, string?> { ["Waluta"] = currency, ["Od"] = from, ["Do"] = to, ["Kurs"] = null, [GlobalDictionaries.CasFxColumn] = cas });

    [Fact]
    public void Formula_value_follows_operator_precedence_and_parentheses()
    {
        IReadOnlyList<CasFormula.Token> tokens = CasFormula.Parse("base + p:1 * r:2")!;
        Assert.Equal(10 + 3 * 0.5m, CasFormula.Evaluate(tokens, 10, _ => 3, _ => 0.5m));
        Assert.Equal((10 + 3) * 0.5m, CasFormula.Evaluate(CasFormula.Parse("( base + p:1 ) * r:2")!, 10, _ => 3, _ => 0.5m));
        Assert.Equal(10 - 4 / 2m, CasFormula.Evaluate(CasFormula.Parse("base - p:1 / r:2")!, 10, _ => 4, _ => 2));
        Assert.Equal(0m, CasFormula.Evaluate(CasFormula.Parse("base / r:2")!, 10, _ => 0, _ => 0));   // jak #DZIEL/0! – 0
    }

    /// <summary>
    /// Proces projektu: ścieżka wspólna – pole kwoty z „Parametrów projektu”, koszt bez przekształceń; wariant CAS – zawsze PLN,
    /// koszt po narzutach CAS kalkulatorem CasCosts (tworzonym przy pierwszym użyciu), opis ACWP i raport kosztów CES.
    /// </summary>
    [Fact]
    public void Project_process_default_keeps_actuals_and_cas_variant_burdens_pln_costs()
    {
        var common = ProjectProcess.Default;
        CostRow[] rows = [new("WBS1", "0092210550", 2026, 1, 100m)];
        Assert.Equal(("ValueRepCur", false, (string?)null, false), (common.AmountField("ValueRepCur"), common.TransformsCosts, common.Currency, common.HasBurdenReport));
        Assert.Equal([100m], common.Costs(rows, (2026, 1)).Costs);
        Assert.Null(common.AcwpDescription("USD"));

        long fringe = PoolId("MFG Fringe");
        var groups = FinanceGroups.Select(g => g with { Formulas = g.Formulas.Where(f => f.Key == fringe).ToDictionary(f => f.Key, f => f.Value) }).ToList();
        var created = 0;
        var cas = new CasProcess(() =>
        {
            created++;
            return new CasCosts(FinancePools, groups, [new(fringe, new DateOnly(2026, 1, 1), null, 0.5m)], FinanceMapping, [Fx("USD", "2026-01-01", null, "4")], "USD");
        });
        Assert.Equal(("ValueObjCrcy", true, true), (cas.AmountField("ValueRepCur"), cas.TransformsCosts, cas.HasBurdenReport));
        Assert.Contains("w USD kursem CAS", cas.AcwpDescription("USD"));
        Assert.Equal(0, created);                                   // opis i flagi – bez słowników
        var result = cas.Costs([new("WBS1", "0092210550", 2026, 1, 400m)], (2026, 1));
        Assert.Equal([150m], result.Costs);                         // 100 USD + Fringe 50%
        Assert.Equal(((string?)null, "USD", 1), (result.Error, cas.Currency, created));
        Assert.Contains("brak kursu CAS", cas.Costs([new("WBS1", "0092210550", 2025, 12, 400m)], (2026, 1)).Error);
    }

    /// <summary>TOTAL COST każdego wiersza i roku = wartość w arkuszu finansów (kolumny TOTAL COST 2026 i 2025).</summary>
    [Fact]
    public void Total_cost_is_the_same_as_in_finance_sheet()
    {
        using var finance = Finance();
        var sheet = finance.Worksheet("Arkusz2");
        var (_, rows, rates) = Input(sheet);
        var costs = new CasCosts(FinancePools, FinanceGroups, rates, FinanceMapping, [], "PLN");
        int TotalColumn(string year) => Enumerable.Range(8, SheetLastColumn - 7).Single(c => sheet.Cell(SheetHeaderRow, c).GetString() == $"TOTAL COST {year}");

        foreach (var (year, cost) in new[] { (2026, 7), (2025, 8) })
        {
            var column = TotalColumn(year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (var r = 0; r < rows.Count; r++)
            {
                var expected = sheet.Cell(SheetFirstRow + r, column).CachedValue;
                var actual = costs.TotalCost((string?)rows[r][3], rows[r][6 + cost - 6] as decimal? ?? 0, year, (2026, 7));
                Assert.True(Math.Abs((decimal)expected.GetNumber() - actual) < 0.000001m, $"{year}, wiersz {SheetFirstRow + r}: arkusz {expected}, wyliczony {actual}");
            }
        }
        Assert.Empty(costs.Notes);
    }

    /// <summary>Projekt w USD: koszt PLN każdego okresu przeliczony kursem CAS tego okresu, potem narzuty; brak kursu – błąd z okresami.</summary>
    [Fact]
    public void Usd_project_converts_pln_cost_with_cas_rate_of_cost_period_before_burdens()
    {
        long fringe = PoolId("MFG Fringe");
        List<CasBurden.Rate> rates = [new(fringe, new DateOnly(2026, 1, 1), null, 0.5m)];
        var groups = FinanceGroups.Select(g => g with { Formulas = g.Formulas.Where(f => f.Key == fringe).ToDictionary(f => f.Key, f => f.Value) }).ToList();
        DictRow[] fx = [Fx("USD", "2026-01-01", "2026-01-31", "4"), Fx("USD", "2026-02-01", null, "5")];
        var costs = new CasCosts(FinancePools, groups, rates, FinanceMapping, fx, "USD");
        CostRow[] rows =
        [
            new("WBS1", "0092210550", 2026, 1, 400m),    // 100 USD + Fringe 50%
            new("WBS1", "0092210550", 2026, 2, 500m),    // 100 USD + 50
            new("WBS1", "0092210550", 2026, 14, 1000m),  // okres specjalny – kurs grudnia (5)
        ];

        var result = costs.Calculate(rows, (2026, 12));

        Assert.Null(result.Error);
        Assert.Equal([150m, 150m, 300m], result.Costs);
        var missing = new CasCosts(FinancePools, groups, rates, FinanceMapping, [Fx("USD", "2026-02-01", "2026-02-28", "5")], "USD").Calculate(rows, (2026, 12));
        Assert.Equal(["2026-01", "2026-12"], missing.MissingFx);
        Assert.Contains("brak kursu CAS", missing.Error);
        Assert.Equal(150m, missing.Costs[1]);
        // Projekt w PLN – bez przeliczenia, kurs CAS niepotrzebny.
        Assert.Equal([600m, 750m, 1500m], new CasCosts(FinancePools, groups, rates, FinanceMapping, [], "PLN").Calculate(rows, (2026, 12)).Costs);
    }

    /// <summary>
    /// Projekt CAS na bazie: ACWP struktury = suma TOTAL COST raportu kosztów CES; waluta USD (Report currency w „Parametrach
    /// projektu”) – koszt PLN z ACTUALS przeliczony kursem CAS (nie kwota USD z SAP), bez kursu CAS – błąd ACWP i raportu.
    /// </summary>
    [SqlFact]
    public void Cas_project_acwp_is_total_cost_of_report_and_usd_uses_cas_rate_of_pln_cost()
    {
        var services = new TestServices();
        using var database = new TestDatabase(presets: true);
        var app = services.App(_folder, database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(database.Sql, services.Clock, services.User), app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        Directory.CreateDirectory(app.Config.ImportFolder);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Path.Combine(app.Config.ImportFolder, "ACTUALS_PAF_01.csv"));
        Assert.Equal(FileDecisions.Imported, Assert.Single(new ImportService(new SqlImportStore(database.Sql), app).Run().Files).Decision);
        var store = new SqlProjectStore(database.Sql, services.Clock, services.User);
        var dictionaryStore = new SqlDictionaryStore(database.Sql, services.Clock, services.User, [.. GlobalDictionaries.Tables, .. ProjectDictionaries.Tables]);
        var service = new ProjectService(store, dictionaryStore, app.Journal, new ProjectFolders(Path.Combine(_folder, "Projekty")), null);
        var tree = new PoTree();
        tree.AddElement(null, "2DI473", "PAF2", null);
        Assert.True(store.Create("PAF", "PAF", ProjectTypes.Cas, tree).Success);

        // PLN: ACWP po narzutach = TOTAL COST raportu (2026 – jedyny rok danych); ACWP Hours / Materials według rodzaju kosztu.
        var pln = service.CostsAndHoursByElement("PAF", ProjectSettings.DefaultActualsAmount);
        var path = Path.Combine(_folder, "PAF_koszty_CES.xlsx");
        Assert.Equal((4, 0), service.ExportCasCostReport(path, "PAF"));
        double TotalCost(string file)
        {
            using var report = new XLWorkbook(file);
            report.RecalculateAllFormulas();
            var ws = report.Worksheet("Koszty CES");
            var column = Header(ws, "2026", "TOTAL COST 2026");
            return Enumerable.Range(CasCostReport.FirstDataRow, 4).Sum(r => ws.Cell(r, column).CachedValue.GetNumber());
        }
        var acwp = pln.Amounts.Values.Sum();
        Assert.Equal(TotalCost(path), (double)acwp, 6);
        Assert.True(acwp > 1254.51m + 4800m + 2400m + 430m);                          // z narzutami
        Assert.True(pln.Labor["2DI473001001"] > 4800m && pln.Material["2DI473001001"] > 1254.51m);
        Assert.Equal(32m, pln.Hours["2DI473001001"]);                                  // Actual DLH bez zmian

        // USD bez kursu CAS – błąd zamiast kwot USD z SAP (bez przybliżonych kursów USD z danych startowych 046).
        using (var connection = database.Sql.Open())
            connection.Execute($"DELETE FROM {database.Sql.Table("dict.FxRate")} WHERE Currency = 'USD'");
        var context = service.Context("PAF", tree, null, P1sInputs.None);
        Assert.Equal(SaveStatus.Saved, service.SaveDictionary(ProjectDictionaries.Settings, context,
            [new DictRow(null, null, new Dictionary<string, string?> { ["Parametr"] = ProjectSettings.ActualsAmount, ["Wartość"] = "ValueRepCur", ["Opis"] = null })], [], "PAF").Status);
        Assert.Contains("brak kursu CAS", Assert.Throws<InvalidOperationException>(() => service.CostsAndHoursByElement("PAF", "ValueRepCur")).Message);
        Assert.Contains("2026-03", Assert.Throws<InvalidOperationException>(() => service.ExportCasCostReport(path, "PAF")).Message);

        // Kurs CAS 4 PLN/USD od 2026 – ACWP i raport w USD = PLN ÷ 4 (formuły liniowe względem kosztu).
        var dictionaries = new DictionaryService(dictionaryStore, app.Journal);
        var fx = GlobalDictionaries.Get(GlobalDictionaries.FxRates);
        Assert.Equal(SaveStatus.Saved, dictionaries.Save(fx, [Fx("USD", "2026-01-01", null, "4")], [], confirmWarnings: true).Status);
        var usd = service.CostsAndHoursByElement("PAF", "ValueRepCur");
        Assert.Equal(Math.Round(acwp / 4, 6), Math.Round(usd.Amounts.Values.Sum(), 6));
        var usdPath = Path.Combine(_folder, "PAF_koszty_CES_USD.xlsx");
        Assert.Equal((4, 1), service.ExportCasCostReport(usdPath, "PAF"));                      // uwaga o przeliczeniu kursem CAS
        Assert.Equal(TotalCost(path) / 4, TotalCost(usdPath), 6);
    }
}
