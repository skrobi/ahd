using System.Globalization;
using System.Text.RegularExpressions;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Raport kosztów CES (docs/model-danych.md, migracja 029): raport kosztów projektu (REP_ProjectCosts) z narzutami CAS
/// liczonymi formułami Excela tak jak w arkuszu finansów – po kolumnach kosztu bazowego (ostatni okres, lata) blok na
/// każdą kolumnę: 12 pul kosztu, 5 pul FCCOM, TOTAL COST i TOTAL FCCOM. Pule wiersza wyznacza grupa narzutowa elementu
/// kosztowego (słownik Cost Category → „Grupy narzutowe CAS”), wskaźniki – „Wskaźniki narzutu CAS” obowiązujące
/// w okresie kolumny (wiersz wskaźników nad nagłówkiem; formuły się do niego odwołują, więc można go zmienić w pliku).
/// Podstawy pul (kaskada arkusza finansów):
/// <list type="bullet">
/// <item>Fringe, Material Handling, MMP, FCCOM MTL – koszt bazowy;</item>
/// <item>MFG OH, FCCOM MFG – koszt + MFG Fringe; ENG OH, FCCOM ENG – koszt + ENG Fringe;</item>
/// <item>GS – koszt + Fringe + OH + Material Handling (bez MMP); FCCOM GS – to samo, MTL Fringe tylko gdy naliczany;</item>
/// <item>G&amp;A, FD, FCCOM G&amp;A – podstawa GS + GS (MTL Fringe tylko gdy naliczany);</item>
/// <item>grupa bez kosztu bazowego w podstawie GS i G&amp;A (suche koszty materiałów i usług): GS, FCCOM GS – od Material
/// Handling; G&amp;A, FD, FCCOM G&amp;A – od Material Handling + GS.</item>
/// </list>
/// </summary>
public static partial class CasCostReport
{
    /// <summary>Wiersze arkusza: nazwa bloku kolumn, wskaźniki, nagłówek, pierwszy wiersz danych.</summary>
    public const int BlockRow = 1, RateRow = 2, HeaderRow = 3, FirstDataRow = 4;

    public const string BaseLabel = "Base cost elements";

    /// <summary>Kolumny opisu wiersza raportu kosztów: Grouping … Cost grouping (REP_ProjectCosts).</summary>
    private const int DescriptionColumns = 6;

    private const string AmountFormat = "#,##0.00";
    private const string RateFormat = "0.0000%";

    private const string MfgFringe = "MFG Fringe", MfgOh = "MFG OH", EngFringe = "ENG Fringe", EngOh = "ENG OH", MtlFringe = "MTL Fringe",
        MtlHandling = "Material Handling (MTL)", Mmp = "Miscellaneous Material Pool (MMP)", Gs = "General Services (GS)", GsFringe = "GS Fringe",
        MsvFringe = "MSV Fringe", Ga = "General & Administrative (GA)", Fd = "Corporate Flowdowns (FD)", FccomMfg = "FCCOM MFG",
        FccomEng = "FCCOM ENG", FccomMtl = "FCCOM MTL", FccomGs = "FCCOM GS", FccomGa = "FCCOM G&A";

    /// <summary>Pule w kolejności kolumn bloku (słownik „Grupy narzutowe CAS”).</summary>
    public static IReadOnlyList<string> Pools => PoolOrder;

    private static readonly List<string> PoolOrder = GlobalDictionaries.CasPools.Select(p => p.Pool).ToList();

    /// <summary>Grupa narzutowa: pule naliczane na koszt elementu i czy koszt bazowy wchodzi do podstawy GS i G&amp;A.</summary>
    public sealed record BurdenGroup(string Name, bool BaseInGsGa, IReadOnlySet<string> Pools);

    /// <summary>Wskaźnik puli obowiązujący od – do (To null – bezterminowo).</summary>
    public sealed record Rate(string Pool, DateOnly From, DateOnly? To, decimal Value);

