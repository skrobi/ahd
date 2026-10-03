using PzlEv.Modules.Mapping.Models;
using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Modules.Mapping.Services;

/// <summary>
/// Drzewo P1S (docs/zrodla-danych.md, rozdz. 5.3): Z_KAT_ZBIORCZA → Z_KATEGORIA → Z_OPIS → PROJORG → elementy wg PARENT.
/// PROJORG (element STUFE = 1) trafia pod kategorie swojego wiersza; bez kategorii – do grupy „Bez kategorii w WBS”.
/// Element, którego rodzica nie ma w LOG.WBS, trafia pod swój PROJORG, a bez niego – do „Bez kategorii w WBS”.
/// Elementy nieaktywne i usunięte zostają (wyszarzone).
/// </summary>
public static class P1sTreeBuilder
{
    public const string NoCategory = "Bez kategorii w WBS";
    public const string NoDescription = "(bez Z_OPIS)";

    public static IReadOnlyList<P1sNode> Build(IReadOnlyList<P1sElement> elements)
    {
        var byKey = new Dictionary<string, P1sElement>();
        foreach (var element in elements.Where(e => e.Pspnr.Length > 0))
            byKey.TryAdd(MappingKeys.Key(element.Pspnr), element);
        var children = elements
            .Where(e => e.Level != 1 && e.Parent.Length > 0 && byKey.ContainsKey(MappingKeys.Key(e.Parent)))
            .GroupBy(e => MappingKeys.Key(e.Parent))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.WbsElement, StringComparer.Ordinal).ToList());

        var roots = elements.Where(e => e.Level == 1).OrderBy(e => e.WbsElement, StringComparer.Ordinal).ToList();
        var rootsByProjOrg = new Dictionary<string, P1sNode>();
        var categories = new SortedDictionary<string, P1sNode>(StringComparer.CurrentCulture);
        var noCategory = new P1sNode(NoCategory);
        var placed = new HashSet<P1sElement>(ReferenceEqualityComparer.Instance);

        foreach (var root in roots)
        {
            var node = Subtree(root, children, placed);
            rootsByProjOrg.TryAdd(MappingKeys.Key(root.ProjOrg.Length > 0 ? root.ProjOrg : root.WbsElement), node);
            if (root.CategoryGroup.Length == 0 && root.Category.Length == 0)
            {
                noCategory.Children.Add(node);
                continue;
            }
            var group = Child(categories, root.CategoryGroup.Length > 0 ? root.CategoryGroup : NoCategory);
            var category = Child(group, root.Category.Length > 0 ? root.Category : NoCategory);
            Child(category, root.GroupDescription.Length > 0 ? root.GroupDescription : NoDescription).Children.Add(node);
        }

        // Elementy poza hierarchią (rodzic spoza LOG.WBS, potem pozostałe – np. cykl PARENT): pod PROJORG, a bez niego –
        // „Bez kategorii w WBS”.
        var outside = elements
            .Where(e => !placed.Contains(e))
            .OrderBy(e => e.Parent.Length > 0 && byKey.ContainsKey(MappingKeys.Key(e.Parent)) ? 1 : 0)
            .ThenBy(e => e.WbsElement, StringComparer.Ordinal)
            .ToList();
        foreach (var element in outside)
        {
            if (placed.Contains(element))
                continue;
            var node = Subtree(element, children, placed);
            if (rootsByProjOrg.TryGetValue(MappingKeys.Key(element.ProjOrg), out var projOrg))
                projOrg.Children.Add(node);
            else
                noCategory.Children.Add(node);
        }

        var result = categories.Values.Select(Sorted).ToList();
        if (noCategory.Children.Count > 0)
            result.Add(noCategory);
        return result;
    }

    private static P1sNode Subtree(P1sElement element, Dictionary<string, List<P1sElement>> children, HashSet<P1sElement> placed)
    {
        var node = new P1sNode(element.Label, element);
        placed.Add(element);
        if (children.TryGetValue(MappingKeys.Key(element.Pspnr), out var list))
        {
            foreach (var child in list.Where(c => !placed.Contains(c)))
                node.Children.Add(Subtree(child, children, placed));
        }
        return node;
    }

    private static P1sNode Child(SortedDictionary<string, P1sNode> level, string title)
    {
        if (!level.TryGetValue(title, out var node))
            level[title] = node = new P1sNode(title);
        return node;
    }

    private static P1sNode Child(P1sNode parent, string title)
    {
        var node = parent.Children.FirstOrDefault(c => c.Element is null && c.Title == title);
        if (node is null)
            parent.Children.Add(node = new P1sNode(title));
        return node;
    }

    /// <summary>Poziomy kategorii alfabetycznie (elementy już są w kolejności kodów WBS).</summary>
    private static P1sNode Sorted(P1sNode node)
    {
        if (node.Element is null)
        {
            var ordered = node.Children.OrderBy(c => c.Element is null ? 0 : 1).ThenBy(c => c.Element is null ? c.Title : "", StringComparer.CurrentCulture).ToList();
            node.Children.Clear();
            node.Children.AddRange(ordered.Select(Sorted));
        }
        return node;
    }
}
