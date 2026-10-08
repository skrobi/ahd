using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Struktura projektu (docs/performance-objectives.md, rozdz. 4.2): drzewo nakładki, a pod każdym węzłem – elementy
/// P1S rozwinięte z LOG.WBS spod jego kodów P1S (Legacy WBS; cel mapowania CES ↔ P1S, gdy jest inny – osobny wiersz).
/// Rozwinięcie zatrzymuje się na kodzie innego węzła nakładki (ten element jest pod swoim węzłem), każdy element P1S
/// występuje raz. Elementy „WP i CAM” spoza LOG.WBS (np. bez PZLPROD) trafiają pod wiersz o najdłuższym pasującym
/// kodzie (kod + kropka), a bez niego – do grupy „spoza struktury”. WP, CAM i budżet z słowników projektu; sumy
/// wiersza obejmują poddrzewo (WP liczony raz).
/// </summary>
public static class StructureBuilder
{
    public const string OutsideId = "p1s:#poza";

    private sealed class Item
    {
        public required string Id { get; init; }
        public required GridRowKind Kind { get; init; }
        public long? NodeKey { get; init; }
        public bool IsVirtual { get; init; }
        public bool HasCode { get; init; } = true;
        public required string Name { get; init; }
        public string? WbsElement { get; init; }
        public string? P1s { get; init; }
        public string? Note { get; init; }
        public bool IsGreyed { get; init; }
        public List<Item> Children { get; } = [];
        public HashSet<string> Wps { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static ProjectStructure Build(PoTree tree, IReadOnlyDictionary<long, MappingResult> mapping, IReadOnlyList<P1sElement>? p1s,
        IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule)
    {
        var budgets = schedule.Where(r => r["WP"] is not null).GroupBy(r => r["WP"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var assignments = wpCam.Where(r => r["Element P1S"] is not null).GroupBy(r => MappingKeys.Key(r["Element P1S"]))
            .ToDictionary(g => g.Key, g => g.First());
        var byCode = (p1s ?? []).GroupBy(e => MappingKeys.Key(e.WbsElement)).ToDictionary(g => g.Key, g => g.First());
        var children = (p1s ?? []).Where(e => e.Parent.Length > 0).GroupBy(e => MappingKeys.Key(e.Parent))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.WbsElement, StringComparer.OrdinalIgnoreCase).ToList());
        var codes = tree.Nodes.Where(n => !n.IsVirtual).ToDictionary(n => n.Key, n => ObjectivesMapping.CodesOf(n, mapping));
        // Kody węzłów nakładki: na nich zatrzymuje się rozwinięcie innego węzła.
        var anchors = codes.Values.SelectMany(c => c).Select(MappingKeys.Key).ToHashSet();
        var primary = codes.Values.Where(c => c.Count > 0).Select(c => MappingKeys.Key(c[0])).ToHashSet();
        var placed = new HashSet<string>();
        var withCode = new Dictionary<string, Item>();

        Item P1sItem(string code, string? note)
        {
            var element = byCode.GetValueOrDefault(MappingKeys.Key(code));
            var item = new Item
            {
                Id = $"p1s:{MappingKeys.Key(code)}", Kind = GridRowKind.P1s, Name = element?.Description is { Length: > 0 } d ? d : code,
                P1s = element?.WbsElement ?? code, Note = note, IsGreyed = element?.IsGreyed ?? false,
            };
            placed.Add(MappingKeys.Key(code));
            withCode.TryAdd(MappingKeys.Key(code), item);
            Expand(item);
            return item;
        }

        void Expand(Item item)
        {
            if (byCode.GetValueOrDefault(MappingKeys.Key(item.P1s)) is not { } element)
                return;
            foreach (var child in children.GetValueOrDefault(MappingKeys.Key(element.Pspnr)) ?? [])
            {
                var key = MappingKeys.Key(child.WbsElement);
                if (!anchors.Contains(key) && !placed.Contains(key))
                    item.Children.Add(P1sItem(child.WbsElement, null));
            }
        }

        // Kody węzłów (pierwszy kod – wiersz węzła) rezerwowane przed rozwinięciem, żeby nie trafiły pod inny węzeł.
        foreach (var key in primary)
            placed.Add(key);

        Item Objective(PoNode node)
        {
            var nodeCodes = codes.GetValueOrDefault(node.Key) ?? [];
            var item = new Item
            {
                Id = $"po:{node.Key}", Kind = GridRowKind.Objective, NodeKey = node.Key, IsVirtual = node.IsVirtual, HasCode = nodeCodes.Count > 0,
                Name = node.Name, WbsElement = node.WbsElement, P1s = nodeCodes.Count > 0 ? nodeCodes[0] : null,
                IsGreyed = nodeCodes.Count > 0 && byCode.GetValueOrDefault(MappingKeys.Key(nodeCodes[0])) is { IsGreyed: true },
            };
            if (item.P1s is not null)
                withCode.TryAdd(MappingKeys.Key(item.P1s), item);
            foreach (var child in tree.Children(node.Key))
                item.Children.Add(Objective(child));
            if (item.P1s is not null)
                Expand(item);
            foreach (var extra in nodeCodes.Skip(1).Where(c => !primary.Contains(MappingKeys.Key(c)) && !placed.Contains(MappingKeys.Key(c))))
                item.Children.Add(P1sItem(extra, "cel mapowania CES ↔ P1S"));
            return item;
        }

        var roots = tree.Children(null).Select(Objective).ToList();

        // Przypisania „WP i CAM” do elementów, których nie ma w drzewie: pod najdłuższy pasujący kod albo poza strukturą.
        Item? outside = null;
        foreach (var (key, row) in assignments.OrderBy(a => a.Value["Element P1S"], StringComparer.OrdinalIgnoreCase))
        {
            if (withCode.ContainsKey(key))
                continue;
            var element = row["Element P1S"]!;
            var parent = withCode.Values.Where(i => i.P1s is not null && element.StartsWith(i.P1s + ".", StringComparison.OrdinalIgnoreCase))
                .MaxBy(i => i.P1s!.Length);
            if (parent is null)
            {
                outside ??= new Item
                {
                    Id = OutsideId, Kind = GridRowKind.P1s, HasCode = false, Name = "Elementy P1S spoza struktury",
                    Note = "przypisania „WP i CAM” do elementów spoza nakładki i LOG.WBS",
                };
                parent = outside;
            }
            var item = new Item { Id = $"p1s:{key}", Kind = GridRowKind.P1s, Name = element, P1s = element, Note = "spoza LOG.WBS" };
            withCode[key] = item;
            parent.Children.Add(item);
        }
        if (outside is not null)
            roots.Add(outside);

        // Sumy poddrzewa (WP liczony raz), potem wiersze w kolejności drzewa.
        void Collect(Item item)
        {
            if (item.P1s is not null && assignments.GetValueOrDefault(MappingKeys.Key(item.P1s))?["WP"] is { } own)
                item.Wps.Add(own);
            foreach (var child in item.Children)
            {
                Collect(child);
                item.Wps.UnionWith(child.Wps);
            }
        }
        foreach (var root in roots)
            Collect(root);

        var rows = new List<StructureRow>();
        void Emit(Item item, string? parentId, int depth)
        {
            var assignment = item.P1s is null ? null : assignments.GetValueOrDefault(MappingKeys.Key(item.P1s));
            var wps = item.Wps.Order(StringComparer.OrdinalIgnoreCase).ToList();
            var (hours, material, start, finish) = AnalyticBaseBuilder.Totals(wps, budgets);
            string? gap = null;
            if (item.Kind == GridRowKind.Objective && !item.IsVirtual && wps.Count == 0)
                gap = item.HasCode ? "element nakładki bez WP" : "brak kodu P1S (Legacy WBS ani mapowania) – bez WP";
            else if (wps.Any(w => !AnalyticBaseBuilder.HasBudget(budgets.GetValueOrDefault(w))))
                gap = "WP bez budżetu";
            rows.Add(new StructureRow(item.Id, parentId, depth, item.Kind, item.NodeKey, item.IsVirtual, item.Name, item.WbsElement, item.P1s,
                item.Note, item.IsGreyed, assignment?["WP"], assignment?["CAM"], assignment?["Cost Category"], wps, hours, material, start, finish,
                gap, item.Children.Count > 0));
            foreach (var child in item.Children)
                Emit(child, item.Id, depth + 1);
        }
        foreach (var root in roots)
            Emit(root, null, 0);

        var all = roots.SelectMany(r => r.Wps).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var camOf = wpCam.Where(r => r["WP"] is not null).GroupBy(r => r["WP"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()["CAM"], StringComparer.OrdinalIgnoreCase);
        var total = AnalyticBaseBuilder.Totals(all, budgets);
        var summary = new StructureSummary(
            all.Count,
            all.Select(w => camOf.GetValueOrDefault(w)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            total.Hours, total.Material, total.Start, total.Finish,
            rows.Count(r => r is { Kind: GridRowKind.Objective, IsVirtual: false } && r.Wps.Count == 0),
            all.Count(w => !AnalyticBaseBuilder.HasBudget(budgets.GetValueOrDefault(w))));
        return new ProjectStructure(rows, summary);
    }
}
