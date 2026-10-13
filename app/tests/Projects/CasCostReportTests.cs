using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Dapper;
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
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Files;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>
/// Raport kosztów CES (CasCostReport): formuły narzutów CAS jak w arkuszu finansów (testdata/Projekty/Narzuty_CAS_arkusz.xlsx,
/// arkusz „Arkusz2”: wiersz 3 – kolumny, 4 – wskaźniki, 5 – nagłówek, dane od 6, opis od kolumny B), wskaźniki od – do,
/// wiele lat, braki mapowania i wskaźników.
/// </summary>
public sealed partial class CasCostReportTests : IDisposable
{
    /// <summary>Pule arkusza finansów w kolejności kolumn (dane startowe – migracja 032): 12 kosztu, 5 FCCOM.</summary>
    public static IReadOnlyList<string> FinancePoolNames { get; } =
    [
        "MFG Fringe", "MFG OH", "ENG Fringe", "ENG OH", "MTL Fringe", "Material Handling (MTL)", "Miscellaneous Material Pool (MMP)", "General Services (GS)",
        "GS Fringe", "MSV Fringe", "General & Administrative (GA)", "Corporate Flowdowns (FD)", "FCCOM MFG", "FCCOM ENG", "FCCOM MTL", "FCCOM GS", "FCCOM G&A",
    ];

    public static IReadOnlyList<CasBurden.Pool> FinancePools { get; } =
        FinancePoolNames.Select((p, i) => new CasBurden.Pool(p, (i + 1) * 10, p.StartsWith("FCCOM"))).ToList();

