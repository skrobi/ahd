using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Struktura projektu (docs/performance-objectives.md, rozdz. 4.2): drzewo nakładki, a pod każdym węzłem – elementy
/// P1S rozwinięte z LOG.WBS spod jego kodów P1S (Legacy WBS).
/// Rozwinięcie zatrzymuje się na kodzie innego węzła nakładki (ten element jest pod swoim węzłem), każdy element P1S
/// występuje raz. Elementy „WP i CAM” spoza LOG.WBS (np. bez PZLPROD) trafiają pod wiersz o najdłuższym pasującym
/// kodzie (kod + kropka), a bez niego – do grupy „spoza struktury”. WP, CAM i budżet z słowników projektu; sumy
/// wiersza obejmują poddrzewo (WP liczony raz).
/// Koszt rzeczywisty (costs – ACWP po elemencie CES z ostatniego importu ACTUALS): koszt elementu nakładki trafia do
/// WP na jego kodzie P1S, a bez niego – do jedynego WP pod elementem; brak WP albo kilka WP – koszt bez przypisania
/// (w brakach wiersza; reguła rozdziału – O45). Koszt elementu CES, którego nie ma w nakładce – „spoza nakładki”.
/// Godziny rzeczywiste (dlh – Actual DLH, Total Quantity po elemencie CES z tego samego odczytu ACTUALS) – przypisywane
/// do WP jak koszt.
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
        public bool SplitError { get; init; }
        public ProductionValues? OwnProduction { get; set; }
        public ProductionSum Production { get; set; }
        // Koszty rzeczywiste elementu CES (Own) i poddrzewa nakładki (Sum): ACWP, Actual DLH, ACWP Labor, ACWP Material.
        public decimal[] Own { get; } = new decimal[Measures];
        public decimal[] Sum { get; } = new decimal[Measures];
        public string? CostGap { get; set; }
        public string? PerformanceObligation { get; init; }
    }

    private const int Measures = 4, CostMeasure = 0, DlhMeasure = 1, LaborMeasure = 2, MaterialMeasure = 3;

    /// <summary>
    /// Suma wartości produkcyjnych (null – żaden element nie ma danych); daty rzeczywiste: start – najwcześniejszy, koniec –
    /// najpóźniejszy, gdy żaden element z godzinami nie jest otwarty (Open – element bez daty końca).
    /// </summary>
    private readonly record struct ProductionSum(decimal? Ac, decimal? Bac, decimal? Ev, decimal? Material, DateOnly? Start, DateOnly? Finish, bool Open,
        decimal? Etc = null, decimal? Value = null, decimal? Fee = null, int? Parts = null, int? PartsDelivered = null, int? NextYear = null,
        IReadOnlyDictionary<int, decimal>? Undelivered = null)
    {
        public ProductionSum Add(ProductionValues? v) => v is null ? this
            : new(Plus(Ac, v.AcHours), Plus(Bac, v.BacHours), Plus(Ev, v.EvHours), Plus(Material, v.ActualMaterial), Min(Start, v.ActualStart),
                Max(Finish, v.ActualFinish), Open || (v.ActualFinish is null && (v.AcHours is not null || v.BacHours is not null)),
                Plus(Etc, v.EtcHours), Plus(Value, v.MaterialValue), Plus(Fee, v.MaterialFee), Plus(Parts, v.Parts), Plus(PartsDelivered, v.PartsDelivered),
                MinYear(NextYear, v.NextDeliveryYear), Plus(Undelivered, v.UndeliveredByYear));

        public ProductionSum Add(ProductionSum v) => new(Plus(Ac, v.Ac), Plus(Bac, v.Bac), Plus(Ev, v.Ev), Plus(Material, v.Material),
            Min(Start, v.Start), Max(Finish, v.Finish), Open || v.Open, Plus(Etc, v.Etc), Plus(Value, v.Value), Plus(Fee, v.Fee), Plus(Parts, v.Parts),
            Plus(PartsDelivered, v.PartsDelivered), MinYear(NextYear, v.NextYear), Plus(Undelivered, v.Undelivered));

        public DateOnly? ActualFinish => Open ? null : Finish;

        private static decimal? Plus(decimal? a, decimal? b) => a is null ? b : b is null ? a : a + b;

        private static int? Plus(int? a, int? b) => a is null ? b : b is null ? a : a + b;

        private static IReadOnlyDictionary<int, decimal>? Plus(IReadOnlyDictionary<int, decimal>? a, IReadOnlyDictionary<int, decimal>? b) =>
            a is null ? b : b is null ? a
            : a.Concat(b).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

        private static int? MinYear(int? a, int? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

        private static DateOnly? Min(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a < b ? a : b;

        private static DateOnly? Max(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : a > b ? a : b;
    }

    /// <param name="production">Wartości produkcyjne z PZLPROD (Operational EV, materiały) – null: kolumny puste.</param>
    /// <param name="statusDate">Dzień stanu dla PV (domyślnie dziś).</param>
    /// <param name="dlh">Actual DLH – godziny (Total Quantity) po elemencie CES z ostatniego importu ACTUALS; null – kolumna pusta.</param>
    public static ProjectStructure Build(PoTree tree, IReadOnlyList<P1sElement>? p1s,
        IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule, IReadOnlyDictionary<string, decimal>? costs = null,
        ProductionData? production = null, DateOnly? statusDate = null, IReadOnlyDictionary<string, decimal>? dlh = null,
        IReadOnlyDictionary<string, decimal>? labor = null, IReadOnlyDictionary<string, decimal>? material = null, EtcRates? rates = null)
    {
        var today = statusDate ?? DateOnly.FromDateTime(DateTime.Today);
        // Waluta projektu (rates.Currency): materiały z vAPD (USD) przeliczone kursem bieżącym (brak kursu – puste);
        // kwoty „Harmonogram i budżet” wpisane w innej walucie (kolumna Waluta) – przeliczone, brak kursu – puste i brak w wierszu.
        var materialFactor = rates is null ? 1 : rates.MaterialFactor;
        var productionOf = (production?.ByPspnr ?? new Dictionary<string, ProductionValues>())
            .GroupBy(p => MappingKeys.Key(p.Key)).ToDictionary(g => g.Key, g => g.First().Value with
            {
                ActualMaterial = g.First().Value.ActualMaterial * materialFactor,
                MaterialValue = g.First().Value.MaterialValue * materialFactor,
                MaterialFee = g.First().Value.MaterialFee * materialFactor,
                UndeliveredByYear = materialFactor is { } factor ? g.First().Value.UndeliveredByYear?.ToDictionary(x => x.Key, x => x.Value * factor) : null,
            });
        var currencyGaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DictRow InProjectCurrency(DictRow row)
        {
            var converted = ProjectDictionaries.InProjectCurrency(row, rates, out var gap);
            if (gap is not null)
                currencyGaps[row["WP"]!] = gap;
            return converted;
        }
        var budgets = schedule.Where(r => r["WP"] is not null).GroupBy(r => r["WP"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => InProjectCurrency(g.First()), StringComparer.OrdinalIgnoreCase);
        var assignments = wpCam.Where(r => r["Element P1S"] is not null).GroupBy(r => MappingKeys.Key(r["Element P1S"]))
            .ToDictionary(g => g.Key, g => g.First());
        var byCode = (p1s ?? []).GroupBy(e => MappingKeys.Key(e.WbsElement)).ToDictionary(g => g.Key, g => g.First());
        var children = (p1s ?? []).Where(e => e.Parent.Length > 0).GroupBy(e => MappingKeys.Key(e.Parent))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.WbsElement, StringComparer.OrdinalIgnoreCase).ToList());
        // Kody węzłów: element CES – Legacy WBS; węzeł wirtualny – Legacy WBS (wydzielenie ELEMENT_P1S.NAZWA.CPLGR).
        var codes = tree.Nodes.Where(n => !n.IsVirtual || !string.IsNullOrWhiteSpace(n.LegacyWbs))
            .ToDictionary(n => n.Key, n => ObjectivesMapping.CodesOf(n));
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
            var split = node.IsVirtual && nodeCodes.Count > 0 ? ProductionSplit.Parse(nodeCodes[0], byCode) : null;
            var item = new Item
            {
                Note = split is not null ? ProductionSplit.Describe(split)
                    : node.IsVirtual && nodeCodes.Count > 0 && byCode.Count > 0 && !byCode.ContainsKey(MappingKeys.Key(nodeCodes[0]))
                        ? $"kod {nodeCodes[0]} – wydzielenie zapisuje się jako ELEMENT_P1S.NAZWA.CPLGR (element z LOG.WBS), np. AC-R39.1.18.03.25.PAINT.W20"
                        : null,
                SplitError = node.IsVirtual && nodeCodes.Count > 0 && split is null && byCode.Count > 0 && !byCode.ContainsKey(MappingKeys.Key(nodeCodes[0])),
                Id = $"po:{node.Key}", Kind = GridRowKind.Objective, NodeKey = node.Key, IsVirtual = node.IsVirtual, HasCode = nodeCodes.Count > 0,
                Name = node.Name, WbsElement = node.WbsElement, P1s = nodeCodes.Count > 0 ? nodeCodes[0] : null,
                PerformanceObligation = node.PerformanceObligation,
                IsGreyed = nodeCodes.Count > 0 && byCode.GetValueOrDefault(MappingKeys.Key(nodeCodes[0])) is { IsGreyed: true },
            };
            if (item.P1s is not null)
                withCode.TryAdd(MappingKeys.Key(item.P1s), item);
            foreach (var child in ChildrenOf(node.Key))
                item.Children.Add(Objective(child));
            if (item.P1s is not null)
                Expand(item);
            return item;
        }

        var roots = ChildrenOf(null).Select(Objective).ToList();

        Item? outside = null;
        Item Outside() => outside ??= new Item
        {
            Id = OutsideId, Kind = GridRowKind.P1s, HasCode = false, Name = "Elementy P1S spoza struktury",
            Note = "przypisania „WP i CAM” do elementów spoza nakładki i LOG.WBS",
        };

        // Przypisania „WP i CAM” do elementów, których nie ma w drzewie: pod najdłuższy pasujący kod albo poza strukturą.
        foreach (var (key, row) in assignments.OrderBy(a => a.Value["Element P1S"], StringComparer.OrdinalIgnoreCase))
        {
            if (withCode.ContainsKey(key))
                continue;
            var element = row["Element P1S"]!;
            var parent = LongestPrefix(element, withCode);
            parent ??= Outside();
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

        // Koszt rzeczywisty (ACWP, Actual DLH, ACWP Labor / Material): element nakładki → WP na jego kodzie P1S albo jedyny WP pod
        // nim; suma poddrzewa nakładki.
        static Dictionary<string, decimal> Ci(IReadOnlyDictionary<string, decimal>? values) => values is null
            ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            : values.ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        var costOf = Ci(costs);
        Dictionary<string, decimal>[] measures = [costOf, Ci(dlh), Ci(labor), Ci(material)];
        var wpActuals = new Dictionary<string, decimal[]>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var withoutWp = 0m;
        void Assign(Item item)
        {
            if (item.Kind == GridRowKind.Objective && item.WbsElement is { } wbs && measures.Any(m => m.ContainsKey(wbs)) && used.Add(wbs))
            {
                for (var m = 0; m < Measures; m++)
                    item.Own[m] = measures[m].GetValueOrDefault(wbs);
                var amount = item.Own[CostMeasure];
                var own = item.P1s is null ? null : assignments.GetValueOrDefault(MappingKeys.Key(item.P1s))?["WP"];
                var target = own ?? (item.Wps.Count == 1 ? item.Wps.First() : null);
                if (target is not null)
                {
                    if (!wpActuals.TryGetValue(target, out var sums))
                        wpActuals[target] = sums = new decimal[Measures];
                    for (var m = 0; m < Measures; m++)
                        sums[m] += item.Own[m];
                }
                else if (amount != 0)
                {
                    withoutWp += amount;
                    item.CostGap = item.Wps.Count == 0
                        ? $"koszt bez WP ({PolishNumber.ToDisplay(Math.Round(amount, 2))})"
                        : $"koszt niejednoznaczny ({PolishNumber.ToDisplay(Math.Round(amount, 2))}) – {item.Wps.Count} WP pod elementem, reguła rozdziału O45";
                }
            }
            foreach (var child in item.Children)
                Assign(child);
            for (var m = 0; m < Measures; m++)
                item.Sum[m] = item.Own[m] + item.Children.Sum(c => c.Sum[m]);
        }
        foreach (var root in roots)
            Assign(root);

        // Produkcja (PZLPROD): element P1S wiersza → PSPNR → wartości (wydzielenie – węzeł wirtualny z kodem
        // ELEMENT_P1S.NAZWA.CPLGR – własne wartości, odjęte od elementu); sumy poddrzewa (każdy element raz w drzewie).
        // WP: wartości elementów przypisane do najbliższego WP w górę drzewa (własnego albo przodka) – WP zagnieżdżony
        // (np. wydzielenie z własnym CAM) ma swoje godziny, WP nadrzędny – resztę; podstawa PV Hours, EV Cost.
        // Kilka węzłów z tym samym kodem – wartości elementu liczone raz, w najgłębszym (jak przypisanie WP do węzła).
        var wpProduction = new Dictionary<string, ProductionSum>(StringComparer.OrdinalIgnoreCase);
        var counted = new HashSet<string>();
        ProductionSum Produce(Item item, string? inheritedWp)
        {
            var wp = (item.P1s is null ? null : assignments.GetValueOrDefault(MappingKeys.Key(item.P1s))?["WP"]) ?? inheritedWp;
            var children = item.Children.Aggregate(new ProductionSum(), (sum, child) => sum.Add(Produce(child, wp)));
            // Kod elementu LOG.WBS (także w węźle wirtualnym, który zastępuje element w drzewie) – wartości elementu (PSPNR);
            // węzeł wirtualny z kodem wydzielenia – wartości wydzielenia.
            var key = item.P1s is not null && byCode.GetValueOrDefault(MappingKeys.Key(item.P1s)) is { } element ? MappingKeys.Key(element.Pspnr)
                : item is { IsVirtual: true, P1s: { } virtualCode } ? MappingKeys.Key(ProductionData.VirtualKey(MappingKeys.Key(virtualCode))) : null;
            if (key is not null && counted.Add(key))
                item.OwnProduction = productionOf.GetValueOrDefault(key);
            if (wp is not null)
                wpProduction[wp] = wpProduction.GetValueOrDefault(wp).Add(item.OwnProduction);
            item.Production = children.Add(item.OwnProduction);
            return item.Production;
        }
        foreach (var root in roots)
            Produce(root, null);
        decimal? Elapsed(string wp) => budgets.GetValueOrDefault(wp) is { } b ? EarnedValue.Elapsed(b["Baseline Start"], b["Baseline Koniec"], today) : null;
        decimal? Sum(IEnumerable<string> wps, Func<string, decimal?> value) =>
            wps.Select(value).Aggregate((decimal?)null, (sum, v) => v is null ? sum : (sum ?? 0) + v);
        decimal? PvHours(string wp) => wpProduction.GetValueOrDefault(wp).Bac * Elapsed(wp);
        decimal? BacCost(string wp) => budgets.GetValueOrDefault(wp) is { } b && decimal.TryParse(b["BAC"], System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        decimal? PvCost(string wp) => BacCost(wp) * Elapsed(wp);
        // Daty rzeczywiste WP: Actual Start / Actual Koniec z „Harmonogram i budżet” nadpisują daty z produkcji; bez nich –
        // daty z produkcji przypisane do WP. Wpisany start bez końca i bez daty końca z produkcji – WP trwa.
        DateOnly? Parse(string? text) =>
            DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        ProductionSum Dates(string wp)
        {
            var produced = wpProduction.GetValueOrDefault(wp);
            var budget = budgets.GetValueOrDefault(wp);
            var start = Parse(budget?["Actual Start"]);
            var finish = Parse(budget?["Actual Koniec"]);
            return new ProductionSum(null, null, null, null, start ?? produced.Start, finish ?? produced.Finish,
                finish is null && (produced.Open || (start is not null && produced.Finish is null)));
        }
        decimal? EvCost(string wp) => wpProduction.GetValueOrDefault(wp) is { Bac: > 0 } p && p.Ev is { } ev ? BacCost(wp) * Math.Min(1, ev / p.Bac.Value) : null;
        static decimal? Dec(string? text) => decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        // Do poniesienia (ETC): wpis w „Harmonogram i budżet” (ETC HOURS / ETC MATERIAL), a bez niego – z produkcji WP.
        decimal? EtcHours(string wp) => Dec(budgets.GetValueOrDefault(wp)?["ETC HOURS"]) ?? wpProduction.GetValueOrDefault(wp).Etc;
        decimal? MaterialEtc(string wp) => Dec(budgets.GetValueOrDefault(wp)?["ETC MATERIAL"])
            ?? (wpProduction.GetValueOrDefault(wp) is { Value: { } value } p ? value - (p.Material ?? 0) : null);
        var mpkOf = wpCam.Where(r => r["WP"] is not null && r["MPK"] is not null).GroupBy(r => r["WP"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()["MPK"]!, StringComparer.OrdinalIgnoreCase);
        // ETC Cost WP = ETC Hours × stawka MPK (Labor Rate × Overhead, PLN) × przelicznik pracy + Material ETC (już w walucie
        // projektu) – w walucie projektu; brak MPK, stawki albo kursu (także USD materiałów) – null (powód w brakach wiersza).
        decimal? EtcCost(string wp)
        {
            if (rates is null || !mpkOf.TryGetValue(wp, out var mpk) || !rates.RateByMpk.TryGetValue(mpk, out var rate) || rates.LaborFactor is not { } laborFactor
                || rates.MaterialFactor is null || currencyGaps.ContainsKey(wp))
                return null;
            return (EtcHours(wp) ?? 0) * rate * laborFactor + (MaterialEtc(wp) ?? 0);
        }
        string? EtcGap(string wp) => rates is null ? null
            : !mpkOf.TryGetValue(wp, out var mpk) ? "WP bez MPK"
            : !rates.RateByMpk.ContainsKey(mpk) ? $"brak stawki MPK {mpk} na {rates.Year} („Stawki wydziałów”)"
            : rates.Problem is { } problem ? $"ETC Cost: {problem}" : null;
        DateOnly? Forecast(IEnumerable<string> wps, string column, bool latest) =>
            wps.Select(w => Parse(budgets.GetValueOrDefault(w)?[column])).OfType<DateOnly>().DefaultIfEmpty().Aggregate((DateOnly?)null,
                (best, d) => d == default ? best : best is null || (latest ? d > best : d < best) ? d : best);

        var rows = new List<StructureRow>();
        void Emit(Item item, string? parentId, int depth)
        {
            var assignment = item.P1s is null ? null : assignments.GetValueOrDefault(MappingKeys.Key(item.P1s));
            var wps = item.Wps.Order(StringComparer.OrdinalIgnoreCase).ToList();
            // Wiersz z własnym WP pokazuje ten WP (budżet, daty, godziny przypisane do WP, PV / EV); bez własnego WP – sumy WP
            // i elementów poddrzewa.
            var ownWp = assignment?["WP"];
            List<string> shown = ownWp is not null ? [ownWp] : wps;
            var ops = ownWp is not null ? wpProduction.GetValueOrDefault(ownWp) : item.Production;
            // Daty rzeczywiste: wiersz z WP w poddrzewie – daty WP (wpisane albo z produkcji); bez WP – z produkcji poddrzewa.
            var dates = shown.Count > 0 ? shown.Aggregate(new ProductionSum(), (sum, w) => sum.Add(Dates(w))) : item.Production;
            var budgetWp = ownWp ?? (wps.Count == 1 ? wps[0] : null);
            var manualStart = budgetWp is not null && budgets.GetValueOrDefault(budgetWp)?["Actual Start"] is not null;
            var manualFinish = budgetWp is not null && budgets.GetValueOrDefault(budgetWp)?["Actual Koniec"] is not null;
            var (hours, material, start, finish) = AnalyticBaseBuilder.Totals(shown, budgets);
            string? gap = null;
            if (item.Kind == GridRowKind.Objective && !item.IsVirtual && wps.Count == 0)
                gap = item.HasCode ? "element nakładki bez WP" : "brak kodu P1S (Legacy WBS) – bez WP";
            else if (wps.Any(w => !AnalyticBaseBuilder.HasBudget(budgets.GetValueOrDefault(w))))
                gap = "WP bez budżetu";
            if (item.SplitError)
                gap = gap is null ? "kod wydzielenia: ELEMENT_P1S.NAZWA.CPLGR" : $"{gap}; kod wydzielenia: ELEMENT_P1S.NAZWA.CPLGR";
            if (assignment?["WP"] is not null && assignment["CAM"] is null)
                gap = gap is null ? "WP bez CAM" : $"{gap}; WP bez CAM";
            if (ownWp is not null && currencyGaps.GetValueOrDefault(ownWp) is { } currencyGap)
                gap = gap is null ? currencyGap : $"{gap}; {currencyGap}";
            if (ownWp is not null && EtcGap(ownWp) is { } etcGap)
                gap = gap is null ? etcGap : $"{gap}; {etcGap}";
            if (item.CostGap is { } costGap)
                gap = gap is null ? costGap : $"{gap}; {costGap}";
            decimal Actual(int measure) => item.Kind == GridRowKind.Objective ? item.Sum[measure]
                : wps.Sum(w => wpActuals.TryGetValue(w, out var sums) ? sums[measure] : 0);
            var budget = budgetWp is null ? null : budgets.GetValueOrDefault(budgetWp);
            var status = dates.ActualFinish is not null ? "zakończony" : dates.Start is not null ? "w toku"
                : shown.Count > 0 || ops.Bac is not null ? "nie rozpoczęty" : null;
            rows.Add(new StructureRow(item.Id, parentId, depth, item.Kind, item.NodeKey, item.IsVirtual, item.Name, item.WbsElement, item.P1s,
                item.Note, item.IsGreyed, assignment?["WP"], assignment?["CAM"], assignment?["Cost Category"], wps, hours, material, start, finish,
                gap, item.Children.Count > 0, Actual(CostMeasure), AnalyticBaseBuilder.Bac(shown, budgets), Date(dates.Start), Date(dates.ActualFinish),
                ops.Bac, Sum(shown, PvHours), ops.Ev, ops.Ac, ops.Material, Sum(shown, PvCost), Sum(shown, EvCost), Actual(DlhMeasure), manualStart, manualFinish,
                PerformanceObligation: item.PerformanceObligation, Mpk: assignment?["MPK"],
                ForecastStart: Date(Forecast(shown, "Forecast Start", latest: false)), ForecastFinish: Date(Forecast(shown, "Forecast Koniec", latest: true)),
                Status: status,
                EtcHours: shown.Count > 0 ? Sum(shown, EtcHours) : ops.Etc, EtcHoursManual: budget?["ETC HOURS"] is not null,
                MaterialValue: ops.Value, MaterialFee: ops.Fee,
                MaterialEtc: shown.Count > 0 ? Sum(shown, MaterialEtc) : ops.Value - (ops.Material ?? 0), MaterialEtcManual: budget?["ETC MATERIAL"] is not null,
                Parts: ops.Parts, PartsDelivered: ops.PartsDelivered, NextDeliveryYear: ops.NextYear,
                AcwpLabor: Actual(LaborMeasure), AcwpMaterial: Actual(MaterialMeasure), EtcCost: Sum(shown, EtcCost),
                ProductionEtcHours: ops.Etc, UndeliveredByYear: ops.Undelivered));
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
            roots.Sum(r => r.Sum[CostMeasure]),
            withoutWp,
            costOf.Where(c => !used.Contains(c.Key)).Sum(c => c.Value),
            costs is not null,
            AnalyticBaseBuilder.Bac(all, budgets));
        return new ProjectStructure(rows, summary);
    }

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

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
