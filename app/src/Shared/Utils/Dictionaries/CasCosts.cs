using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Process;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Koszt projektu CAS po narzutach (docs/slowniki.md, rozdz. 6.1) – te same reguły co raport kosztów CES, liczone na
/// kwotach zamiast formuł Excela: koszt bazowy wiersza ACTUALS w PLN (Value in Obj. Crcy – zawsze, także dla projektu
/// w USD); waluta projektu inna niż PLN – koszt przeliczony kursem CAS („Kursy walut”, kolumna Kurs CAS) obowiązującym
/// w okresie kosztu (okresy specjalne 13–16 – grudzień); narzuty grupy narzutowej cost elementu (Cost Category → formuły
/// pul), wskaźnik puli – jak w kolumnie roku raportu (obowiązujący w ostatnim miesiącu roku, w roku ostatniego okresu
/// danych – w tym okresie); koszt projektu = TOTAL COST (koszt + pule rodzaju Koszt; pule FCCOM poza kosztem).
/// Formuły pul są liniowe względem kosztu (koszt × wskaźnik, łańcuchy pul), więc suma wierszy = TOTAL COST sumy.
/// </summary>
public sealed class CasCosts
{
    /// <summary>Koszty wierszy po narzutach w walucie projektu (kolejność wierszy); MissingFx – okresy bez kursu CAS (RRRR-MM).</summary>
    public sealed record Result(IReadOnlyList<decimal> Costs, IReadOnlyList<string> MissingFx, IReadOnlyList<string> Notes)
    {
        public string? Error => MissingFx.Count == 0 ? null
            : $"brak kursu CAS ({GlobalDictionaries.CasFxColumn} w „Kursy walut”) dla okresów z kosztem: {string.Join(", ", MissingFx)}";
    }

