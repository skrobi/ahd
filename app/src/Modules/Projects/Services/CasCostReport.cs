using System.Globalization;
using System.Text.RegularExpressions;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Raport kosztów CES (docs/slowniki.md, rozdz. 6.1): raport kosztów projektu (REP_ProjectCosts) z narzutami CAS
/// liczonymi formułami Excela – po kolumnach kosztu bazowego (ostatni okres, lata) blok na każdą kolumnę: pule
/// („Pule narzutu CAS”, w kolejności), TOTAL COST (koszt + pule rodzaju Koszt) i TOTAL FCCOM (pule FCCOM). Formułę puli
/// w wierszu wyznacza grupa narzutowa elementu kosztowego (Cost Category → „Formuły narzutów CAS”): klocki formuły
/// (CasFormula) zamieniane na komórki – koszt bazowy kolumny, narzut puli w bloku, wskaźnik puli z wiersza wskaźników
/// nad nagłówkiem („Wskaźniki narzutu CAS” obowiązujące w okresie kolumny; można go zmienić w pliku).
/// </summary>
public static partial class CasCostReport
{
    /// <summary>Wiersze arkusza: nazwa bloku kolumn, wskaźniki, nagłówek, pierwszy wiersz danych.</summary>
    public const int BlockRow = 1, RateRow = 2, HeaderRow = 3, FirstDataRow = 4;

    public const string BaseLabel = CasBurden.Base;

    /// <summary>Kolumny opisu wiersza raportu kosztów: Grouping … Cost grouping (REP_ProjectCosts).</summary>
    private const int DescriptionColumns = 6;

    private const string AmountFormat = "#,##0.00";
    private const string RateFormat = "0.0000%";

    /// <summary>Wskaźnik puli (identyfikator puli) obowiązujący od – do (To null – bezterminowo).</summary>
    public sealed record Rate(long Pool, DateOnly From, DateOnly? To, decimal Value);

    /// <summary>Arkusz raportu i arkusze pomocnicze: użyte wskaźniki, uwagi (braki mapowania, formuł i wskaźników).</summary>
    public sealed record Result(ExcelTableWriter.ReportSheet Report, ExcelTableWriter.ReportSheet Rates, IReadOnlyList<string> Notes);

    /// <summary>Kolumna kwoty raportu kosztów: nagłówek, etykieta sum bloku (07/2026, 2026) i miesiące, których dotyczy.</summary>
    private sealed record Block(string Header, string Label, IReadOnlyList<DateOnly> Months);