    /// <summary>Arkusz raportu i arkusze pomocnicze: użyte wskaźniki, uwagi (braki mapowania i wskaźników).</summary>
    public sealed record Result(ExcelTableWriter.ReportSheet Report, ExcelTableWriter.ReportSheet Rates, IReadOnlyList<string> Notes);

    /// <summary>Kolumna kwoty raportu kosztów: nagłówek, etykieta sum bloku (07/2026, 2026) i miesiące, których dotyczy.</summary>
    private sealed record Block(string Header, string Label, IReadOnlyList<DateOnly> Months);

    /// <param name="columns">Nagłówki raportu kosztów (REP_ProjectCosts): 6 kolumn opisu, „Period MM/RRRR”, lata.</param>
    /// <param name="rows">Wiersze raportu kosztów.</param>
    /// <param name="mapping">Grupa narzutowa elementu kosztowego (efektywny słownik Cost Category projektu).</param>
    public static Result Build(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyDictionary<string, string?> mapping,
        IReadOnlyList<BurdenGroup> groups, IReadOnlyList<Rate> rates)
    {
        var notes = new List<string>();
        var blocks = Blocks(columns);
        var byName = groups.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
        var costElement = GlobalDictionaries.Get(GlobalDictionaries.CostCategory).Columns[0];
        string Key(string? value) => ValueFormat.TryNormalize(costElement, value, out var canonical, out _) ? canonical ?? "" : value ?? "";
        var groupOf = mapping.GroupBy(m => Key(m.Key), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

        // Kolumny: opis, grupa narzutowa, koszt bazowy bloków, bloki (pule, TOTAL COST, TOTAL FCCOM).
        var baseColumn = DescriptionColumns + 2;
        var firstBlock = baseColumn + blocks.Count;
        int Base(int b) => baseColumn + b;
        int Pool(int b, string pool) => firstBlock + b * (Pools.Count + 2) + PoolOrder.IndexOf(pool);

        var headers = columns.Take(DescriptionColumns).Append(GlobalDictionaries.BurdenGroupColumn).Concat(blocks.Select(b => b.Header)).ToList();
        var blockRow = Enumerable.Repeat<object?>(null, headers.Count).ToList();
        var rateRow = Enumerable.Repeat<object?>(null, headers.Count).ToList();
        foreach (var (block, b) in blocks.Select((x, i) => (x, i)))
        {
            blockRow[Base(b) - 1] = block.Header;
            rateRow[Base(b) - 1] = BaseLabel;
            headers.AddRange(Pools.Append($"TOTAL COST {block.Label}").Append($"TOTAL FCCOM {block.Label}"));
            for (var p = 0; p < Pools.Count + 2; p++)
                blockRow.Add(block.Header);
            rateRow.AddRange(Enumerable.Repeat<object?>(null, Pools.Count + 2));
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmapped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var data = new List<IReadOnlyList<object?>>();
        for (var r = 0; r < rows.Count; r++)
        {
            var source = rows[r];
            var excelRow = FirstDataRow + r;
            var element = source.Count > 3 ? source[3]?.ToString() : null;
            var name = groupOf.GetValueOrDefault(Key(element));
            BurdenGroup? group = null;
            if (name is null)
                unmapped.Add(element ?? "(brak)");
            else if (!byName.TryGetValue(name, out group))
                unknown.Add($"{name} ({element})");
            if (group is not null)
                used.UnionWith(group.Pools);

            var row = new object?[headers.Count];
            for (var c = 0; c < DescriptionColumns && c < source.Count; c++)
                row[c] = source[c];
            row[DescriptionColumns] = name;
            for (var b = 0; b < blocks.Count; b++)
            {
                row[Base(b) - 1] = DescriptionColumns + b < source.Count ? source[DescriptionColumns + b] : null;
                string Cell(int column) => $"{Letter(column)}{excelRow}";
                string Of(string pool) => Cell(Pool(b, pool));
                var cost = Cell(Base(b));
                if (group is not null)
                {
                    foreach (var pool in Pools.Where(group.Pools.Contains))
                    {
                        var basis = Basis(pool, group, cost, Of);
                        row[Pool(b, pool) - 1] = new ExcelTableWriter.Formula($"={basis}*{Letter(Pool(b, pool))}${RateRow}", AmountFormat);
                    }
                }
                // TOTAL COST i TOTAL FCCOM – kolumny za ostatnią pulą (indeks wiersza od 0).
                row[Pool(b, FccomGa)] = new ExcelTableWriter.Formula($"=SUM({Of(MfgFringe)}:{Of(Fd)})+{cost}", AmountFormat);
                row[Pool(b, FccomGa) + 1] = new ExcelTableWriter.Formula($"=SUM({Of(FccomMfg)}:{Of(FccomGa)})", AmountFormat);
            }
            data.Add(row);
        }

        var usedRates = new List<IReadOnlyList<object?>>();
        foreach (var (block, b) in blocks.Select((x, i) => (x, i)))
        {
            foreach (var pool in Pools)
            {
                var (rate, changes) = RateFor(rates, pool, block.Months);
                if (rate is not null)
                {
                    rateRow[Pool(b, pool) - 1] = new ExcelTableWriter.Formatted(rate.Value, RateFormat);
                    usedRates.Add([block.Header, pool, new ExcelTableWriter.Formatted(rate.Value, RateFormat), rate.From, rate.To]);
                    if (changes && used.Contains(pool))
                        notes.Add($"{block.Header}: wskaźnik {pool} zmienia się w trakcie okresu kolumny – użyty wskaźnik od {rate.From:yyyy-MM-dd} (ostatni miesiąc kolumny)");
                }
                else if (used.Contains(pool))
                    notes.Add($"{block.Header}: brak wskaźnika {pool} w słowniku „Wskaźniki narzutu CAS” – narzut liczony jako 0");
            }
        }
        if (unmapped.Count > 0)
            notes.Add($"Elementy kosztowe bez grupy narzutowej CAS (bez narzutów – uzupełnij „{GlobalDictionaries.BurdenGroupColumn}” w słowniku Cost Category): {string.Join(", ", unmapped)}");
        if (unknown.Count > 0)
            notes.Add($"Grupy narzutowe spoza słownika „Grupy narzutowe CAS” (bez narzutów): {string.Join(", ", unknown)}");

        var report = new ExcelTableWriter.ReportSheet("Koszty CES", headers, data, [blockRow, rateRow], FrozenColumns: DescriptionColumns + 1);
        var rateSheet = new ExcelTableWriter.ReportSheet("Wskaźniki", ["Kolumna", "Pula", "Wskaźnik", "Od", "Do"], usedRates);
        return new Result(report, rateSheet, notes);
    }

    /// <summary>Grupy z wierszy słownika „Grupy narzutowe CAS” (pula zaznaczona „tak” – naliczana).</summary>
    public static IReadOnlyList<BurdenGroup> GroupsFrom(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Grupa"] is not null)
            .Select(r => new BurdenGroup(r["Grupa"]!, r[GlobalDictionaries.BaseInGsGaColumn] == "tak",
                Pools.Where(p => r[p] == "tak").ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>Wskaźniki z wierszy słownika „Wskaźniki narzutu CAS” (zapis kanoniczny: daty RRRR-MM-DD, liczba z kropką).</summary>
    public static IReadOnlyList<Rate> RatesFrom(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Pula"] is not null && r["Od"] is not null && r["Wskaźnik"] is not null)
            .Select(r => new Rate(r["Pula"]!, Date(r["Od"]!), r["Do"] is { } to ? Date(to) : null,
                decimal.Parse(r["Wskaźnik"]!, NumberStyles.Number, CultureInfo.InvariantCulture)))
            .ToList();

    private static DateOnly Date(string canonical) => DateOnly.ParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Podstawa puli w formule (bez nawiasów, gdy to jedna komórka).</summary>
    private static string Basis(string pool, BurdenGroup group, string cost, Func<string, string> of)
    {
        var mtlFringe = group.Pools.Contains(MtlFringe);
        IEnumerable<string> parts = pool switch
        {
            MfgFringe or EngFringe or MtlFringe or GsFringe or MsvFringe or MtlHandling or Mmp or FccomMtl => [cost],
            MfgOh or FccomMfg => [cost, of(MfgFringe)],
            EngOh or FccomEng => [cost, of(EngFringe)],
            Gs when group.BaseInGsGa => [cost, of(MfgFringe), of(MfgOh), of(EngFringe), of(EngOh), of(MtlHandling), of(GsFringe), of(MsvFringe), of(MtlFringe)],
            FccomGs when group.BaseInGsGa =>
                [cost, of(MfgFringe), of(MfgOh), of(EngFringe), of(EngOh), of(MtlHandling), of(GsFringe), of(MsvFringe), .. (mtlFringe ? new[] { of(MtlFringe) } : Array.Empty<string>())],
            Gs or FccomGs => [of(MtlHandling)],
            Ga or Fd or FccomGa when group.BaseInGsGa =>
                [cost, of(MfgFringe), of(MfgOh), of(EngFringe), of(EngOh), of(MtlHandling), of(Gs), of(GsFringe), of(MsvFringe), .. (mtlFringe ? new[] { of(MtlFringe) } : Array.Empty<string>())],
            Ga or Fd or FccomGa => [of(MtlHandling), of(Gs)],
            _ => throw new ArgumentOutOfRangeException(nameof(pool), pool, "Nieznana pula narzutu CAS"),
        };
        var list = parts.ToList();
        return list.Count == 1 ? list[0] : $"({string.Join("+", list)})";
    }

    /// <summary>
    /// Wskaźnik puli dla miesięcy kolumny: wersja obowiązująca w ostatnim miesiącu, dla którego jakaś obowiązuje
    /// (w miesiącu kilka – najpóźniej rozpoczęta); changes – w miesiącach kolumny obowiązują różne wskaźniki.
    /// </summary>
    private static (Rate? Rate, bool Changes) RateFor(IReadOnlyList<Rate> rates, string pool, IReadOnlyList<DateOnly> months)
    {
        var found = months.Select(m => rates
                .Where(r => string.Equals(r.Pool, pool, StringComparison.OrdinalIgnoreCase) && r.From <= m.AddMonths(1).AddDays(-1) && (r.To is null || r.To >= m))
                .MaxBy(r => r.From))
            .OfType<Rate>().ToList();
        return found.Count == 0 ? (null, false) : (found[^1], found.Select(r => r.Value).Distinct().Count() > 1);
    }

    /// <summary>Kolumny kwot raportu kosztów: „Period MM/RRRR” (miesiąc) i „RRRR” (rok – do ostatniego okresu danych).</summary>
    private static List<Block> Blocks(IReadOnlyList<string> columns)
    {
        var headers = columns.Skip(DescriptionColumns).ToList();
        DateOnly? last = null;
        var blocks = new List<Block>();
        foreach (var header in headers)
        {
            if (PeriodHeader().Match(header) is { Success: true } period)
            {
                var month = new DateOnly(int.Parse(period.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(period.Groups[1].Value, CultureInfo.InvariantCulture), 1);
                last = month;
                blocks.Add(new Block(header, $"{period.Groups[1].Value}/{period.Groups[2].Value}", [month]));
            }
            else if (YearHeader().IsMatch(header))
            {
                var year = int.Parse(header, CultureInfo.InvariantCulture);
                var months = Enumerable.Range(1, last is { } l && l.Year == year ? l.Month : 12).Select(m => new DateOnly(year, m, 1)).ToList();
                blocks.Add(new Block(header, header, months));
            }
            else
                blocks.Add(new Block(header, header, []));
        }
        return blocks;
    }

    /// <summary>Litera kolumny Excela (1 → A, 27 → AA).</summary>
    public static string Letter(int column)
    {
        var letters = "";
        for (; column > 0; column = (column - 1) / 26)
            letters = (char)('A' + (column - 1) % 26) + letters;
        return letters;
    }

    [GeneratedRegex(@"^Period (\d{2})/(\d{4})$")]
    private static partial Regex PeriodHeader();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex YearHeader();
}
