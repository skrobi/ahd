using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Strona P1S nakładki (docs/performance-objectives.md, rozdz. 2): kody P1S węzłów to Legacy WBS z Performance
/// Objectives; zakres P1S projektu – te kody i poddrzewa LOG.WBS pod nimi (PZLPROD).
/// </summary>
public static class ObjectivesMapping
{
    /// <summary>Kody P1S węzła: Legacy WBS z Performance Objectives.</summary>
    public static IReadOnlyList<string> CodesOf(PoNode node) =>
        node.LegacyWbs is { Length: > 0 } legacy ? [legacy] : [];

    /// <summary>
    /// Zakres P1S projektu: kody węzłów i elementy LOG.WBS pod nimi (poddrzewa wg PARENT) – pod Legacy WBS
    /// (P1S bywa rozbudowane głębiej niż Legacy WBS z Excela).
    /// </summary>
    public static P1sScope Scope(PoTree tree, P1sInputs inputs)
    {
        var roots = tree.Nodes.Where(n => !n.IsVirtual).SelectMany(CodesOf).ToList();
        var below = new List<(string, string)>();
        if (inputs.P1s is { } p1s)
        {
            // Element LOG.WBS należy do najbliższego kodu węzła nad nim (wspinaczka po PARENT).
            var targets = new Dictionary<string, string>();
            var byCode = p1s.GroupBy(e => MappingKeys.Key(e.WbsElement)).ToDictionary(g => g.Key, g => g.First());
            foreach (var code in roots)
            {
                if (byCode.GetValueOrDefault(MappingKeys.Key(code)) is { } element)
                    targets.TryAdd(MappingKeys.Key(element.Pspnr), element.WbsElement);
            }
            var parents = p1s.GroupBy(e => MappingKeys.Key(e.Pspnr)).ToDictionary(g => g.Key, g => MappingKeys.Key(g.First().Parent));
            foreach (var element in p1s.Where(e => !targets.ContainsKey(MappingKeys.Key(e.Pspnr))))
            {
                var seen = new HashSet<string>();
                var parent = parents.GetValueOrDefault(MappingKeys.Key(element.Pspnr), "");
                while (parent.Length > 0 && seen.Add(parent))
                {
                    if (targets.TryGetValue(parent, out var root))
                    {
                        below.Add((element.WbsElement, root));
                        break;
                    }
                    parent = parents.GetValueOrDefault(parent, "");
                }
            }
        }
        return new P1sScope(roots, below);
    }

    /// <summary>
    /// Kontrola gotowości – strona P1S nakładki: element CES bez Legacy WBS nie ma kodu P1S, więc nie podepnie się do
    /// niego żaden WP (WARNING); brak PZLPROD – WARNING z powodem.
    /// </summary>
    public static Issue Check(PoTree tree, P1sInputs inputs)
    {
        var elements = tree.Nodes.Where(n => !n.IsVirtual && !string.IsNullOrWhiteSpace(n.WbsElement)).ToList();
        var withoutCode = elements.Where(n => CodesOf(n).Count == 0).Select(n => n.WbsElement!).ToList();
        if (withoutCode.Count > 0)
            return Issue.Warning($"Elementy nakładki bez Legacy WBS – nie podepnie się do nich WP: {string.Join(", ", withoutCode.Take(10))}{(withoutCode.Count > 10 ? "…" : "")}", "Strona P1S");
        if (inputs.P1sError is not null)
            return Issue.Warning($"{inputs.P1sError} – zakres P1S bez poddrzew LOG.WBS", "Strona P1S");
        return new Issue(Shared.Models.Pipeline.CheckLevel.Pass, $"Strona P1S: {elements.Count - withoutCode.Count} z {elements.Count} elementów z Legacy WBS");
    }
}