    /// <param name="columns">Nagłówki raportu kosztów (REP_ProjectCosts): 6 kolumn opisu, „Period MM/RRRR”, lata.</param>
    /// <param name="rows">Wiersze raportu kosztów.</param>
    /// <param name="mapping">Identyfikator grupy narzutowej elementu kosztowego (efektywny słownik Cost Category projektu).</param>
    /// <param name="pools">Pule w kolejności kolumn (CasBurden.PoolsFrom).</param>
    /// <param name="groups">Grupy z formułami pul (CasBurden.GroupsFrom).</param>
    public static Result Build(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyDictionary<string, string?> mapping,
        IReadOnlyList<CasBurden.Pool> pools, IReadOnlyList<CasBurden.Group> groups, IReadOnlyList<Rate> rates)
    {
        var notes = new List<string>();
        var blocks = Blocks(columns);
        var byId = groups.ToDictionary(g => g.Id);
        var costElement = GlobalDictionaries.Get(GlobalDictionaries.CostCategory).Columns[0];
        string Key(string? value) => ValueFormat.TryNormalize(costElement, value, out var canonical, out _) ? canonical ?? "" : value ?? "";
        var groupOf = mapping.GroupBy(m => Key(m.Key), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
        var index = pools.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var names = pools.ToDictionary(p => p.Id, p => p.Name);
        var known = pools.Select(p => p.Id).ToHashSet();
        var width = pools.Count + 2;

        // Kolumny: opis, grupa narzutowa, koszt bazowy bloków, bloki (pule, TOTAL COST, TOTAL FCCOM).
        var baseColumn = DescriptionColumns + 2;
        var firstBlock = baseColumn + blocks.Count;
        int Base(int b) => baseColumn + b;
        int Pool(int b, int p) => firstBlock + b * width + p;

        var headers = columns.Take(DescriptionColumns).Append(GlobalDictionaries.BurdenGroupColumn).Concat(blocks.Select(b => b.Header)).ToList();
        var blockRow = Enumerable.Repeat<object?>(null, headers.Count).ToList();
        var rateRow = Enumerable.Repeat<object?>(null, headers.Count).ToList();
        foreach (var (block, b) in blocks.Select((x, i) => (x, i)))
        {
            blockRow[Base(b) - 1] = block.Header;
            rateRow[Base(b) - 1] = BaseLabel;
            headers.AddRange(pools.Select(p => p.Name).Append($"TOTAL COST {block.Label}").Append($"TOTAL FCCOM {block.Label}"));
            blockRow.AddRange(Enumerable.Repeat<object?>(block.Header, width));
            rateRow.AddRange(Enumerable.Repeat<object?>(null, width));
        }

        // Formuły grup: pula spoza słownika pul, formuła błędna albo zapętlona – bez narzutu, z uwagą.
        var usable = new Dictionary<long, List<(int Pool, IReadOnlyList<CasFormula.Token> Tokens)>>();
        var used = new HashSet<long>();
        IReadOnlyList<(int Pool, IReadOnlyList<CasFormula.Token> Tokens)> FormulasOf(CasBurden.Group group)
        {
            if (usable.TryGetValue(group.Id, out var ready))
                return ready;
            var cycles = CasBurden.Cycles(group.Formulas);
            var list = new List<(int, IReadOnlyList<CasFormula.Token>)>();
            foreach (var (pool, tokens) in group.Formulas)
            {
                if (!index.TryGetValue(pool, out var p))
                    notes.Add($"Grupa {group.Name}: formuła puli usuniętej ze słownika „Pule narzutu CAS” – pominięta");
                else if (CasFormula.Error(tokens, known) is { } error)
                    notes.Add($"Grupa {group.Name}, pula {names[pool]}: {error} – bez narzutu");
                else if (cycles.Contains(pool))
                    notes.Add($"Grupa {group.Name}: narzut puli {names[pool]} zależy od niego samego (zapętlenie formuł) – bez narzutu");
                else
                    list.Add((p, tokens));
            }
            list.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return usable[group.Id] = list;
        }

        var costPools = pools.Select((p, i) => (p, i)).Where(x => !x.p.IsFccom).Select(x => x.i).ToList();
        var fccomPools = pools.Select((p, i) => (p, i)).Where(x => x.p.IsFccom).Select(x => x.i).ToList();
        var unmapped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var data = new List<IReadOnlyList<object?>>();
        for (var r = 0; r < rows.Count; r++)
        {
            var source = rows[r];
            var excelRow = FirstDataRow + r;
            var element = source.Count > 3 ? source[3]?.ToString() : null;
            var reference = groupOf.GetValueOrDefault(Key(element));
            IReadOnlyList<(int Pool, IReadOnlyList<CasFormula.Token> Tokens)> formulas = [];
            CasBurden.Group? group = null;
            if (reference is null)
                unmapped.Add(element ?? "(brak)");
            else if (CasBurden.Id(reference) is not { } id || !byId.TryGetValue(id, out group))
                unknown.Add(element ?? "(brak)");
            else
            {
                formulas = FormulasOf(group);
                foreach (var (pool, tokens) in formulas)
                {
                    used.Add(pools[pool].Id);
                    used.UnionWith(tokens.Where(t => t.Kind == CasFormula.Kind.Rate).Select(t => t.Id));
                }
            }

            var row = new object?[headers.Count];
            for (var c = 0; c < DescriptionColumns && c < source.Count; c++)
                row[c] = source[c];
            row[DescriptionColumns] = group?.Name;
            for (var b = 0; b < blocks.Count; b++)
            {
                row[Base(b) - 1] = DescriptionColumns + b < source.Count ? source[DescriptionColumns + b] : null;
                string Cell(int column) => $"{Letter(column)}{excelRow}";
                var cost = Cell(Base(b));
                foreach (var (pool, tokens) in formulas)
                    row[Pool(b, pool) - 1] = new ExcelTableWriter.Formula(
                        CasFormula.Excel(tokens, cost, id => Cell(Pool(b, index[id])), id => $"{Letter(Pool(b, index[id]))}${RateRow}"), AmountFormat);
                // TOTAL COST i TOTAL FCCOM – kolumny za ostatnią pulą (indeks wiersza od 0).
                row[Pool(b, pools.Count) - 1] = new ExcelTableWriter.Formula(
                    costPools.Count == 0 ? $"={cost}" : $"=SUM({Range(costPools.Select(p => Pool(b, p)).ToList(), excelRow)})+{cost}", AmountFormat);
                row[Pool(b, pools.Count)] = new ExcelTableWriter.Formula(
                    fccomPools.Count == 0 ? "=0" : $"=SUM({Range(fccomPools.Select(p => Pool(b, p)).ToList(), excelRow)})", AmountFormat);
            }
            data.Add(row);
        }

        var usedRates = new List<IReadOnlyList<object?>>();
        foreach (var (block, b) in blocks.Select((x, i) => (x, i)))
        {
            foreach (var (pool, p) in pools.Select((x, i) => (x, i)))
            {
                var (rate, changes) = RateFor(rates, pool.Id, block.Months);
                if (rate is not null)
                {
                    rateRow[Pool(b, p) - 1] = new ExcelTableWriter.Formatted(rate.Value, RateFormat);
                    usedRates.Add([block.Header, pool.Name, new ExcelTableWriter.Formatted(rate.Value, RateFormat), rate.From, rate.To]);
                    if (changes && used.Contains(pool.Id))
                        notes.Add($"{block.Header}: wskaźnik {pool.Name} zmienia się w trakcie okresu kolumny – użyty wskaźnik od {rate.From:yyyy-MM-dd} (ostatni miesiąc kolumny)");
                }
                else if (used.Contains(pool.Id))
                    notes.Add($"{block.Header}: brak wskaźnika {pool.Name} w słowniku „Wskaźniki narzutu CAS” – liczony jako 0");
            }
        }
        if (unmapped.Count > 0)
            notes.Add($"Elementy kosztowe bez grupy narzutowej CAS (bez narzutów – uzupełnij „{GlobalDictionaries.BurdenGroupColumn}” w słowniku Cost Category): {string.Join(", ", unmapped)}");
        if (unknown.Count > 0)
            notes.Add($"Elementy kosztowe z grupą usuniętą ze słownika „Grupy narzutowe CAS” (bez narzutów): {string.Join(", ", unknown)}");

        var report = new ExcelTableWriter.ReportSheet("Koszty CES", headers, data, [blockRow, rateRow], FrozenColumns: DescriptionColumns + 1);
        var rateSheet = new ExcelTableWriter.ReportSheet("Wskaźniki", ["Kolumna", "Pula", "Wskaźnik", "Od", "Do"], usedRates);
        return new Result(report, rateSheet, notes);
    }

    /// <summary>Komórki sumy w wierszu: kolumny kolejne – zakres (K4:V4), inaczej lista (K4,M4).</summary>
    private static string Range(IReadOnlyList<int> columns, int row) =>
        columns[^1] - columns[0] == columns.Count - 1
            ? $"{Letter(columns[0])}{row}:{Letter(columns[^1])}{row}"
            : string.Join(",", columns.Select(c => $"{Letter(c)}{row}"));

    /// <summary>Wskaźniki z wierszy słownika „Wskaźniki narzutu CAS” (zapis kanoniczny: daty RRRR-MM-DD, liczba z kropką).</summary>
    public static IReadOnlyList<Rate> RatesFrom(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Pula"] is not null && r["Od"] is not null && r["Wskaźnik"] is not null)
            .Where(r => CasBurden.Id(r["Pula"]) is not null)
            .Select(r => new Rate(CasBurden.Id(r["Pula"])!.Value, Date(r["Od"]!), r["Do"] is { } to ? Date(to) : null,
                decimal.Parse(r["Wskaźnik"]!, NumberStyles.Number, CultureInfo.InvariantCulture)))
            .ToList();

    private static DateOnly Date(string canonical) => DateOnly.ParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Wskaźnik puli dla miesięcy kolumny: wersja obowiązująca w ostatnim miesiącu, dla którego jakaś obowiązuje
    /// (w miesiącu kilka – najpóźniej rozpoczęta); changes – w miesiącach kolumny obowiązują różne wskaźniki.
    /// </summary>
    private static (Rate? Rate, bool Changes) RateFor(IReadOnlyList<Rate> rates, long pool, IReadOnlyList<DateOnly> months)
    {
        var found = months.Select(m => rates
                .Where(r => r.Pool == pool && r.From <= m.AddMonths(1).AddDays(-1) && (r.To is null || r.To >= m))
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
