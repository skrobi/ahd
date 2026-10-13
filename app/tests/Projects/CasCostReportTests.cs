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
/// Raport kosztów CES (CasCostReport) i formuły narzutów CAS z klocków: formuły jak w arkuszu finansów
/// (testdata/Projekty/Narzuty_CAS_arkusz.xlsx, arkusz „Arkusz2”: wiersz 3 – kolumny, 4 – wskaźniki, 5 – nagłówek, dane od 6,
/// opis od kolumny B), pule i grupy wskazywane identyfikatorem, wskaźniki od – do, wiele lat, braki mapowania i wskaźników,
/// edycja klocków, zapętlenia, zmiana nazwy i blokada usunięcia używanej puli (baza).
/// </summary>
public sealed partial class CasCostReportTests : IDisposable
{
    /// <summary>Pule arkusza finansów w kolejności kolumn (dane startowe – migracja 034): 12 kosztu, 5 FCCOM; identyfikatory 1–17.</summary>
    public static IReadOnlyList<string> FinancePoolNames { get; } =
    [
        "MFG Fringe", "MFG OH", "ENG Fringe", "ENG OH", "MTL Fringe", "Material Handling (MTL)", "Miscellaneous Material Pool (MMP)", "General Services (GS)",
        "GS Fringe", "MSV Fringe", "General & Administrative (GA)", "Corporate Flowdowns (FD)", "FCCOM MFG", "FCCOM ENG", "FCCOM MTL", "FCCOM GS", "FCCOM G&A",
    ];

    public static IReadOnlyList<CasBurden.Pool> FinancePools { get; } =
        FinancePoolNames.Select((p, i) => new CasBurden.Pool(i + 1, p, (i + 1) * 10, p.StartsWith("FCCOM"))).ToList();

