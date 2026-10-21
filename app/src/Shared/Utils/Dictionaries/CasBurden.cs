using System.Globalization;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Narzuty CAS jako dane (docs/slowniki.md, rozdz. 6.1): pule („Kategorie narzutu CAS”), grupy („Grupy narzutowe CAS”)
/// i formuły („Formuły narzutów CAS”: grupa + pula → formuła z klocków – CasFormula). Grupy, pule, wskaźniki i mapowanie
/// w Cost Category wskazują pulę / grupę identyfikatorem wiersza (RowId), nie nazwą: zmiana nazwy w słowniku pul albo
/// grup jest od razu widoczna w formułach, wskaźnikach, Cost Category i raporcie. Reguł podstaw nie ma w kodzie.
/// </summary>
public static class CasBurden
{
    /// <summary>Klocek kosztu bazowego kolumny raportu (okres, rok).</summary>
    public const string Base = "Base cost elements";

    /// <summary>Rodzaj puli: suma w TOTAL COST albo w TOTAL FCCOM.</summary>
    public const string CostKind = "Koszt", FccomKind = "FCCOM";

    /// <summary>Pula: identyfikator (RowId), nazwa (nagłówek kolumny), kolejność kolumny, rodzaj (FCCOM – TOTAL FCCOM).</summary>
    public sealed record Pool(long Id, string Name, int Order, bool IsFccom);

    /// <summary>Grupa narzutowa: identyfikator, nazwa, opis; formuły – pula (identyfikator) → klocki.</summary>
    public sealed record Group(long Id, string Name, string? Description, IReadOnlyDictionary<long, IReadOnlyList<CasFormula.Token>> Formulas);