    private readonly IReadOnlyList<CasBurden.Pool> _pools;
    private readonly Dictionary<long, CasBurden.Group> _groups;
    private readonly IReadOnlyList<CasBurden.Rate> _rates;
    private readonly Dictionary<string, string?> _groupOf;
    private readonly IReadOnlyList<DictRow> _fx;
    private readonly List<string> _notes = [];
    private readonly Dictionary<long, Dictionary<long, IReadOnlyList<CasFormula.Token>>> _usable = [];
    private readonly Dictionary<(long Pool, int Year, int LastYear, int LastMonth), decimal> _rateCache = [];
    private readonly SortedSet<string> _unmapped = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _unknown = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _noRate = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="burdenGroupOf">Cost element → identyfikator grupy narzutowej (efektywny słownik Cost Category projektu).</param>
    /// <param name="fxRates">Wiersze słownika „Kursy walut”.</param>
    /// <param name="currency">Waluta projektu (PLN – bez przeliczenia).</param>
    public CasCosts(IReadOnlyList<CasBurden.Pool> pools, IReadOnlyList<CasBurden.Group> groups, IReadOnlyList<CasBurden.Rate> rates,
        IReadOnlyDictionary<string, string?> burdenGroupOf, IReadOnlyList<DictRow> fxRates, string currency)
    {
        _pools = pools;
        _groups = groups.ToDictionary(g => g.Id);
        _rates = rates;
        _groupOf = burdenGroupOf.GroupBy(m => Key(m.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
        _fx = fxRates;
        Currency = currency;
    }

    public string Currency { get; }

    private static readonly DictColumn CostElementColumn = GlobalDictionaries.Get(GlobalDictionaries.CostCategory).Columns[0];

    /// <summary>Numer cost elementu jak w słowniku (liczba – uzupełniona zerami do 10 znaków).</summary>
    private static string Key(string? value) =>
        ValueFormat.TryNormalize(CostElementColumn, value, out var canonical, out _) ? canonical ?? "" : value ?? "";

    /// <summary>Miesiące kolumny roku raportu: 1–12, w roku ostatniego okresu danych – do niego.</summary>
    public static IReadOnlyList<DateOnly> YearMonths(int year, (int Year, int Period) last) =>
        Enumerable.Range(1, last.Year == year ? Math.Clamp(last.Period, 1, 12) : 12).Select(m => new DateOnly(year, m, 1)).ToList();

    /// <summary>Przelicznik PLN → waluta projektu w okresie kosztu (1 / kurs CAS); brak kursu – null.</summary>
    public decimal? Fx(int year, int period) =>
        string.Equals(Currency, "PLN", StringComparison.OrdinalIgnoreCase) ? 1
        : FxRate.InMonth(_fx, Currency, year, Math.Clamp(period, 1, 12), GlobalDictionaries.CasFxColumn) is { } rate ? 1 / rate : null;

    /// <summary>TOTAL COST kosztu bazowego (już w walucie projektu) cost elementu w kolumnie roku year.</summary>
    public decimal TotalCost(string? costElement, decimal cost, int year, (int Year, int Period) last)
    {
        if (_groupOf.GetValueOrDefault(Key(costElement)) is not { } reference)
        {
            _unmapped.Add(costElement ?? "(brak)");
            return cost;
        }
        if (CasBurden.Id(reference) is not { } id || !_groups.TryGetValue(id, out var group))
        {
            _unknown.Add(costElement ?? "(brak)");
            return cost;
        }
        if (!_usable.TryGetValue(group.Id, out var formulas))
            _usable[group.Id] = formulas = CasBurden.Usable(group, _pools, _notes).ToDictionary(f => _pools[f.Pool].Id, f => f.Tokens);

        decimal Rate(long pool)
        {
            var key = (pool, year, last.Year, last.Period);
            if (_rateCache.TryGetValue(key, out var cached))
                return cached;
            var rate = CasBurden.RateFor(_rates, pool, YearMonths(year, last)).Rate;
            if (rate is null)
                _noRate.Add($"{year}: {_pools.FirstOrDefault(p => p.Id == pool)?.Name ?? pool.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            return _rateCache[key] = rate?.Value ?? 0;
        }
        var values = new Dictionary<long, decimal>();
        decimal Pool(long pool)
        {
            if (values.TryGetValue(pool, out var value))
                return value;
            // Pula bez formuły w grupie – pusta komórka raportu (0).
            return values[pool] = formulas.TryGetValue(pool, out var tokens) ? CasFormula.Evaluate(tokens, cost, Pool, Rate) : 0;
        }
        return cost + _pools.Where(p => !p.IsFccom && formulas.ContainsKey(p.Id)).Sum(p => Pool(p.Id));
    }

    /// <summary>Koszty wierszy po narzutach w walucie projektu; wiersz bez kursu CAS – 0 i okres w MissingFx.</summary>
    public Result Calculate(IReadOnlyList<CostRow> rows, (int Year, int Period) last)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var costs = new decimal[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (Fx(row.FiscalYear, row.Period) is not { } fx)
            {
                missing.Add($"{row.FiscalYear}-{Math.Clamp(row.Period, 1, 12):00}");
                continue;
            }
            costs[i] = TotalCost(row.CostElement, row.Amount * fx, row.FiscalYear, last);
        }
        return new Result(costs, missing.ToList(), Notes);
    }

    /// <summary>Uwagi: cost elementy bez grupy narzutowej, grupy usunięte, braki wskaźników, formuły pominięte.</summary>
    public IReadOnlyList<string> Notes =>
    [
        .. _notes.Distinct(),
        .. _unmapped.Count > 0
            ? [$"Elementy kosztowe bez grupy narzutowej CAS (bez narzutów – uzupełnij „{GlobalDictionaries.BurdenGroupColumn}” w słowniku Cost Category): {string.Join(", ", _unmapped)}"]
            : Array.Empty<string>(),
        .. _unknown.Count > 0 ? [$"Elementy kosztowe z grupą usuniętą ze słownika „Grupy narzutowe CAS” (bez narzutów): {string.Join(", ", _unknown)}"] : Array.Empty<string>(),
        .. _noRate.Count > 0 ? [$"Brak wskaźnika w słowniku „Wskaźniki narzutu CAS” – liczony jako 0: {string.Join(", ", _noRate)}"] : Array.Empty<string>(),
    ];
}