    /// <summary>
    /// Formuły grup odczytane z arkusza finansów (dane startowe – migracja 034): grupa, pula, klocki z nazwą puli –
    /// p{pula} (narzut), r{pula} (wskaźnik), base (koszt bazowy).
    /// </summary>
    public static IReadOnlyList<(string Group, string Pool, string Formula)> FinanceFormulas { get; } =
    [
        ("GS labor", "General Services (GS)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}"),
        ("GS labor", "GS Fringe", "base * r{GS Fringe}"),
        ("GS labor", "General & Administrative (GA)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}"),
        ("GS labor", "Corporate Flowdowns (FD)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}"),
        ("GS labor", "FCCOM GS", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}"),
        ("GS labor", "FCCOM G&A", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}"),
        ("Direct Materials", "Material Handling (MTL)", "base * r{Material Handling (MTL)}"),
        ("Direct Materials", "Miscellaneous Material Pool (MMP)", "base * r{Miscellaneous Material Pool (MMP)}"),
        ("Direct Materials", "General Services (GS)", "p{Material Handling (MTL)} * r{General Services (GS)}"),
        ("Direct Materials", "General & Administrative (GA)", "( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{General & Administrative (GA)}"),
        ("Direct Materials", "Corporate Flowdowns (FD)", "( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{Corporate Flowdowns (FD)}"),
        ("Direct Materials", "FCCOM MTL", "base * r{FCCOM MTL}"),
        ("Direct Materials", "FCCOM GS", "p{Material Handling (MTL)} * r{FCCOM GS}"),
        ("Direct Materials", "FCCOM G&A", "( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{FCCOM G&A}"),
        ("Direct Services", "Material Handling (MTL)", "base * r{Material Handling (MTL)}"),
        ("Direct Services", "General Services (GS)", "p{Material Handling (MTL)} * r{General Services (GS)}"),
        ("Direct Services", "General & Administrative (GA)", "( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{General & Administrative (GA)}"),
        ("Direct Services", "Corporate Flowdowns (FD)", "( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{Corporate Flowdowns (FD)}"),
        ("Direct Services", "FCCOM MTL", "base * r{FCCOM MTL}"),
        ("Direct Services", "FCCOM GS", "p{Material Handling (MTL)} * r{FCCOM GS}"),
        ("Direct Services", "FCCOM G&A", "( p{Material Handling (MTL)} + p{General Services (GS)} ) * r{FCCOM G&A}"),
        ("Travel", "General Services (GS)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}"),
        ("Travel", "General & Administrative (GA)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}"),
        ("Travel", "Corporate Flowdowns (FD)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}"),
        ("Travel", "FCCOM G&A", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}"),
        ("MFG labor", "MFG Fringe", "base * r{MFG Fringe}"),
        ("MFG labor", "MFG OH", "( base + p{MFG Fringe} ) * r{MFG OH}"),
        ("MFG labor", "General Services (GS)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}"),
        ("MFG labor", "General & Administrative (GA)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}"),
        ("MFG labor", "Corporate Flowdowns (FD)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}"),
        ("MFG labor", "FCCOM MFG", "( base + p{MFG Fringe} ) * r{FCCOM MFG}"),
        ("MFG labor", "FCCOM GS", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}"),
        ("MFG labor", "FCCOM G&A", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}"),
        ("ENG labor", "ENG Fringe", "base * r{ENG Fringe}"),
        ("ENG labor", "ENG OH", "( base + p{ENG Fringe} ) * r{ENG OH}"),
        ("ENG labor", "General Services (GS)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}"),
        ("ENG labor", "General & Administrative (GA)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}"),
        ("ENG labor", "Corporate Flowdowns (FD)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}"),
        ("ENG labor", "FCCOM ENG", "( base + p{ENG Fringe} ) * r{FCCOM ENG}"),
        ("ENG labor", "FCCOM GS", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}"),
        ("ENG labor", "FCCOM G&A", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}"),
        ("ENG LL labor", "ENG OH", "( base + p{ENG Fringe} ) * r{ENG OH}"),
        ("ENG LL labor", "General Services (GS)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}"),
        ("ENG LL labor", "General & Administrative (GA)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}"),
        ("ENG LL labor", "Corporate Flowdowns (FD)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}"),
        ("ENG LL labor", "FCCOM ENG", "( base + p{ENG Fringe} ) * r{FCCOM ENG}"),
        ("ENG LL labor", "FCCOM GS", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}"),
        ("ENG LL labor", "FCCOM G&A", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}"),
        ("MTL labor", "MTL Fringe", "base * r{MTL Fringe}"),
        ("MTL labor", "General Services (GS)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}"),
        ("MTL labor", "General & Administrative (GA)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General & Administrative (GA)}"),
        ("MTL labor", "Corporate Flowdowns (FD)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{Corporate Flowdowns (FD)}"),
        ("MTL labor", "FCCOM GS", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{FCCOM GS}"),
        ("MTL labor", "FCCOM G&A", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{FCCOM G&A}"),
        ("MSV labor", "General Services (GS)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} + p{MTL Fringe} ) * r{General Services (GS)}"),
        ("MSV labor", "MSV Fringe", "base * r{MSV Fringe}"),
        ("MSV labor", "General & Administrative (GA)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{General & Administrative (GA)}"),
        ("MSV labor", "Corporate Flowdowns (FD)", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{Corporate Flowdowns (FD)}"),
        ("MSV labor", "FCCOM GS", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM GS}"),
        ("MSV labor", "FCCOM G&A", "( base + p{MFG Fringe} + p{MFG OH} + p{ENG Fringe} + p{ENG OH} + p{Material Handling (MTL)} + p{General Services (GS)} + p{GS Fringe} + p{MSV Fringe} ) * r{FCCOM G&A}"),
    ];

    /// <summary>Grupy arkusza finansów – identyfikatory 101–110.</summary>
    public static IReadOnlyList<string> FinanceGroupNames { get; } =
        ["MFG labor", "ENG labor", "ENG LL labor", "MSV labor", "GS labor", "MTL labor", "Direct Materials", "Direct Services", "Travel", "ODC"];

    public static long GroupId(string name) => 101 + FinanceGroupNames.ToList().IndexOf(name);

    public static long PoolId(string name) => 1 + FinancePoolNames.ToList().IndexOf(name);

    /// <summary>Zapis klocków z nazwami pul (jak w migracji 034) → klocki z identyfikatorami.</summary>
    public static IReadOnlyList<CasFormula.Token> Tokens(string formula, Func<string, long> id) =>
        CasFormula.Parse(TokenName().Replace(formula, m => $"{m.Groups[1].Value}:{id(m.Groups[2].Value)}"))!;

    [GeneratedRegex(@"([pr])\{([^}]+)\}")]
    private static partial Regex TokenName();

    public static IReadOnlyList<CasBurden.Group> FinanceGroups { get; } = FinanceGroupNames
        .Select(g => new CasBurden.Group(GroupId(g), g, null, FinanceFormulas.Where(f => f.Group == g)
            .ToDictionary(f => PoolId(f.Pool), f => Tokens(f.Formula, PoolId))))
        .ToList();

    /// <summary>Grupa narzutowa elementów kosztowych arkusza finansów (dane startowe – migracja 034).</summary>
    public static IReadOnlyDictionary<string, string> FinanceMappingNames { get; } = new Dictionary<string, string>
    {
        ["0096606550"] = "GS labor", ["0051105550"] = "Direct Materials", ["0051110550"] = "Direct Services", ["0057120550"] = "Travel",
        ["0057510550"] = "ODC", ["0057511550"] = "ODC", ["0057712550"] = "ODC", ["0057714550"] = "ODC",
        ["0092210550"] = "MFG labor", ["0092211550"] = "MFG labor", ["0092212550"] = "MFG labor", ["0092213550"] = "MFG labor",
        ["0092214550"] = "MFG labor", ["0092215550"] = "MFG labor", ["0092216550"] = "MFG labor", ["0092217550"] = "MFG labor",
        ["0092218550"] = "MFG labor", ["0092219550"] = "MFG labor", ["0092223550"] = "MFG labor", ["9221X550"] = "MFG labor", ["9222X550"] = "MFG labor",
        ["0094410550"] = "ENG labor", ["0094414550"] = "ENG labor", ["0094412550"] = "ENG labor", ["0094490550"] = "ENG LL labor",
        ["0096610550"] = "MTL labor", ["9441X550"] = "ENG labor", ["9229X550"] = "MSV labor", ["9660R550"] = "MSV labor",
    };

    public static IReadOnlyDictionary<string, string?> FinanceMapping { get; } =
        FinanceMappingNames.ToDictionary(m => m.Key, m => (string?)CasBurden.Ref(GroupId(m.Value)));

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
            rates.Add(new CasCostReport.Rate(p + 1, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), (decimal)sheet.Cell(SheetRateRow, 30 + p).GetDouble()));
            rates.Add(new CasCostReport.Rate(p + 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), (decimal)sheet.Cell(SheetRateRow, 49 + p).GetDouble()));
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
        long fringe = PoolId("MFG Fringe"), oh = PoolId("MFG OH");
        List<CasCostReport.Rate> rates =
        [
            new(fringe, new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 30), 0.5m),   // jeden wskaźnik na dwa lata
            new(fringe, new DateOnly(2026, 7, 1), null, 0.7m),                        // od połowy 2026 bezterminowo
            new(oh, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), 2m),
            new(oh, new DateOnly(2028, 1, 1), null, 9m),                              // poza ostatnim okresem danych (03/2027)
        ];

        var result = CasCostReport.Build(columns, rows, FinanceMapping, FinancePools, FinanceGroups, rates);

        var rateRow = result.Report.Above![1];
        decimal? Rate(string block, string pool) => rateRow[Column(result, block, pool) - 1] is ExcelTableWriter.Formatted { Value: decimal v } ? v : null;
        Assert.Equal((0.7m, 2m), (Rate("Period 03/2027", "MFG Fringe"), Rate("Period 03/2027", "MFG OH")));
        Assert.Equal((0.7m, 2m), (Rate("2027", "MFG Fringe"), Rate("2027", "MFG OH")));   // 2027 do marca – bez wskaźnika od 2028
        Assert.Equal((0.7m, null), (Rate("2026", "MFG Fringe"), Rate("2026", "MFG OH")));
        Assert.Equal((0.5m, null), (Rate("2025", "MFG Fringe"), Rate("2025", "MFG OH")));
        Assert.Contains(result.Notes, n => n.StartsWith("2026: wskaźnik MFG Fringe zmienia się") && n.Contains("2026-07-01"));
        Assert.Contains(result.Notes, n => n == "2026: brak wskaźnika MFG OH w słowniku „Wskaźniki narzutu CAS” – liczony jako 0");
        Assert.Contains(result.Notes, n => n.StartsWith("2025: brak wskaźnika General Services (GS)"));
        Assert.DoesNotContain(result.Notes, n => n.Contains("MMP"));   // pula nienaliczana w żadnym wierszu – brak wskaźnika bez uwagi

        // Blok każdej kolumny kosztu ma własne formuły: MFG Fringe roku 2026 = koszt 2026 × wskaźnik bloku 2026.
        var column = Column(result, "2026", "MFG Fringe");
        Assert.Equal($"={CasCostReport.Letter(10)}{CasCostReport.FirstDataRow}*{CasCostReport.Letter(column)}${CasCostReport.RateRow}",
            ((ExcelTableWriter.Formula)result.Report.Rows.Single()[column - 1]!).A1);
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
        var mapping = new Dictionary<string, string?> { ["0051105550"] = CasBurden.Ref(GroupId("Direct Materials")), ["0057120550"] = "999" };

        var result = CasCostReport.Build(columns, rows, mapping, FinancePools, FinanceGroups, []);

        var formulas = result.Report.Rows.Select(r => r.OfType<ExcelTableWriter.Formula>().Select(f => f.A1).ToList()).ToList();
        Assert.Equal("Direct Materials", result.Report.Rows.First()[6]);   // nazwa grupy, nie identyfikator
        Assert.Equal(8 + 2, formulas[0].Count);   // pule materiałów + TOTAL COST, TOTAL FCCOM
        Assert.Contains("=N4*P$2", formulas[0]);   // GS materiałów – od Material Handling (N), wskaźnik GS w P2
        Assert.Equal(["=SUM(I5:T5)+H5", "=SUM(U5:Y5)"], formulas[1]);
        Assert.Equal(["=SUM(I6:T6)+H6", "=SUM(U6:Y6)"], formulas[2]);
        Assert.Contains(result.Notes, n => n.Contains("bez grupy narzutowej") && n.EndsWith(": 0099999999"));
        Assert.Contains(result.Notes, n => n.Contains("grupą usuniętą") && n.EndsWith(": 0057120550"));
    }

    [Fact]
    public void Formula_blocks_are_validated_displayed_and_written_to_excel()
    {
        var tokens = Tokens("( base + p{MFG Fringe} ) * r{MFG OH} - p{MFG Fringe} / ( r{MFG Fringe} )", PoolId);
        var names = FinancePools.ToDictionary(p => p.Id, p => p.Name);

        Assert.Null(CasFormula.Error(tokens, names.Keys.ToHashSet()));
        Assert.Equal("( base + p:1 ) * r:2 - p:1 / ( r:1 )", CasFormula.Serialize(tokens));
        Assert.Equal(tokens, CasFormula.Parse(CasFormula.Serialize(tokens)));
        Assert.Equal("(Base cost elements + MFG Fringe) × wskaźnik MFG OH − MFG Fringe ÷ (wskaźnik MFG Fringe)", CasFormula.Display(tokens, id => names[id]));
        Assert.Equal("=(H4+K4)*L$2-K4/(K$2)", CasFormula.Excel(tokens, "H4", id => $"{(char)('J' + id)}4", id => $"{(char)('J' + id)}$2"));
        Assert.Equal([1L], CasFormula.Pools(tokens));

        string? Error(string formula) => CasFormula.Error(Tokens(formula, PoolId), names.Keys.ToHashSet());
        Assert.Contains("pusta", Error(""));
        Assert.Contains("Brak operatora", Error("base p{MFG OH}"));
        Assert.Contains("zaczynać się od operatora", Error("* base"));
        Assert.Contains("kończyć się operatorem", Error("base +"));
        Assert.Contains("Dwa operatory", Error("base + * r{MFG OH}"));
        Assert.Contains("Niedomknięte nawiasy", Error("( base + p{MFG Fringe} * r{MFG OH}"));
        Assert.Contains("bez otwierającego", Error("base ) * r{MFG OH}"));
        Assert.Contains("Pusty nawias", Error("base * ( )"));
        Assert.Contains("usunięta", CasFormula.Error([CasFormula.Token.BaseCost, CasFormula.Token.Op('*'), CasFormula.Token.Rate(999)], names.Keys.ToHashSet()));
        Assert.Null(CasFormula.Parse("base * x:1"));   // zapis nieczytelny
    }

    [Fact]
    public void Builder_inserts_moves_and_removes_blocks_at_cursor()
    {
        long oh = PoolId("MFG OH"), fringe = PoolId("MFG Fringe");
        var draft = new CasFormulaDraft(oh, CasFormula.Default(oh));   // base * r:OH, kursor na końcu

        draft.MoveCursor(1);                                           // za „base”
        draft.Insert(CasFormula.Token.Op('+'));
        draft.Insert(CasFormula.Token.Pool(fringe));                   // base + p:Fringe * r:OH
        draft.Insert(CasFormula.Token.Open, at: 0);                    // upuszczenie na początku
        draft.Insert(CasFormula.Token.Close, at: 4);                   // ( base + p:Fringe ) * r:OH
        Assert.Equal("( base + p:1 ) * r:2", CasFormula.Serialize(draft.Tokens));
        Assert.Equal(5, draft.Cursor);
        Assert.Null(draft.Error());

        draft.Move(from: 3, to: 1);                                    // przeciągnięcie p:Fringe przed base
        Assert.Equal("( p:1 base + ) * r:2", CasFormula.Serialize(draft.Tokens));
        Assert.NotNull(draft.Error());
        draft.Move(from: 1, to: 4);
        Assert.Equal("( base + p:1 ) * r:2", CasFormula.Serialize(draft.Tokens));

        draft.MoveCursor(99);
        draft.Backspace();                                             // usuwa r:OH
        draft.MoveCursor(0);
        draft.Delete();                                                // usuwa „(”
        Assert.Equal("base + p:1 ) *", CasFormula.Serialize(draft.Tokens));
        draft.Replace([CasFormula.Token.Pool(oh)]);
        Assert.Contains("nie może zależeć od niego samego", draft.Error());
    }

    [Fact]
    public void Matrix_changes_replace_formulas_and_keep_versions()
    {
        DictRow Row(long id, long group, long pool, string formula) =>
            new(id, 3, new Dictionary<string, string?> { ["Grupa"] = CasBurden.Ref(group), ["Pula"] = CasBurden.Ref(pool), ["Formuła"] = formula });
        IReadOnlyList<DictRow> current = [Row(1, 101, 8, "base * r:8"), Row(2, 101, 12, "base * r:12"), Row(3, 110, 1, "base * r:1")];

        var (working, removed) = CasBurden.Apply(current,
        [
            new CasBurden.Change(101, 8, Tokens("( base + p{MFG Fringe} ) * r{General Services (GS)}", PoolId)),
            new CasBurden.Change(101, 11, CasFormula.Default(11)),
            new CasBurden.Change(101, 12, null),
        ]);

        Assert.Equal([(1L, 3), (3L, 3), (null, null)], working.Select(r => (r.RowId, r.Version)));   // GS – ta sama wersja, G&A – nowy, ODC bez zmian
        Assert.Equal("( base + p:1 ) * r:8", working[0]["Formuła"]);
        Assert.Equal(2L, removed.Single().RowId);   // FD odznaczona
        Assert.Empty(DictionaryValidator.Validate(GlobalDictionaries.Get(GlobalDictionaries.CasBurdenFormulas), working));
    }

    [Fact]
    public void Formulas_depending_on_themselves_are_rejected()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.CasBurdenFormulas);
        DictRow Row(long group, string pool, string? formula) => new(null, null, new Dictionary<string, string?>
        {
            ["Grupa"] = CasBurden.Ref(group), ["Pula"] = CasBurden.Ref(PoolId(pool)), ["Formuła"] = formula is null ? null : CasFormula.Serialize(Tokens(formula, PoolId)),
        });

        var issues = DictionaryValidator.Validate(spec,
        [
            Row(1, "General Services (GS)", "( base + p{General & Administrative (GA)} ) * r{General Services (GS)}"),
            Row(1, "General & Administrative (GA)", "p{General Services (GS)} * r{General & Administrative (GA)}"),
            Row(1, "MFG OH", "p{MFG OH} * r{MFG OH}"),
            Row(1, "MFG Fringe", "base * r{MFG Fringe}"),
            Row(2, "General Services (GS)", "p{General & Administrative (GA)} * r{General Services (GS)}"),   // w grupie 2 G&A nie zależy od GS
            Row(2, "Corporate Flowdowns (FD)", "base +"),
        ]);

        Assert.Equal(["grupa 1 / pula 2", "grupa 1 / pula 8", "grupa 1 / pula 11"], issues.Where(i => i.Message.Contains("zapętlenie")).Select(i => i.Element));
        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Element == "wiersz 6" && i.Message.Contains("kończyć się operatorem"));
    }

    [Fact]
    public void New_pool_from_dictionary_gets_its_column_and_totals_follow_kind()
    {
        List<string> columns = [.. Description, "2026"];
        List<IReadOnlyList<object?>> rows = [["G", "P1", "Projekt", "0092210550", "Quality", "Manufacturing and QA labor", 100m]];
        // Nowa pula kosztu na końcu (po FCCOM) – TOTAL COST jako lista komórek; formuła w grupie z innych pul.
        List<CasBurden.Pool> pools = [.. FinancePools, new CasBurden.Pool(50, "Nowa pula", 200, false)];
        var groups = FinanceGroups.Select(g => g.Name != "MFG labor" ? g : g with
        {
            Formulas = new Dictionary<long, IReadOnlyList<CasFormula.Token>>(g.Formulas) { [50] = Tokens("( p{MFG Fringe} + p{MFG OH} ) * r{Nowa pula}", n => n == "Nowa pula" ? 50 : PoolId(n)) },
        }).ToList();

        var result = CasCostReport.Build(columns, rows, FinanceMapping, pools, groups, [new(50, new DateOnly(2026, 1, 1), null, 0.1m)]);

        // Kolumny: H – koszt 2026, I..Y – 17 pul, Z – Nowa pula, AA – TOTAL COST, AB – TOTAL FCCOM.
        Assert.Equal(["Nowa pula", "TOTAL COST 2026", "TOTAL FCCOM 2026"], result.Report.Headers.Skip(25).Take(3));
        var row = result.Report.Rows.Single();
        Assert.Equal("=(I4+J4)*Z$2", ((ExcelTableWriter.Formula)row[25]!).A1);
        Assert.Equal("=SUM(I4,J4,K4,L4,M4,N4,O4,P4,Q4,R4,S4,T4,Z4)+H4", ((ExcelTableWriter.Formula)row[26]!).A1);
        Assert.Equal("=SUM(U4:Y4)", ((ExcelTableWriter.Formula)row[27]!).A1);
        Assert.Equal(new ExcelTableWriter.Formatted(0.1m, "0.0000%"), result.Report.Above![1][25]);
    }

    [Fact]
    public void Renamed_pool_and_group_keep_their_formulas_because_ids_link_them()
    {
        List<string> columns = [.. Description, "2026"];
        List<IReadOnlyList<object?>> rows = [["G", "P1", "Projekt", "0092210550", "Quality", "Manufacturing and QA labor", 100m]];
        var pools = FinancePools.Select(p => p.Name == "MFG Fringe" ? p with { Name = "Fringe produkcji" } : p).ToList();
        var groups = FinanceGroups.Select(g => g.Name == "MFG labor" ? g with { Name = "Robocizna produkcji" } : g).ToList();

        var renamed = CasCostReport.Build(columns, rows, FinanceMapping, pools, groups, []);
        var original = CasCostReport.Build(columns, rows, FinanceMapping, FinancePools, FinanceGroups, []);

        Assert.Equal("Robocizna produkcji", renamed.Report.Rows.Single()[6]);
        Assert.Equal("Fringe produkcji", renamed.Report.Headers[8]);
        Assert.Equal(original.Report.Rows.Single().OfType<ExcelTableWriter.Formula>(), renamed.Report.Rows.Single().OfType<ExcelTableWriter.Formula>());
        Assert.Contains(renamed.Notes, n => n.Contains("brak wskaźnika Fringe produkcji"));
    }

    [Fact]
    public void Dictionary_rows_are_read_as_pools_groups_formulas_and_rates()
    {
        DictRow Row(long id, params (string Column, string? Value)[] values) => new(id, 1, values.ToDictionary(v => v.Column, v => v.Value));

        var pools = CasBurden.PoolsFrom(
        [
            Row(17, ("Pula", "FCCOM G&A"), ("Kolejność", "170"), ("Rodzaj", "FCCOM")),
            Row(1, ("Pula", "MFG Fringe"), ("Kolejność", "10"), ("Rodzaj", "Koszt")),
        ]);
        var groups = CasBurden.GroupsFrom(
            [Row(109, ("Grupa", "Travel"), ("Opis", "Podróże")), Row(110, ("Grupa", "Pusta"), ("Opis", null))],
            [
                Row(1, ("Grupa", "109"), ("Pula", "8"), ("Formuła", "( base + p:1 ) * r:8")),
                Row(2, ("Grupa", "109"), ("Pula", "11"), ("Formuła", "nieczytelna")),   // pominięta
                Row(3, ("Grupa", "555"), ("Pula", "1"), ("Formuła", "base * r:1")),      // grupa spoza słownika grup
            ]);
        var rates = CasCostReport.RatesFrom([Row(5, ("Pula", "17"), ("Od", "2026-01-01"), ("Do", null), ("Wskaźnik", "0.000057456819309837"))]);

        Assert.Equal([new CasBurden.Pool(1, "MFG Fringe", 10, false), new CasBurden.Pool(17, "FCCOM G&A", 170, true)], pools);
        Assert.Equal(["Travel", "Pusta"], groups.Select(g => g.Name));
        Assert.Equal("( base + p:1 ) * r:8", CasFormula.Serialize(groups[0].Formulas[8]));
        Assert.Single(groups[0].Formulas);
        Assert.Empty(groups[1].Formulas);
        Assert.Equal(new CasCostReport.Rate(17, new DateOnly(2026, 1, 1), null, 0.000057456819309837m), rates.Single());
        Assert.Equal([new LookupOption("1", "MFG Fringe"), new LookupOption("17", "FCCOM G&A")],
            GlobalDictionaries.LookupOptions(GlobalDictionaries.CasPools, [Row(17, ("Pula", "FCCOM G&A"), ("Kolejność", "170")), Row(1, ("Pula", "MFG Fringe"), ("Kolejność", "10"))]));
    }

    [Fact]
    public void Rates_of_one_pool_must_not_overlap()
    {
        var spec = GlobalDictionaries.Get(GlobalDictionaries.CasBurdenRates);
        DictRow Row(string pool, string from, string? to, string rate) =>
            new(null, null, new Dictionary<string, string?> { ["Pula"] = pool, ["Od"] = from, ["Do"] = to, ["Wskaźnik"] = rate, ["Opis"] = null });

        var issues = DictionaryValidator.Validate(spec,
        [
            Row("2", "2025-01-01", "2025-12-31", "1.76"),
            Row("2", "2025-07-01", null, "2.24"),
            Row("1", "2025-01-01", null, "0.8"),
            Row("4", "2026-01-01", null, "-0.1"),
        ]);

        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message.Contains("nakłada się") && i.Element == "wiersz 2");
        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message == "Wskaźnik nie może być ujemny" && i.Element == "wiersz 4");
        Assert.DoesNotContain(issues, i => i.Element is "wiersz 1" or "wiersz 3");
    }

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
        var dictionaries = new DictionaryService(new SqlDictionaryStore(database.Sql, services.Clock, services.User, [.. GlobalDictionaries.Tables, .. ProjectDictionaries.Tables]), app.Journal);
        var groupSpec = GlobalDictionaries.Get(GlobalDictionaries.CasBurdenGroups);
        var odc = dictionaries.Load(groupSpec).Single(r => r["Grupa"] == "ODC").RowId!.Value;
        using (var connection = database.Sql.Open())   // zmiana projektu: podróże bez narzutów
            connection.Execute($"INSERT INTO {database.Sql.Table("dict.CostCategory")} (RowId, Version, Project, CostElement, CostCategory, BurdenGroupId, RecordedAt, RecordedBy) " +
                               "VALUES (900001, 1, 'PAF', '0057120550', N'Travel costs', @odc, SYSDATETIMEOFFSET(), 'test')", new { odc = CasBurden.Ref(odc) });
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

        // Zmiana nazwy puli i grupy w słownikach – formuły, wskaźniki i mapowanie wskazują je identyfikatorem.
        var poolSpec = GlobalDictionaries.Get(GlobalDictionaries.CasPools);
        DictRow Renamed(DictRow row, string column, string name) => row with { Values = new Dictionary<string, string?>(row.Values) { [column] = name } };
        var pools = dictionaries.Load(poolSpec);
        Assert.Equal(SaveStatus.Saved, dictionaries.Save(poolSpec, pools.Select(r => r["Pula"] == "MFG Fringe" ? Renamed(r, "Pula", "Fringe produkcji") : r).ToList(), [], true).Status);
        var groups = dictionaries.Load(groupSpec);
        Assert.Equal(SaveStatus.Saved, dictionaries.Save(groupSpec, groups.Select(r => r["Grupa"] == "MFG labor" ? Renamed(r, "Grupa", "Robocizna produkcji") : r).ToList(), [], true).Status);
        Assert.Equal((4, 0), service.ExportCasCostReport(path, "PAF"));
        using (var renamed = new XLWorkbook(path))
        {
            var sheet = renamed.Worksheet("Koszty CES");
            renamed.RecalculateAllFormulas();
            Assert.Equal("Robocizna produkcji", sheet.Cell(rows["0092212550"], 7).GetString());
            Assert.Equal(fringe, sheet.Cell(rows["0092212550"], Header(sheet, "2026", "Fringe produkcji")).CachedValue.GetNumber(), 6);
        }

        // Puli i grupy używanej w formułach, wskaźnikach albo Cost Category (także w projekcie) nie można usunąć.
        var used = dictionaries.Save(poolSpec, dictionaries.Load(poolSpec).Where(r => r["Pula"] != "Fringe produkcji").ToList(),
            dictionaries.Load(poolSpec).Where(r => r["Pula"] == "Fringe produkcji").ToList(), true);
        Assert.Equal(SaveStatus.Rejected, used.Status);
        Assert.Contains(used.Issues, i => i.Message.Contains("„Fringe produkcji” jest używane w słowniku „Formuły narzutów CAS”"));
        Assert.Contains(used.Issues, i => i.Message.Contains("jest używane w słowniku „Wskaźniki narzutu CAS”"));
        var odcRow = dictionaries.Load(groupSpec).Where(r => r["Grupa"] == "ODC").ToList();
        var group = dictionaries.Save(groupSpec, dictionaries.Load(groupSpec).Except(odcRow).ToList(), odcRow, true);
        Assert.Contains(group.Issues, i => i.Message.Contains("„ODC” jest używane w słowniku „Cost Category”") && i.Message.Contains("także w projektach"));

        // Excel słownika wskaźników: nazwa puli zamiast identyfikatora; wczytanie zamienia nazwę z powrotem na identyfikator.
        var rateSpec = GlobalDictionaries.Get(GlobalDictionaries.CasBurdenRates);
        var excel = Path.Combine(_folder, "wskazniki.xlsx");
        dictionaries.Export(rateSpec, excel);
        using (var file = new XLWorkbook(excel))
            Assert.Contains("Fringe produkcji", file.Worksheet(1).Column(1).CellsUsed().Select(c => c.GetString()));
        var preview = dictionaries.PreviewImport(rateSpec, excel);
        Assert.False(preview.HasErrors, string.Join("; ", preview.Issues.Select(i => $"{i.Message} ({i.Element})")));
        Assert.False(preview.HasChanges, string.Join("; ", preview.Changed.Take(3)));
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
