using System.Globalization;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Baza analityczna (docs/funkcjonalnosc.md, F01, krok 5): węzeł nakładki → WP (słownik „WP i CAM” przez Legacy WBS:
/// element P1S trafia do węzła z najdłuższym pasującym Legacy WBS, a przy równych – do najgłębszego) → budżet i daty
/// („Harmonogram i budżet”). Sumy węzła obejmują poddrzewo; WP liczony raz.
/// </summary>
public static class AnalyticBaseBuilder
{
    public static AnalyticBase Build(PoTree tree, IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule)
    {
        var flat = tree.Flatten();
        var depth = flat.ToDictionary(x => x.Node.Key, x => x.Depth);
        var budgets = schedule.Where(r => r["WP"] is not null).GroupBy(r => r["WP"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var wpCams = wpCam.Where(r => r["WP"] is not null).GroupBy(r => r["WP"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()["CAM"] ?? "", StringComparer.OrdinalIgnoreCase);

        // WP podpięte bezpośrednio do węzła (przez element P1S).
        var own = new Dictionary<long, HashSet<string>>();
        var legacy = tree.Nodes.Where(n => n.LegacyWbs is not null).ToList();
        foreach (var row in wpCam)
        {
            if (row["Element P1S"] is not { } element || row["WP"] is not { } wp)
                continue;
            var scope = ProjectDictionaryContext.ScopeOf(legacy.Select(n => n.LegacyWbs!), element);
            var node = scope is null ? null : legacy.Where(n => n.LegacyWbs!.Equals(scope, StringComparison.OrdinalIgnoreCase)).MaxBy(n => depth.GetValueOrDefault(n.Key));
            if (node is null)
                continue;   // element poza zakresem – błąd walidacji słownika „WP i CAM”
            (own.TryGetValue(node.Key, out var set) ? set : own[node.Key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(wp);
        }

        var rows = new List<AnalyticRow>(flat.Count);
        var withoutWp = new List<string>();
        foreach (var (node, d) in flat)
        {
            var wps = tree.Subtree(node.Key).SelectMany(n => own.GetValueOrDefault(n.Key) ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
            string? gap = null;
            if (!node.IsVirtual && wps.Count == 0)
            {
                gap = node.LegacyWbs is null ? "brak Legacy WBS – bez WP" : "element nakładki bez WP";
                withoutWp.Add(node.WbsElement ?? node.Name);
            }
            else if (wps.Any(w => !HasBudget(budgets.GetValueOrDefault(w))))
                gap = "WP bez budżetu";
            var (hours, material, start, finish) = Totals(wps, budgets);
            rows.Add(new AnalyticRow(d, node.Name, node.WbsElement, node.LegacyWbs, node.IsVirtual, wps,
                wps.Select(w => wpCams.GetValueOrDefault(w) ?? "").Where(c => c.Length > 0).Distinct().Order().ToList(),
                hours, material, start, finish, gap));
        }

        var allWps = wpCams.Keys.Order().ToList();
        var byCam = wpCams.GroupBy(p => p.Value)
            .Select(g =>
            {
                var (hours, material, start, finish) = Totals(g.Select(p => p.Key).ToList(), budgets);
                return new CamSummary(g.Key, g.Count(), hours, material, start, finish);
            })
            .OrderBy(c => c.Cam)
            .ToList();
        var total = Totals(allWps, budgets);
        var withoutBudget = allWps.Where(w => !HasBudget(budgets.GetValueOrDefault(w))).ToList();
        return new AnalyticBase(rows, byCam, withoutWp, withoutBudget, allWps.Count, total.Hours, total.Material);
    }

    private static bool HasBudget(DictRow? row) => row is not null && (row["BAC HOURS"] is not null || row["BAC MATERIAL"] is not null);

    private static (decimal Hours, decimal Material, string? Start, string? Finish) Totals(IReadOnlyList<string> wps, IReadOnlyDictionary<string, DictRow> budgets)
    {
        decimal hours = 0, material = 0;
        string? start = null, finish = null;
        foreach (var wp in wps)
        {
            if (budgets.GetValueOrDefault(wp) is not { } b)
                continue;
            hours += Dec(b["BAC HOURS"]);
            material += Dec(b["BAC MATERIAL"]);
            if (b["Planowany Start"] is { } s && (start is null || string.CompareOrdinal(s, start) < 0))
                start = s;
            if (b["Planowany Koniec"] is { } f && (finish is null || string.CompareOrdinal(f, finish) > 0))
                finish = f;
        }
        return (hours, material, start, finish);
    }

    private static decimal Dec(string? canonical) =>
        decimal.TryParse(canonical, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
