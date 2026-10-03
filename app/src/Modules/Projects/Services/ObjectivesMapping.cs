using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Strona P1S nakładki z globalnego mapowania (docs/performance-objectives.md, rozdz. 2): element CES nakładki
/// rozstrzygany tymi samymi regułami co ekran Mapowanie (korekta elementu, raport, dziedziczenie z projektu CES –
/// kolumna Project definition, gdy elementu nie ma w kosztach). Nakładka tylko czyta mapowanie.
/// </summary>
public static class ObjectivesMapping
{
    /// <summary>Wynik mapowania elementów CES nakładki: klucz węzła → wynik.</summary>
    public static IReadOnlyDictionary<long, MappingResult> Resolve(PoTree tree, MappingInputs inputs)
    {
        var costs = inputs.CostElements.GroupBy(e => MappingKeys.Key(e.WbsElement)).ToDictionary(g => g.Key, g => g.First());
        var nodes = tree.Nodes.Where(n => !n.IsVirtual && !string.IsNullOrWhiteSpace(n.WbsElement)).ToList();
        var elements = nodes
            .GroupBy(n => MappingKeys.Key(n.WbsElement))
            .Select(g => costs.TryGetValue(g.Key, out var cost)
                ? cost with { Project = cost.Project.Length > 0 ? cost.Project : g.First().ProjectDefinition ?? "" }
                : new CesElement(g.First().WbsElement!, g.First().ProjectDefinition ?? "", false, 0, 0))
            .ToList();
        var results = MappingResolver.Resolve(elements, inputs.Report, inputs.Corrections, inputs.P1s, null).Results
            .GroupBy(r => MappingKeys.Key(r.CesElement)).ToDictionary(g => g.Key, g => g.First());
        return nodes
            .Where(n => results.ContainsKey(MappingKeys.Key(n.WbsElement)))
            .ToDictionary(n => n.Key, n => results[MappingKeys.Key(n.WbsElement)]);
    }

    /// <summary>Kody P1S węzła: Legacy WBS z Excela i cel z mapowania.</summary>
    public static IReadOnlyList<string> CodesOf(PoNode node, IReadOnlyDictionary<long, MappingResult> mapping)
    {
        var codes = new List<string>(2);
        if (node.LegacyWbs is { Length: > 0 } legacy)
            codes.Add(legacy);
        if (mapping.TryGetValue(node.Key, out var result) && result.IsMapped && result.Target.Length > 0
            && !codes.Contains(result.Target, StringComparer.OrdinalIgnoreCase))
            codes.Add(result.Target);
        return codes;
    }

    /// <summary>Zakres P1S projektu: kody węzłów i elementy LOG.WBS pod celami mapowania (poddrzewa wg PARENT).</summary>
    public static P1sScope Scope(PoTree tree, IReadOnlyDictionary<long, MappingResult> mapping, MappingInputs inputs)
    {
        var roots = tree.Nodes.Where(n => !n.IsVirtual).SelectMany(n => CodesOf(n, mapping)).ToList();
        var below = new List<(string, string)>();
        if (inputs.P1s is { } p1s)
        {
            // Element LOG.WBS należy do najbliższego celu mapowania nad nim (wspinaczka po PARENT).
            var targets = mapping.Values.Where(r => r.IsMapped && r.TargetPspnr.Length > 0)
                .GroupBy(r => MappingKeys.Key(r.TargetPspnr)).ToDictionary(g => g.Key, g => g.First().Target);
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
    /// Kontrola gotowości – strona P1S nakładki: element CES bez celu mapowania i bez Legacy WBS nie ma kodu P1S, więc
    /// nie podepnie się do niego żaden WP (WARNING); brak raportu mapowań albo PZLPROD – WARNING z powodem.
    /// </summary>
    public static Issue Check(PoTree tree, IReadOnlyDictionary<long, MappingResult> mapping, MappingInputs inputs)
    {
        var elements = tree.Nodes.Where(n => !n.IsVirtual && !string.IsNullOrWhiteSpace(n.WbsElement)).ToList();
        var withoutCode = elements.Where(n => CodesOf(n, mapping).Count == 0).Select(n => n.WbsElement!).ToList();
        var mapped = elements.Count(n => mapping.TryGetValue(n.Key, out var r) && r.IsMapped);
        if (withoutCode.Count > 0)
            return Issue.Warning($"Elementy nakładki bez strony P1S (UNMAPPED i bez Legacy WBS) – nie podepnie się do nich WP: {string.Join(", ", withoutCode.Take(10))}{(withoutCode.Count > 10 ? "…" : "")}", "Mapowanie CES ↔ P1S");
        if (inputs.Report is null && inputs.Corrections.Count == 0)
            return Issue.Warning("Raport mapowań nie został zaimportowany – strona P1S tylko z Legacy WBS", "Mapowanie CES ↔ P1S");
        if (inputs.P1sError is not null)
            return Issue.Warning($"{inputs.P1sError} – zakres P1S bez poddrzew LOG.WBS", "Mapowanie CES ↔ P1S");
        return new Issue(Shared.Models.Pipeline.CheckLevel.Pass, $"Strona P1S: {mapped} z {elements.Count} elementów z celem mapowania CES ↔ P1S");
    }
}
