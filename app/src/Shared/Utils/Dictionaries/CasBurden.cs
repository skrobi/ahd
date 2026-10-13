using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Narzuty CAS jako dane (docs/slowniki.md, rozdz. 6.1): pule („Pule narzutu CAS”), grupy („Grupy narzutowe CAS”)
/// i formuły („Formuły narzutów CAS”: grupa + pula → podstawa). Narzut puli w grupie = (suma składników podstawy) ×
/// wskaźnik puli; składnik to koszt bazowy („Base cost elements”) albo inna pula. Wspólne dla raportu kosztów CES
/// i edytora formuł na ekranie Słowniki – reguły podstaw nie są w kodzie.
/// </summary>
public static class CasBurden
{
    /// <summary>Składnik podstawy: koszt bazowy kolumny (raport kosztów).</summary>
    public const string Base = "Base cost elements";

    /// <summary>Separator składników podstawy w słowniku „Formuły narzutów CAS”.</summary>
    public const string Separator = " + ";

    /// <summary>Rodzaj puli: suma w TOTAL COST albo w TOTAL FCCOM.</summary>
    public const string CostKind = "Koszt", FccomKind = "FCCOM";

    /// <summary>Pula: nazwa (nagłówek kolumny), kolejność kolumny, rodzaj (FCCOM – TOTAL FCCOM, inaczej TOTAL COST).</summary>
    public sealed record Pool(string Name, int Order, bool IsFccom);

    /// <summary>Grupa narzutowa: pula naliczana → składniki podstawy (kolejność jak w słowniku).</summary>
    public sealed record Group(string Name, string? Description, IReadOnlyDictionary<string, IReadOnlyList<string>> Formulas);

    /// <summary>Składniki podstawy z zapisu „A + B + C” (puste pominięte, bez powtórzeń).</summary>
    public static IReadOnlyList<string> Parse(string? basis) =>
        (basis ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string Join(IEnumerable<string> components) => string.Join(Separator, components);

    /// <summary>Pule ze słownika „Pule narzutu CAS” w kolejności kolumn (Kolejność, potem nazwa).</summary>
    public static IReadOnlyList<Pool> PoolsFrom(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Pula"] is not null)
            .Select(r => new Pool(r["Pula"]!, int.TryParse(r["Kolejność"], out var order) ? order : int.MaxValue,
                string.Equals(r["Rodzaj"], FccomKind, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Order).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Grupy ze słowników „Grupy narzutowe CAS” i „Formuły narzutów CAS” (formuła grupy spoza słownika grup – też grupa).</summary>
    public static IReadOnlyList<Group> GroupsFrom(IEnumerable<DictRow> groups, IEnumerable<DictRow> formulas)
    {
        var byGroup = formulas.Where(r => r["Grupa"] is not null && r["Pula"] is not null)
            .GroupBy(r => r["Grupa"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, IReadOnlyList<string>>)g
                .ToDictionary(r => r["Pula"]!, r => Parse(r["Podstawa"]), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var described = groups.Where(r => r["Grupa"] is not null).ToList();
        var known = described.Select(r => r["Grupa"]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return described.Select(r => new Group(r["Grupa"]!, r["Opis"], byGroup.GetValueOrDefault(r["Grupa"]!) ?? Empty))
            .Concat(byGroup.Where(g => !known.Contains(g.Key)).Select(g => new Group(g.Key, null, g.Value)))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Empty = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Pule grupy w zapętleniu (pula zależna od samej siebie wprost albo przez inne pule podstawy).</summary>
    public static IReadOnlySet<string> Cycles(IReadOnlyDictionary<string, IReadOnlyList<string>> formulas)
    {
        var cyclic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in formulas.Keys)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>(formulas[start]);
            while (stack.Count > 0)
            {
                var next = stack.Pop();
                if (string.Equals(next, start, StringComparison.OrdinalIgnoreCase))
                {
                    cyclic.Add(start);
                    break;
                }
                if (seen.Add(next) && formulas.TryGetValue(next, out var basis))
                    foreach (var component in basis)
                        stack.Push(component);
            }
        }
        return cyclic;
    }

    /// <summary>„Formuły narzutów CAS”: podstawa niepusta, bez zapętlenia puli w grupie.</summary>
    public static IEnumerable<Issue> FormulaRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (Parse(rows[i]["Podstawa"]).Count == 0)
                yield return Issue.Error("Podstawa – wskaż co najmniej jeden składnik (Base cost elements albo pulę)", DictionaryValidator.RowElement(i));
        }
        foreach (var group in rows.Where(r => r["Grupa"] is not null && r["Pula"] is not null).GroupBy(r => r["Grupa"]!, StringComparer.OrdinalIgnoreCase))
        {
            var formulas = group.GroupBy(r => r["Pula"]!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Parse(g.First()["Podstawa"]), StringComparer.OrdinalIgnoreCase);
            foreach (var pool in Cycles(formulas).Order(StringComparer.OrdinalIgnoreCase))
                yield return Issue.Error($"Pula {pool} zależy od samej siebie (zapętlenie formuł) – usuń ją z podstawy albo z podstaw pul, od których zależy", $"{group.Key} / {pool}");
        }
    }

    /// <summary>
    /// Stan słownika „Formuły narzutów CAS” po edycji jednej grupy (edytor formuł): wiersze innych grup bez zmian,
    /// wiersze grupy – formuły z edytora (istniejąca pula zachowuje RowId i wersję), pule odznaczone – removed.
    /// </summary>
    public static (IReadOnlyList<DictRow> Working, IReadOnlyList<DictRow> Removed) ReplaceGroup(IReadOnlyList<DictRow> current, string group,
        IReadOnlyDictionary<string, IReadOnlyList<string>> formulas)
    {
        bool Of(DictRow r) => string.Equals(r["Grupa"], group, StringComparison.OrdinalIgnoreCase);
        var existing = current.Where(Of).Where(r => r["Pula"] is not null).GroupBy(r => r["Pula"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var working = current.Where(r => !Of(r)).ToList();
        foreach (var (pool, basis) in formulas)
        {
            var values = new Dictionary<string, string?> { ["Grupa"] = group, ["Pula"] = pool, ["Podstawa"] = Join(basis) };
            working.Add(existing.TryGetValue(pool, out var row) ? new DictRow(row.RowId, row.Version, values) : new DictRow(null, null, values));
        }
        var removed = current.Where(Of).Where(r => r["Pula"] is null || !formulas.ContainsKey(r["Pula"]!)).ToList();
        return (working, removed);
    }
}
