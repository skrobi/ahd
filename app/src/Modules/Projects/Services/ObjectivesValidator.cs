using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Kontrola nakładki przed zapisem: co najmniej jeden element CES (F02), element CES z kluczem i bez powtórzeń,
/// węzeł z nazwą, element CES w co najwyżej jednej nakładce (O46 – ERROR z kodem projektu, który go ma).
/// </summary>
public static class ObjectivesValidator
{
    public static List<Issue> Validate(PoTree tree, IReadOnlyDictionary<string, string> wbsOwners)
    {
        var issues = new List<Issue>();
        if (tree.ElementCount == 0)
            issues.Add(Issue.Error("Nakładka nie zawiera żadnego elementu CES – wczytaj eksport z SAP albo dodaj elementy", "Performance Objectives"));

        foreach (var node in tree.Nodes)
        {
            var at = node.WbsElement ?? node.Name;
            if (node.Name.Trim().Length == 0)
                issues.Add(Issue.Error("Węzeł bez nazwy", at));
            if (node.IsVirtual)
                continue;
            if (string.IsNullOrWhiteSpace(node.WbsElement))
                issues.Add(Issue.Error("Element CES bez WBS element", node.Name));
            else if (wbsOwners.TryGetValue(node.WbsElement, out var owner))
                issues.Add(Issue.Error($"Element CES należy już do nakładki projektu {owner}", node.WbsElement));
        }
        foreach (var group in tree.Nodes.Where(n => !n.IsVirtual && !string.IsNullOrWhiteSpace(n.WbsElement))
                     .GroupBy(n => n.WbsElement!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(Issue.Error($"Element CES występuje w nakładce {group.Count()} razy", group.Key));

        var withoutLegacy = tree.Nodes.Count(n => !n.IsVirtual && n.LegacyWbs is null);
        if (tree.ElementCount > 0 && withoutLegacy == tree.ElementCount)
            issues.Add(Issue.Warning("Żaden element nie ma Legacy WBS – słownika „WP i CAM” nie da się powiązać z nakładką", "Legacy WBS"));
        return issues;
    }
}