    /// <summary>Formuły grup odczytane z arkusza finansów (dane startowe – migracja 032): grupa → pula → podstawa.</summary>
    public static IReadOnlyDictionary<string, Dictionary<string, string>> FinanceFormulas { get; } = new Dictionary<string, Dictionary<string, string>>
    {
        ["GS labor"] = new Dictionary<string, string> { ["General Services (GS)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe", ["GS Fringe"] = "Base cost elements", ["General & Administrative (GA)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["Corporate Flowdowns (FD)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["FCCOM GS"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe", ["FCCOM G&A"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe" },
        ["Direct Materials"] = new Dictionary<string, string> { ["Material Handling (MTL)"] = "Base cost elements", ["Miscellaneous Material Pool (MMP)"] = "Base cost elements", ["General Services (GS)"] = "Material Handling (MTL)", ["General & Administrative (GA)"] = "Material Handling (MTL) + General Services (GS)", ["Corporate Flowdowns (FD)"] = "Material Handling (MTL) + General Services (GS)", ["FCCOM MTL"] = "Base cost elements", ["FCCOM GS"] = "Material Handling (MTL)", ["FCCOM G&A"] = "Material Handling (MTL) + General Services (GS)" },
        ["Direct Services"] = new Dictionary<string, string> { ["Material Handling (MTL)"] = "Base cost elements", ["General Services (GS)"] = "Material Handling (MTL)", ["General & Administrative (GA)"] = "Material Handling (MTL) + General Services (GS)", ["Corporate Flowdowns (FD)"] = "Material Handling (MTL) + General Services (GS)", ["FCCOM MTL"] = "Base cost elements", ["FCCOM GS"] = "Material Handling (MTL)", ["FCCOM G&A"] = "Material Handling (MTL) + General Services (GS)" },
        ["Travel"] = new Dictionary<string, string> { ["General Services (GS)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe", ["General & Administrative (GA)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["Corporate Flowdowns (FD)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["FCCOM G&A"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe" },
        ["ODC"] = new Dictionary<string, string> {  },
        ["MFG labor"] = new Dictionary<string, string> { ["MFG Fringe"] = "Base cost elements", ["MFG OH"] = "Base cost elements + MFG Fringe", ["General Services (GS)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe", ["General & Administrative (GA)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["Corporate Flowdowns (FD)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["FCCOM MFG"] = "Base cost elements + MFG Fringe", ["FCCOM GS"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe", ["FCCOM G&A"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe" },
        ["ENG labor"] = new Dictionary<string, string> { ["ENG Fringe"] = "Base cost elements", ["ENG OH"] = "Base cost elements + ENG Fringe", ["General Services (GS)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe", ["General & Administrative (GA)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["Corporate Flowdowns (FD)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["FCCOM ENG"] = "Base cost elements + ENG Fringe", ["FCCOM GS"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe", ["FCCOM G&A"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe" },
        ["ENG LL labor"] = new Dictionary<string, string> { ["ENG OH"] = "Base cost elements + ENG Fringe", ["General Services (GS)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe", ["General & Administrative (GA)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["Corporate Flowdowns (FD)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["FCCOM ENG"] = "Base cost elements + ENG Fringe", ["FCCOM GS"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe", ["FCCOM G&A"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe" },
        ["MTL labor"] = new Dictionary<string, string> { ["MTL Fringe"] = "Base cost elements", ["General Services (GS)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe", ["General & Administrative (GA)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe + MTL Fringe", ["Corporate Flowdowns (FD)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe + MTL Fringe", ["FCCOM GS"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe", ["FCCOM G&A"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe + MTL Fringe" },
        ["MSV labor"] = new Dictionary<string, string> { ["General Services (GS)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe + MTL Fringe", ["MSV Fringe"] = "Base cost elements", ["General & Administrative (GA)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["Corporate Flowdowns (FD)"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe", ["FCCOM GS"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + GS Fringe + MSV Fringe", ["FCCOM G&A"] = "Base cost elements + MFG Fringe + MFG OH + ENG Fringe + ENG OH + Material Handling (MTL) + General Services (GS) + GS Fringe + MSV Fringe" },
    };

    public static IReadOnlyList<CasBurden.Group> FinanceGroups { get; } = FinanceFormulas
        .Select(g => new CasBurden.Group(g.Key, null, g.Value.ToDictionary(f => f.Key, f => CasBurden.Parse(f.Value))))
        .ToList();

    /// <summary>Grupa narzutowa elementów kosztowych arkusza finansów (dane startowe – migracja 030).</summary>
    public static IReadOnlyDictionary<string, string?> FinanceMapping { get; } = new Dictionary<string, string?>
    {
        ["0096606550"] = "GS labor", ["0051105550"] = "Direct Materials", ["0051110550"] = "Direct Services", ["0057120550"] = "Travel",
        ["0057510550"] = "ODC", ["0057511550"] = "ODC", ["0057712550"] = "ODC", ["0057714550"] = "ODC",
        ["0092210550"] = "MFG labor", ["0092211550"] = "MFG labor", ["0092212550"] = "MFG labor", ["0092213550"] = "MFG labor",
        ["0092214550"] = "MFG labor", ["0092215550"] = "MFG labor", ["0092216550"] = "MFG labor", ["0092217550"] = "MFG labor",
        ["0092218550"] = "MFG labor", ["0092219550"] = "MFG labor", ["0092223550"] = "MFG labor", ["9221X550"] = "MFG labor", ["9222X550"] = "MFG labor",
        ["0094410550"] = "ENG labor", ["0094414550"] = "ENG labor", ["0094412550"] = "ENG labor", ["0094490550"] = "ENG LL labor",
        ["0096610550"] = "MTL labor", ["9441X550"] = "ENG labor", ["9229X550"] = "MSV labor", ["9660R550"] = "MSV labor",
    };

    private const int SheetHeaderRow = 5, SheetRateRow = 4, SheetFirstRow = 6, SheetLastColumn = 67;   // BO – TOTAL FCCOM 2025

    private static readonly string[] Description =
        ["Grouping", "Project definition", "Project definition description", "Cost Element", "Cost Elem. Descr.", "Cost grouping"];

    private readonly string _folder = Directory.CreateTempSubdirectory("pzlev-cas-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static XLWorkbook Finance() => new(TestServices.TestData("Projekty", "Narzuty_CAS_arkusz.xlsx"));

    /// <summary>Raport kosztów (REP_ProjectCosts) z kosztów bazowych arkusza finansów: opis B–G, koszt H (07/2026), I (2026), J (2025).</summary>
    private static (List<string> Columns, List<IReadOnlyList<object?>> Rows, List<CasCostReport.Rate> Rates) Input(IXLWorksheet sheet)
    {
        var columns = Description.Concat(["Period 07/2026", "2026", "2025"]).ToList();
        var rows = new List<IReadOnlyList<object?>>();
        for (var r = SheetFirstRow; !sheet.Cell(r, 5).IsEmpty(); r++)
            rows.Add([.. Enumerable.Range(2, 6).Select(c => (object?)sheet.Cell(r, c).GetFormattedString()),
                .. Enumerable.Range(8, 3).Select(c => sheet.Cell(r, c).IsEmpty() ? null : (object?)(decimal)sheet.Cell(r, c).GetDouble())]);
        // Wskaźniki roku 2026 (kolumny AD–AT) i 2025 (AW–BM) – od – do = rok.
        var rates = new List<CasCostReport.Rate>();
        for (var p = 0; p < FinancePoolNames.Count; p++)
        {
            rates.Add(new CasCostReport.Rate(FinancePoolNames[p], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), (decimal)sheet.Cell(SheetRateRow, 30 + p).GetDouble()));
            rates.Add(new CasCostReport.Rate(FinancePoolNames[p], new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), (decimal)sheet.Cell(SheetRateRow, 49 + p).GetDouble()));
        }
        return (columns, rows, rates);
    }

    private string Write(CasCostReport.Result result)
    {
        var path = Path.Combine(_folder, "koszty_CES.xlsx");
        ExcelTableWriter.WriteReport(path, [result.Report, result.Rates]);
        return path;
    }

    /// <summary>Formuła bez położenia wiersza: komórka wiersza danych – „K#”, wskaźnik – „K$R”.</summary>
    private static string Normalize(string formula, int row, int rateRow) =>
        CellReference().Replace(formula.TrimStart('='), m =>
            m.Groups[3].Value == "$" && m.Groups[4].Value == rateRow.ToString() ? $"{m.Groups[2].Value}$R"
            : m.Groups[4].Value == row.ToString() ? $"{m.Groups[2].Value}#" : m.Value);

    /// <summary>
    /// Błąd arkusza finansów: Procurement 2025, MTL Fringe (BA29) – „=J29*$BL$4”, wskaźnik FCCOM GS zamiast MTL Fringe
    /// (koszt bazowy 0 – wartość ta sama). Raport liczy wskaźnikiem MTL Fringe jak w okresie i w 2026.
    /// </summary>
    private static bool SheetError(int row, int column) => (row, column) == (29, 53);

    [GeneratedRegex(@"(\$?)([A-Z]{1,3})(\$?)(\d+)")]
    private static partial Regex CellReference();

    [Fact]
    public void Formulas_and_headers_are_the_same_as_in_finance_sheet()
    {
        using var finance = Finance();
        var sheet = finance.Worksheet("Arkusz2");
        var (columns, rows, rates) = Input(sheet);

        var result = CasCostReport.Build(columns, rows, FinanceMapping, FinancePools, FinanceGroups, rates);

        Assert.Empty(result.Notes);
        using var report = new XLWorkbook(Write(result));
        var ws = report.Worksheet("Koszty CES");
        Assert.Equal([.. Description, GlobalDictionaries.BurdenGroupColumn], Enumerable.Range(1, 7).Select(c => ws.Cell(CasCostReport.HeaderRow, c).GetString()));
        Assert.Equal(29, rows.Count);
        // Kolumny od H (koszt bazowy) – te same w raporcie i w arkuszu finansów (arkusz ma opis od B, raport – od A i grupę w G).
        for (var c = 8; c <= SheetLastColumn; c++)
        {
            Assert.Equal(sheet.Cell(SheetHeaderRow, c).GetFormattedString(), ws.Cell(CasCostReport.HeaderRow, c).GetString());
            Assert.Equal(sheet.Cell(SheetHeaderRow - 2, c).GetFormattedString(), ws.Cell(CasCostReport.BlockRow, c).GetFormattedString());
            for (var r = 0; r < rows.Count; r++)
            {
                if (SheetError(SheetFirstRow + r, c))
                    continue;
                var expected = sheet.Cell(SheetFirstRow + r, c).HasFormula ? Normalize(sheet.Cell(SheetFirstRow + r, c).FormulaA1, SheetFirstRow + r, SheetRateRow) : "";
                var actual = ws.Cell(CasCostReport.FirstDataRow + r, c).HasFormula
                    ? Normalize(ws.Cell(CasCostReport.FirstDataRow + r, c).FormulaA1, CasCostReport.FirstDataRow + r, CasCostReport.RateRow) : "";
                Assert.True(expected == actual, $"{CasCostReport.Letter(c)}: {sheet.Cell(SheetFirstRow + r, 5).GetString()} – arkusz „{expected}”, raport „{actual}”");
            }
        }
        Assert.Equal("Base cost elements", ws.Cell(CasCostReport.RateRow, 8).GetString());
        Assert.Equal("MFG labor", ws.Cell(CasCostReport.FirstDataRow + 8, 7).GetString());   // 0092210550 PZL Quality Control
    }

    [Fact]
    public void Values_are_the_same_as_in_finance_sheet()
    {
        using var finance = Finance();
        var sheet = finance.Worksheet("Arkusz2");
        var (columns, rows, rates) = Input(sheet);

        using var report = new XLWorkbook(Write(CasCostReport.Build(columns, rows, FinanceMapping, FinancePools, FinanceGroups, rates)));
        var ws = report.Worksheet("Koszty CES");
        report.RecalculateAllFormulas();

        for (var c = 8; c <= SheetLastColumn; c++)
        {
            var rate = sheet.Cell(SheetRateRow, c).CachedValue;
            if (rate.IsNumber)
                Assert.Equal(rate.GetNumber(), ws.Cell(CasCostReport.RateRow, c).GetDouble(), 12);
            for (var r = 0; r < rows.Count; r++)
            {
                var expected = sheet.Cell(SheetFirstRow + r, c).CachedValue;
                var actual = ws.Cell(CasCostReport.FirstDataRow + r, c);
                if (expected.IsNumber)
                    Assert.True(Math.Abs(expected.GetNumber() - actual.CachedValue.GetNumber()) < 1e-6,
                        $"{CasCostReport.Letter(c)}{SheetFirstRow + r}: arkusz {expected.GetNumber()}, raport {actual.CachedValue}");
                else
                    Assert.True(actual.IsEmpty(), $"{CasCostReport.Letter(c)}{SheetFirstRow + r}: raport ma wartość, arkusz – pustą komórkę");
            }
        }
        Assert.Equal(3 * 17, report.Worksheet("Wskaźniki").RowsUsed().Count() - 1);   // okres 07/2026, 2026, 2025 × 17 pul
    }

    [Fact]
    public void Rate_is_taken_from_period_of_column_and_changes_within_year_are_reported()
    {
        List<string> columns = [.. Description, "Period 03/2027", "2027", "2026", "2025"];
        List<IReadOnlyList<object?>> rows = [["G", "P1", "Projekt", "0092210550", "Quality", "Manufacturing and QA labor", 100m, 300m, 1000m, 50m]];
        List<CasCostReport.Rate> rates =
        [
            new("MFG Fringe", new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 30), 0.5m),   // jeden wskaźnik na dwa lata
            new("MFG Fringe", new DateOnly(2026, 7, 1), null, 0.7m),                        // od połowy 2026 bezterminowo
            new("MFG OH", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), 2m),
            new("MFG OH", new DateOnly(2028, 1, 1), null, 9m),                              // poza ostatnim okresem danych (03/2027)
        ];

        var result = CasCostReport.Build(columns, rows, FinanceMapping, FinancePools, FinanceGroups, rates);

        var rateRow = result.Report.Above![1];
        decimal? Rate(string block, string pool) => rateRow[Column(result, block, pool) - 1] is ExcelTableWriter.Formatted { Value: decimal v } ? v : null;
        Assert.Equal((0.7m, 2m), (Rate("Period 03/2027", "MFG Fringe"), Rate("Period 03/2027", "MFG OH")));
        Assert.Equal((0.7m, 2m), (Rate("2027", "MFG Fringe"), Rate("2027", "MFG OH")));   // 2027 do marca – bez wskaźnika od 2028
        Assert.Equal((0.7m, null), (Rate("2026", "MFG Fringe"), Rate("2026", "MFG OH")));
        Assert.Equal((0.5m, null), (Rate("2025", "MFG Fringe"), Rate("2025", "MFG OH")));
        Assert.Contains(result.Notes, n => n.StartsWith("2026: wskaźnik MFG Fringe zmienia się") && n.Contains("2026-07-01"));
        Assert.Contains(result.Notes, n => n == "2026: brak wskaźnika MFG OH w słowniku „Wskaźniki narzutu CAS” – narzut liczony jako 0");
        Assert.Contains(result.Notes, n => n.StartsWith("2025: brak wskaźnika General Services (GS)"));
        Assert.DoesNotContain(result.Notes, n => n.Contains("MMP"));   // pula nienaliczana w żadnym wierszu – brak wskaźnika bez uwagi

        // Blok każdej kolumny kosztu ma własne formuły: MFG Fringe roku 2026 = koszt 2026 × wskaźnik bloku 2026.
        var cost2026 = CasCostReport.Letter(10);
        var fringe = Column(result, "2026", "MFG Fringe");
        Assert.Equal($"={cost2026}{CasCostReport.FirstDataRow}*{CasCostReport.Letter(fringe)}${CasCostReport.RateRow}",
            ((ExcelTableWriter.Formula)result.Report.Rows.Single()[fringe - 1]!).A1);
        Assert.Equal(["Period 03/2027", "2027", "2026", "2025"], result.Report.Headers.Skip(7).Take(4));
        Assert.Equal(11 + 4 * 19, result.Report.Headers.Count);
    }

    [Fact]
    public void Cost_element_without_group_gets_totals_only_and_is_listed_in_notes()
    {
        List<string> columns = [.. Description, "2026"];
        List<IReadOnlyList<object?>> rows =
        [
            ["G", "P1", "Projekt", "51105550", "Mat", "Direct Materials", 10m],        // numer bez zer wiodących – jak klucz słownika
            ["G", "P1", "Projekt", "0099999999", "Nowy", null, 5m],
            ["G", "P1", "Projekt", "0057120550", "Travel", "Travel costs", 1m],
        ];
        var mapping = new Dictionary<string, string?> { ["0051105550"] = "Direct Materials", ["0057120550"] = "Podróże" };

        var result = CasCostReport.Build(columns, rows, mapping, FinancePools, FinanceGroups, []);

        var formulas = result.Report.Rows.Select(r => r.OfType<ExcelTableWriter.Formula>().Select(f => f.A1).ToList()).ToList();
        Assert.Equal("Direct Materials", result.Report.Rows.First()[6]);
        Assert.Equal(8 + 2, formulas[0].Count);   // pule materiałów + TOTAL COST, TOTAL FCCOM
        Assert.Contains("=N4*P$2", formulas[0]);   // GS materiałów – od Material Handling (N), wskaźnik GS w P2
        Assert.Equal(["=SUM(I5:T5)+H5", "=SUM(U5:Y5)"], formulas[1]);
        Assert.Equal(["=SUM(I6:T6)+H6", "=SUM(U6:Y6)"], formulas[2]);
        Assert.Contains(result.Notes, n => n.Contains("bez grupy narzutowej") && n.EndsWith(": 0099999999"));
        Assert.Contains(result.Notes, n => n.Contains("spoza słownika") && n.EndsWith(": Podróże (0057120550)"));
    }

    [Fact]
    public void Dictionary_rows_are_read_as_pools_groups_formulas_and_rates()
    {
        DictRow Row(params (string Column, string? Value)[] values) => new(1, 1, values.ToDictionary(v => v.Column, v => v.Value));

        var pools = CasBurden.PoolsFrom(
        [
            Row(("Pula", "FCCOM G&A"), ("Kolejność", "170"), ("Rodzaj", "FCCOM")),
            Row(("Pula", "MFG Fringe"), ("Kolejność", "10"), ("Rodzaj", "Koszt")),
        ]);
        var groups = CasBurden.GroupsFrom(
            [Row(("Grupa", "Travel"), ("Opis", "Podróże")), Row(("Grupa", "Pusta"), ("Opis", null))],
            [
                Row(("Grupa", "Travel"), ("Pula", "General Services (GS)"), ("Podstawa", "Base cost elements + MFG Fringe +  + MFG Fringe")),
                Row(("Grupa", "Bez opisu"), ("Pula", "MFG Fringe"), ("Podstawa", "Base cost elements")),
            ]);
        var rates = CasCostReport.RatesFrom([Row(("Pula", "FCCOM G&A"), ("Od", "2026-01-01"), ("Do", null), ("Wskaźnik", "0.000057456819309837"))]);

        Assert.Equal([new CasBurden.Pool("MFG Fringe", 10, false), new CasBurden.Pool("FCCOM G&A", 170, true)], pools);
        Assert.Equal(["Travel", "Pusta", "Bez opisu"], groups.Select(g => g.Name));
        Assert.Equal(["Base cost elements", "MFG Fringe"], groups[0].Formulas["General Services (GS)"]);   // bez pustych i powtórzeń
        Assert.Empty(groups[1].Formulas);
        Assert.Equal(new CasCostReport.Rate("FCCOM G&A", new DateOnly(2026, 1, 1), null, 0.000057456819309837m), rates.Single());
    }

    [Fact]
    public void Editor_replaces_formulas_of_one_group_and_keeps_versions()
    {
        DictRow Row(long id, string group, string pool, string basis) =>
            new(id, 3, new Dictionary<string, string?> { ["Grupa"] = group, ["Pula"] = pool, ["Podstawa"] = basis });
        IReadOnlyList<DictRow> current =
        [
            Row(1, "Travel", "General Services (GS)", "Base cost elements"),
            Row(2, "Travel", "Corporate Flowdowns (FD)", "Base cost elements"),
            Row(3, "ODC", "MFG Fringe", "Base cost elements"),
        ];

        var (working, removed) = CasBurden.ReplaceGroup(current, "Travel", new Dictionary<string, IReadOnlyList<string>>
        {
            ["General Services (GS)"] = ["Base cost elements", "MFG Fringe"],
            ["General & Administrative (GA)"] = ["Base cost elements", "General Services (GS)"],
        });

        Assert.Equal([(3L, 3), (1L, 3), (null, null)], working.Select(r => (r.RowId, r.Version)));   // ODC bez zmian, GS – ta sama wersja, G&A – nowy
        Assert.Equal("Base cost elements + MFG Fringe", working[1]["Podstawa"]);
        Assert.Equal(2L, removed.Single().RowId);   // FD odznaczona
        Assert.Empty(DictionaryValidator.Validate(GlobalDictionaries.Get(GlobalDictionaries.CasBurdenFormulas), working));
    }

    [Fact]
    public void Formulas_depending_on_themselves_are_rejected()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.CasBurdenFormulas);
        DictRow Row(string group, string pool, string? basis) =>
            new(null, null, new Dictionary<string, string?> { ["Grupa"] = group, ["Pula"] = pool, ["Podstawa"] = basis });

        var issues = DictionaryValidator.Validate(spec,
        [
            Row("A", "General Services (GS)", "Base cost elements + General & Administrative (GA)"),
            Row("A", "General & Administrative (GA)", "General Services (GS)"),
            Row("A", "MFG OH", "MFG OH"),
            Row("A", "MFG Fringe", "Base cost elements"),
            Row("B", "General Services (GS)", "General & Administrative (GA)"),   // w grupie B G&A nie zależy od GS
            Row("B", "FD", null),
        ]);

        Assert.Equal(["A / General & Administrative (GA)", "A / General Services (GS)", "A / MFG OH"],
            issues.Where(i => i.Message.Contains("zapętlenie")).Select(i => i.Element));
        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Element == "wiersz 6");   // podstawa pusta
    }

    [Fact]
    public void New_pool_from_dictionary_gets_its_column_and_totals_follow_kind()
    {
        List<string> columns = [.. Description, "2026"];
        List<IReadOnlyList<object?>> rows = [["G", "P1", "Projekt", "0092210550", "Quality", "Manufacturing and QA labor", 100m]];
        // Nowa pula kosztu na końcu (po FCCOM) – TOTAL COST jako lista komórek; formuła w grupie z podstawą z innych pul.
        List<CasBurden.Pool> pools = [.. FinancePools, new CasBurden.Pool("Nowa pula", 200, false)];
        var groups = FinanceGroups.Select(g => g.Name != "MFG labor" ? g
            : g with { Formulas = new Dictionary<string, IReadOnlyList<string>>(g.Formulas) { ["Nowa pula"] = ["MFG Fringe", "MFG OH"] } }).ToList();

        var result = CasCostReport.Build(columns, rows, FinanceMapping, pools, groups, [new("Nowa pula", new DateOnly(2026, 1, 1), null, 0.1m)]);

        // Kolumny: H – koszt 2026, I..Y – 17 pul, Z – Nowa pula, AA – TOTAL COST, AB – TOTAL FCCOM.
        Assert.Equal(["Nowa pula", "TOTAL COST 2026", "TOTAL FCCOM 2026"], result.Report.Headers.Skip(25).Take(3));
        var row = result.Report.Rows.Single();
        Assert.Equal("=(I4+J4)*Z$2", ((ExcelTableWriter.Formula)row[25]!).A1);
        Assert.Equal("=SUM(I4,J4,K4,L4,M4,N4,O4,P4,Q4,R4,S4,T4,Z4)+H4", ((ExcelTableWriter.Formula)row[26]!).A1);
        Assert.Equal("=SUM(U4:Y4)", ((ExcelTableWriter.Formula)row[27]!).A1);
        Assert.Equal(new ExcelTableWriter.Formatted(0.1m, "0.0000%"), result.Report.Above![1][25]);
    }

    [Fact]
    public void Rates_of_one_pool_must_not_overlap()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.CasBurdenRates);
        DictRow Row(string pool, string from, string? to, string rate) =>
            new(null, null, new Dictionary<string, string?> { ["Pula"] = pool, ["Od"] = from, ["Do"] = to, ["Wskaźnik"] = rate, ["Opis"] = null });

        var issues = DictionaryValidator.Validate(spec,
        [
            Row("MFG OH", "2025-01-01", "2025-12-31", "1.76"),
            Row("MFG OH", "2025-07-01", null, "2.24"),
            Row("MFG Fringe", "2025-01-01", null, "0.8"),
            Row("ENG OH", "2026-01-01", null, "-0.1"),
        ]);

        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message.Contains("nakłada się") && i.Element == "wiersz 2");
        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message == "Wskaźnik nie może być ujemny" && i.Element == "wiersz 4");
        Assert.DoesNotContain(issues, i => i.Element is "wiersz 1" or "wiersz 3");
    }

    /// <summary>
    /// Raport kosztów CES z bazy (ProjectService.ExportCasCostReport): koszty ostatniego importu ACTUALS, grupa z efektywnego
    /// słownika Cost Category (zmiana projektu przed globalnym), grupy i wskaźniki 2026 z danych startowych (migracja 030).
    /// </summary>
    [SqlFact]
    public void Ces_cost_report_of_project_uses_dictionaries_and_computes_burdens()
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
        var service = new ProjectService(store, new SqlDictionaryStore(database.Sql, services.Clock, services.User, [.. GlobalDictionaries.Tables, .. ProjectDictionaries.Tables]),
            app.Journal, new ProjectFolders(Path.Combine(_folder, "Projekty")), null);
        var tree = new PoTree();
        tree.AddElement(null, "2DI473", "PAF2", null);
        Assert.True(store.Create("PAF", "PAF", ProjectTypes.Cas, tree).Success);
        using (var connection = database.Sql.Open())   // zmiana projektu: podróże bez narzutów
            connection.Execute($"INSERT INTO {database.Sql.Table("dict.CostCategory")} (RowId, Version, Project, CostElement, CostCategory, BurdenGroup, RecordedAt, RecordedBy) " +
                               "VALUES (900001, 1, 'PAF', '0057120550', N'Travel costs', N'ODC', SYSDATETIMEOFFSET(), 'test')");
        var path = Path.Combine(_folder, "PAF_koszty_CES.xlsx");

        Assert.Equal((4, 0), service.ExportCasCostReport(path, "PAF"));

        using var report = new XLWorkbook(path);
        Assert.Equal(["Koszty CES", "Wskaźniki"], report.Worksheets.Select(w => w.Name));
        var ws = report.Worksheet("Koszty CES");
        report.RecalculateAllFormulas();
        var rows = Enumerable.Range(CasCostReport.FirstDataRow, 4).ToDictionary(r => ws.Cell(r, 4).GetString());
        Assert.Equal(["Direct Materials", "ODC", "MFG labor", "ENG labor"],
            new[] { "0051105550", "0057120550", "0092212550", "0094410550" }.Select(e => ws.Cell(rows[e], 7).GetString()));
        Assert.Equal(["Period 03/2026", "2026"], new[] { 8, 9 }.Select(c => ws.Cell(CasCostReport.HeaderRow, c).GetString()));

        // Blok 2026 (od kolumny J): materiały 1 254,51 – MTL Handling, MMP, GS i G&A / FD od MTL Handling.
        double Rate(string pool) => ws.Cell(CasCostReport.RateRow, Header(ws, "2026", pool)).GetDouble();
        double Value(string element, string header) => ws.Cell(rows[element], Header(ws, "2026", header)).CachedValue.GetNumber();
        var mtl = 1254.51 * Rate("Material Handling (MTL)");
        var gs = mtl * Rate("General Services (GS)");
        var cost = 1254.51 + mtl + 1254.51 * Rate("Miscellaneous Material Pool (MMP)") + gs
                   + (mtl + gs) * (Rate("General & Administrative (GA)") + Rate("Corporate Flowdowns (FD)"));
        Assert.Equal(0.035011066852654725, Rate("Material Handling (MTL)"), 15);
        Assert.Equal(cost, Value("0051105550", "TOTAL COST 2026"), 6);
        Assert.Equal(430, Value("0057120550", "TOTAL COST 2026"), 6);   // ODC w projekcie – bez narzutów
        Assert.Equal(0, Value("0057120550", "TOTAL FCCOM 2026"), 6);
        var fringe = 4800 * Rate("MFG Fringe");
        Assert.Equal(fringe, Value("0092212550", "MFG Fringe"), 6);
        Assert.Equal((4800 + fringe) * Rate("MFG OH"), Value("0092212550", "MFG OH"), 6);
        Assert.Equal(2400 * Rate("ENG Fringe"), Value("0094410550", "ENG Fringe"), 6);
        Assert.Equal(17 * 2, report.Worksheet("Wskaźniki").RowsUsed().Count() - 1);
    }

    /// <summary>Kolumna puli w bloku kolumny kosztu (nazwa bloku w wierszu nad nagłówkiem).</summary>
    private static int Header(IXLWorksheet ws, string block, string header) =>
        Enumerable.Range(1, ws.LastColumnUsed()!.ColumnNumber())
            .First(c => ws.Cell(CasCostReport.HeaderRow, c).GetString() == header && ws.Cell(CasCostReport.BlockRow, c).GetString() == block);

    private static int Column(CasCostReport.Result result, string block, string pool)
    {
        var blockRow = result.Report.Above![0];
        for (var c = 0; c < result.Report.Headers.Count; c++)
        {
            if (result.Report.Headers[c] == pool && Equals(blockRow[c], block))
                return c + 1;
        }
        throw new InvalidOperationException($"Brak kolumny {pool} w bloku {block}");
    }
}
