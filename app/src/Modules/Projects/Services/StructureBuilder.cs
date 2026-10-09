using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Struktura projektu (docs/performance-objectives.md, rozdz. 4.2): drzewo nakładki, a pod każdym węzłem – elementy
/// P1S rozwinięte z LOG.WBS spod jego kodów P1S (Legacy WBS; cel mapowania CES ↔ P1S, gdy jest inny – osobny wiersz).
/// Rozwinięcie zatrzymuje się na kodzie innego węzła nakładki (ten element jest pod swoim węzłem), każdy element P1S
/// występuje raz. Elementy „WP i CAM” spoza LOG.WBS (np. bez PZLPROD) trafiają pod wiersz o najdłuższym pasującym
/// kodzie (kod + kropka), a bez niego – do grupy „spoza struktury”. WP, CAM i budżet z słowników projektu; sumy
/// wiersza obejmują poddrzewo (WP liczony raz).
/// Koszt rzeczywisty (costs – ACWP po elemencie CES z ostatniego importu ACTUALS): koszt elementu nakładki trafia do
/// WP na jego kodzie P1S, a bez niego – do jedynego WP pod elementem; brak WP albo kilka WP – koszt bez przypisania
/// (w brakach wiersza; reguła rozdziału – O45). Koszt elementu CES, którego nie ma w nakładce – „spoza nakładki”.
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
        public decimal OwnCost { get; set; }
        public ProductionValues? OwnProduction { get; set; }
        public ProductionSum Production { get; set; }
        public decimal Cost { get; set; }
        public string? CostGap { get; set; }
    }

    /// <summary>Suma wartości produkcyjnych (null – żaden element nie ma danych).</summary>
    private readonly record struct ProductionSum(decimal? Ac, decimal? Bac, decimal? Ev, decimal? Material)
    {
        public ProductionSum Add(ProductionValues? v) => v is null ? this
            : new(Plus(Ac, v.AcHours), Plus(Bac, v.BacHours), Plus(Ev, v.EvHours), Plus(Material, v.ActualMaterial));

        public ProductionSum Add(ProductionSum v) => new(Plus(Ac, v.Ac), Plus(Bac, v.Bac), Plus(Ev, v.Ev), Plus(Material, v.Material));

        private static decimal? Plus(decimal? a, decimal? b) => a is null ? b : b is null ? a : a + b;
    }

    /// <param name="production">Wartości produkcyjne z PZLPROD (Operational EV, materiały) – null: kolumny puste.</param>
    /// <param name="statusDate">Dzień stanu dla PV (domyślnie dziś).</param>
    public static ProjectStructure Build(PoTree tree, IReadOnlyDictionary<long, MappingResult> mapping, IReadOnlyList<P1sElement>? p1s,
        IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule, IReadOnlyDictionary<string, decimal>? costs = null,
        ProductionData? production = null, DateOnly? statusDate = null)
    {
        var today = statusDate ?? DateOnly.FromDateTime(DateTime.Today);
        var productionOf = (production?.ByPspnr ?? new Dictionary<string, ProductionValues>())
            .GroupBy(p => MappingKeys.Key(p.Key)).ToDictionary(g => g.Key, g => g.First().Value);
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
        // Dzieci węzłów nakładki jednym przebiegiem (PoTree.Children przegląda wszystkie węzły przy każdym wywołaniu).
        var nodesOf = tree.Nodes.ToLookup(n => n.ParentKey);
        IEnumerable<PoNode> ChildrenOf(long? key) => nodesOf[key].OrderBy(n => n.SortOrder);

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
            foreach (var child in ChildrenOf(node.Key))
                item.Children.Add(Objective(child));
            if (item.P1s is not null)
                Expand(item);
            foreach (var extra in nodeCodes.Skip(1).Where(c => !primary.Contains(MappingKeys.Key(c)) && !placed.Contains(MappingKeys.Key(c))))
                item.Children.Add(P1sItem(extra, "cel mapowania CES ↔ P1S"));
            return item;
        }

        var roots = ChildrenOf(null).Select(Objective).ToList();

        // Przypisania „WP i CAM” do elementów, których nie ma w drzewie: pod najdłuższy pasujący kod albo poza strukturą.
        Item? outside = null;
        foreach (var (key, row) in assignments.OrderBy(a => a.Value["Element P1S"], StringComparer.OrdinalIgnoreCase))
        {
            if (withCode.ContainsKey(key))
                continue;
            var element = row["Element P1S"]!;
            var parent = LongestPrefix(element, withCode);
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

        // Koszt rzeczywisty: element nakładki → WP na jego kodzie P1S albo jedyny WP pod nim; suma poddrzewa nakładki.
        var costOf = costs is null ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            : costs.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        var wpCost = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var withoutWp = 0m;
        decimal Assign(Item item)
        {
            if (item.Kind == GridRowKind.Objective && item.WbsElement is { } wbs && costOf.TryGetValue(wbs, out var amount) && used.Add(wbs) && amount != 0)
            {
                item.OwnCost = amount;
                var own = item.P1s is null ? null : assignments.GetValueOrDefault(MappingKeys.Key(item.P1s))?["WP"];
                var target = own ?? (item.Wps.Count == 1 ? item.Wps.First() : null);
                if (target is not null)
                    wpCost[target] = wpCost.GetValueOrDefault(target) + amount;
                else
                {
                    withoutWp += amount;
                    item.CostGap = item.Wps.Count == 0
                        ? $"koszt bez WP ({PolishNumber.ToDisplay(amount)})"
                        : $"koszt niejednoznaczny ({PolishNumber.ToDisplay(amount)}) – {item.Wps.Count} WP pod elementem, reguła rozdziału O45";
                }
            }
            item.Cost = item.OwnCost + item.Children.Sum(Assign);
            return item.Cost;
        }
        foreach (var root in roots)
            Assign(root);

        // Produkcja (PZLPROD): element P1S wiersza → PSPNR → wartości; sumy poddrzewa (każdy element P1S jest raz w drzewie).
        // WP: wartości poddrzewa wiersza z własnym WP – podstawa PV Hours, EV Cost.
        var wpProduction = new Dictionary<string, ProductionSum>(StringComparer.OrdinalIgnoreCase);
        ProductionSum Produce(Item item)
        {
            if (item.P1s is not null && byCode.GetValueOrDefault(MappingKeys.Key(item.P1s)) is { } element)
                item.OwnProduction = productionOf.GetValueOrDefault(MappingKeys.Key(element.Pspnr));
            item.Production = item.Children.Aggregate(new ProductionSum().Add(item.OwnProduction), (sum, child) => sum.Add(Produce(child)));
            if (item.P1s is not null && assignments.GetValueOrDefault(MappingKeys.Key(item.P1s))?["WP"] is { } ownWp)
                wpProduction[ownWp] = item.Production;
            return item.Production;
        }
        foreach (var root in roots)
            Produce(root);
        decimal? Elapsed(string wp) => budgets.GetValueOrDefault(wp) is { } b ? EarnedValue.Elapsed(b["Baseline Start"], b["Baseline Koniec"], today) : null;
        decimal? Sum(IEnumerable<string> wps, Func<string, decimal?> value) =>
            wps.Select(value).Aggregate((decimal?)null, (sum, v) => v is null ? sum : (sum ?? 0) + v);
        decimal? PvHours(string wp) => wpProduction.GetValueOrDefault(wp).Bac * Elapsed(wp);
        decimal? BacCost(string wp) => budgets.GetValueOrDefault(wp) is { } b && decimal.TryParse(b["BAC"], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        decimal? PvCost(string wp) => BacCost(wp) * Elapsed(wp);
        decimal? EvCost(string wp) => wpProduction.GetValueOrDefault(wp) is { Bac: > 0 } p && p.Ev is { } ev ? BacCost(wp) * Math.Min(1, ev / p.Bac.Value) : null;

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
            if (assignment?["WP"] is not null && assignment["CAM"] is null)
                gap = gap is null ? "WP bez CAM" : $"{gap}; WP bez CAM";
            if (item.CostGap is { } costGap)
                gap = gap is null ? costGap : $"{gap}; {costGap}";
            var acwp = item.Kind == GridRowKind.Objective ? item.Cost : wps.Sum(w => wpCost.GetValueOrDefault(w));
            rows.Add(new StructureRow(item.Id, parentId, depth, item.Kind, item.NodeKey, item.IsVirtual, item.Name, item.WbsElement, item.P1s,
                item.Note, item.IsGreyed, assignment?["WP"], assignment?["CAM"], assignment?["Cost Category"], wps, hours, material, start, finish,
                gap, item.Children.Count > 0, acwp, AnalyticBaseBuilder.Bac(wps, budgets), null, null,
                item.Production.Bac, Sum(wps, PvHours), item.Production.Ev, item.Production.Ac, item.Production.Material, Sum(wps, PvCost), Sum(wps, EvCost)));
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
            all.Count(w => !AnalyticBaseBuilder.HasBudget(budgets.GetValueOrDefault(w))),
            roots.Sum(r => r.Cost),
            withoutWp,
            costOf.Where(c => !used.Contains(c.Key)).Sum(c => c.Value),
            costs is not null,
            AnalyticBaseBuilder.Bac(all, budgets));
        return new ProjectStructure(rows, summary);
    }

    /// <summary>Wiersz o najdłuższym kodzie P1S, który jest prefiksem elementu do kropki (A.B.C → A.B, potem A).</summary>
    private static Item? LongestPrefix(string element, IReadOnlyDictionary<string, Item> withCode)
    {
        for (var dot = element.LastIndexOf('.'); dot > 0; dot = element.LastIndexOf('.', dot - 1))
        {
            if (withCode.TryGetValue(MappingKeys.Key(element[..dot]), out var item) && item.P1s is not null)
                return item;
        }
        return null;
    }
}