    /// <summary>Identyfikator wiersza zapisany w kolumnie powiązanej (DictColumn.Lookup z IdLookup).</summary>
    public static long? Id(string? value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    public static string Ref(long id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pule ze słownika „Kategorie narzutu CAS” w kolejności kolumn (Kolejność, potem nazwa); wiersze zapisane (z RowId).</summary>
    public static IReadOnlyList<Pool> PoolsFrom(IEnumerable<DictRow> rows) =>
        rows.Where(r => r.RowId is not null && r["Kategoria"] is not null)
            .Select(r => new Pool(r.RowId!.Value, r["Kategoria"]!, int.TryParse(r["Kolejność"], out var order) ? order : int.MaxValue,
                string.Equals(r["Rodzaj"], FccomKind, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Grupy ze słowników „Grupy narzutowe CAS” i „Formuły narzutów CAS”; formuła uszkodzona (zapis nieczytelny) – pominięta.</summary>
    public static IReadOnlyList<Group> GroupsFrom(IEnumerable<DictRow> groups, IEnumerable<DictRow> formulas)
    {
        var byGroup = formulas
            .Select(r => (Group: Id(r["Grupa"]), Pool: Id(r["Kategoria"]), Tokens: CasFormula.Parse(r["Formuła"])))
            .Where(f => f.Group is not null && f.Pool is not null && f.Tokens is not null)
            .GroupBy(f => f.Group!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<long, IReadOnlyList<CasFormula.Token>>)g
                .GroupBy(f => f.Pool!.Value).ToDictionary(p => p.Key, p => p.First().Tokens!));
        return groups.Where(r => r.RowId is not null && r["Grupa"] is not null)
            .Select(r => new Group(r.RowId!.Value, r["Grupa"]!, r["Opis"], byGroup.GetValueOrDefault(r.RowId!.Value) ?? Empty))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<long, IReadOnlyList<CasFormula.Token>> Empty = new Dictionary<long, IReadOnlyList<CasFormula.Token>>();

    /// <summary>Pule grupy w zapętleniu (narzut puli zależy od niej samej wprost albo przez narzuty innych pul).</summary>
    public static IReadOnlySet<long> Cycles(IReadOnlyDictionary<long, IReadOnlyList<CasFormula.Token>> formulas)
    {
        var cyclic = new HashSet<long>();
        foreach (var start in formulas.Keys)
        {
            var seen = new HashSet<long>();
            var stack = new Stack<long>(CasFormula.Pools(formulas[start]));
            while (stack.Count > 0)
            {
                var next = stack.Pop();
                if (next == start)
                {
                    cyclic.Add(start);
                    break;
                }
                if (seen.Add(next) && formulas.TryGetValue(next, out var tokens))
                    foreach (var pool in CasFormula.Pools(tokens))
                        stack.Push(pool);
            }
        }
        return cyclic;
    }

    /// <summary>Wskaźnik puli (identyfikator puli) obowiązujący od – do (To null – bezterminowo).</summary>
    public sealed record Rate(long Pool, DateOnly From, DateOnly? To, decimal Value);

    /// <summary>Wskaźniki z wierszy słownika „Wskaźniki narzutu CAS” (zapis kanoniczny: daty RRRR-MM-DD, liczba z kropką).</summary>
    public static IReadOnlyList<Rate> RatesFrom(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Kategoria"] is not null && r["Od"] is not null && r["Wskaźnik"] is not null)
            .Where(r => Id(r["Kategoria"]) is not null)
            .Select(r => new Rate(Id(r["Kategoria"])!.Value, Date(r["Od"]!), r["Do"] is { } to ? Date(to) : null,
                decimal.Parse(r["Wskaźnik"]!, NumberStyles.Number, CultureInfo.InvariantCulture)))
            .ToList();

    private static DateOnly Date(string canonical) => DateOnly.ParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Wskaźnik puli dla miesięcy kolumny raportu: wersja obowiązująca w ostatnim miesiącu, dla którego jakaś obowiązuje
    /// (w miesiącu kilka – najpóźniej rozpoczęta); changes – w miesiącach kolumny obowiązują różne wskaźniki.
    /// </summary>
    public static (Rate? Rate, bool Changes) RateFor(IReadOnlyList<Rate> rates, long pool, IReadOnlyList<DateOnly> months)
    {
        var found = months.Select(m => rates
                .Where(r => r.Pool == pool && r.From <= m.AddMonths(1).AddDays(-1) && (r.To is null || r.To >= m))
                .MaxBy(r => r.From))
            .OfType<Rate>().ToList();
        return found.Count == 0 ? (null, false) : (found[^1], found.Select(r => r.Value).Distinct().Count() > 1);
    }

    /// <summary>
    /// Formuły grupy do naliczenia (indeks puli w pools, klocki) w kolejności pul: pula spoza słownika pul, formuła błędna
    /// albo zapętlona – bez narzutu, z uwagą w notes (raport kosztów CES i koszt projektu CAS liczą tak samo).
    /// </summary>
    public static IReadOnlyList<(int Pool, IReadOnlyList<CasFormula.Token> Tokens)> Usable(Group group, IReadOnlyList<Pool> pools, ICollection<string> notes)
    {
        var index = pools.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var known = pools.Select(p => p.Id).ToHashSet();
        var cycles = Cycles(group.Formulas);
        var list = new List<(int Pool, IReadOnlyList<CasFormula.Token> Tokens)>();
        foreach (var (pool, tokens) in group.Formulas)
        {
            if (!index.TryGetValue(pool, out var p))
                notes.Add($"Grupa {group.Name}: formuła kategorii usuniętej ze słownika „Kategorie narzutu CAS” – pominięta");
            else if (CasFormula.Error(tokens, known) is { } error)
                notes.Add($"Grupa {group.Name}, kategoria {pools[p].Name}: {error} – bez narzutu");
            else if (cycles.Contains(pool))
                notes.Add($"Grupa {group.Name}: narzut kategorii {pools[p].Name} zależy od niego samego (zapętlenie formuł) – bez narzutu");
            else
                list.Add((p, tokens));
        }
        list.Sort((x, y) => x.Pool.CompareTo(y.Pool));
        return list;
    }

    /// <summary>„Formuły narzutów CAS”: zapis czytelny, formuła poprawnie zbudowana, bez zapętlenia pul w grupie.</summary>
    public static IEnumerable<Issue> FormulaRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var tokens = CasFormula.Parse(rows[i]["Formuła"]);
            if (tokens is null)
                yield return Issue.Error("Formuła nieczytelna – zbuduj ją w edytorze formuł", DictionaryValidator.RowElement(i));
            else if (CasFormula.Error(tokens) is { } error)
                yield return Issue.Error(error, DictionaryValidator.RowElement(i));
        }
        foreach (var group in rows.Where(r => Id(r["Grupa"]) is not null && Id(r["Kategoria"]) is not null).GroupBy(r => Id(r["Grupa"])!.Value))
        {
            var formulas = group.Select(r => (Pool: Id(r["Kategoria"])!.Value, Tokens: CasFormula.Parse(r["Formuła"])))
                .Where(f => f.Tokens is not null).GroupBy(f => f.Pool).ToDictionary(f => f.Key, f => f.First().Tokens!);
            foreach (var pool in Cycles(formulas).Order())
                yield return Issue.Error("Narzut kategorii zależy od niego samego (zapętlenie formuł) – usuń klocek kategorii z formuły", $"grupa {group.Key} / kategoria {pool}");
        }
    }

    /// <summary>Formuła grupy i puli w edycji macierzy: klocki albo null – pula nienaliczana (formuła do usunięcia).</summary>
    public sealed record Change(long Group, long Pool, IReadOnlyList<CasFormula.Token>? Tokens);

    /// <summary>
    /// Stan słownika „Formuły narzutów CAS” po zmianach z macierzy: istniejąca formuła zachowuje RowId i wersję (nowa
    /// wersja przy zapisie), nowa – nowy wiersz, pula odznaczona – removed.
    /// </summary>
    public static (IReadOnlyList<DictRow> Working, IReadOnlyList<DictRow> Removed) Apply(IReadOnlyList<DictRow> current, IEnumerable<Change> changes)
    {
        var working = current.ToList();
        var removed = new List<DictRow>();
        foreach (var change in changes)
        {
            var index = working.FindIndex(r => Id(r["Grupa"]) == change.Group && Id(r["Kategoria"]) == change.Pool);
            if (change.Tokens is null)
            {
                if (index >= 0)
                {
                    removed.Add(working[index]);
                    working.RemoveAt(index);
                }
                continue;
            }
            var values = new Dictionary<string, string?>
            {
                ["Grupa"] = Ref(change.Group), ["Kategoria"] = Ref(change.Pool), ["Formuła"] = CasFormula.Serialize(change.Tokens),
            };
            if (index >= 0)
                working[index] = new DictRow(working[index].RowId, working[index].Version, values);
            else
                working.Add(new DictRow(null, null, values));
        }
        return (working, removed);
    }

    /// <summary>Wiersz słownika wskazuje usuwaną pulę albo grupę: identyfikator w kolumnie albo klocek formuły z pulą.</summary>
    public static bool References(string? value, long id) =>
        Id(value) == id || (CasFormula.Parse(value) is { } tokens && tokens.Any(t => t.Kind is CasFormula.Kind.Pool or CasFormula.Kind.Rate && t.Id == id));
}
