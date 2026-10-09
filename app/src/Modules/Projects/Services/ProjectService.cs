using System.IO;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Mapping;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Projekt PZL-EV (docs/funkcjonalnosc.md, F01, F02): utworzenie z nakładką Performance Objectives, słownikami
/// projektu i folderami; utrzymanie nakładki i słowników projektu; gotowość; baza analityczna i eksport do Excela.
/// </summary>
public sealed class ProjectService(IProjectStore store, IDictionaryStore dictionaries, IJournal journal, ProjectFolders folders,
    IMappingStore mapping, IPzlProdSource? pzlProd)
{
    private const string Area = "Projekty";

    private readonly DictionaryService _dictionaries = new(dictionaries, journal);
    private readonly object _mappingLock = new();
    private MappingInputs? _mapping;

    public ProjectFolders Folders => folders;

    public IReadOnlyList<ProjectInfo> Projects() => store.Projects();

    public ProjectInfo? Find(string code) => store.Find(code);

    public PoTree Objectives(string code) => store.Objectives(code);

    public List<Issue> ValidateBasics(string code, string name, string type) =>
        ProjectRules.ValidateBasics(code, name, type, store.Projects().Select(p => p.Code));

    public List<Issue> ValidateObjectives(string code, PoTree tree) => ObjectivesValidator.Validate(tree, store.WbsOwners(code));

    /// <summary>Elementy CES nakładek innych projektów (O46) – do kontroli w pamięci podczas edycji nakładki.</summary>
    public IReadOnlyDictionary<string, string> WbsOwners(string exceptCode) => store.WbsOwners(exceptCode);

    public PoImportResult ReadObjectives(string path) => PerformanceObjectivesReader.Read(path);

    /// <summary>
    /// Podmiana nakładki eksportem SAP (edytor nakładki – „Wczytaj Excel”): struktura z pliku; elementy o tym samym
    /// WBS element zachowują identyfikator (historia węzła trwa). Węzły wirtualne i ręczne zmiany nie są przenoszone –
    /// zachowuje je „Dołóż z Excela” (AddNew).
    /// </summary>
    public static PoTree Refresh(PoTree current, PoTree imported)
    {
        var existing = current.Nodes.Where(n => !n.IsVirtual && n.WbsElement is not null)
            .GroupBy(n => n.WbsElement!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var keys = new Dictionary<long, long>();
        var nodes = new List<PoNode>();
        foreach (var node in imported.Nodes)
        {
            var copy = node.Copy();
            if (node.WbsElement is not null && existing.TryGetValue(node.WbsElement, out var old))
            {
                copy = CopyWithKey(node, old.Key);
                copy.NodeId = old.NodeId;
                copy.Version = old.Version;
            }
            keys[node.Key] = copy.Key;
            nodes.Add(copy);
        }
        foreach (var node in nodes)
            node.ParentKey = node.ParentKey is { } p ? keys[p] : null;
        return new PoTree(nodes);
    }

    private static PoNode CopyWithKey(PoNode node, long key) => new()
    {
        Key = key, ParentKey = node.ParentKey, SortOrder = node.SortOrder, Level = node.Level, IsVirtual = node.IsVirtual,
        ProjectDefinition = node.ProjectDefinition, WbsElement = node.WbsElement, Name = node.Name, PersonResponsible = node.PersonResponsible,
        ProfitCenter = node.ProfitCenter, LegacyWbs = node.LegacyWbs, PerformanceObligation = node.PerformanceObligation,
        SacObjNumber = node.SacObjNumber, IsStatistical = node.IsStatistical, IsAcctAsstElement = node.IsAcctAsstElement,
    };

    /// <summary>
    /// Dołożenie do nakładki elementów z kolejnego eksportu SAP (ekran Projekt – np. nowe Project definition): elementy
    /// o WBS element, którego nakładka jeszcze nie ma, dochodzą pod swojego rodzica z pliku (istniejący element nakładki
    /// albo nowy); istniejące węzły, węzły wirtualne i zmiany w aplikacji zostają bez zmian.
    /// </summary>
    public static (PoTree Tree, int Added, IReadOnlyList<string> Projects) AddNew(PoTree current, PoTree imported)
    {
        var tree = current.Copy();
        var existing = tree.Nodes.Where(n => !n.IsVirtual && n.WbsElement is not null)
            .GroupBy(n => n.WbsElement!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
        var keys = new Dictionary<long, long>();
        var added = 0;
        var projects = new List<string>();
        foreach (var (node, _) in imported.Flatten())
        {
            if (node.WbsElement is not null && existing.TryGetValue(node.WbsElement, out var key))
            {
                keys[node.Key] = key;
                continue;
            }
            var copy = CopyWithKey(node, tree.NewKey());
            copy.ParentKey = node.ParentKey is { } parent && keys.TryGetValue(parent, out var mapped) ? mapped : null;
            tree.Add(copy);
            keys[node.Key] = copy.Key;
            if (copy.WbsElement is not null)
                existing[copy.WbsElement] = copy.Key;
            added++;
            if (node.ProjectDefinition is { Length: > 0 } project && !projects.Contains(project, StringComparer.OrdinalIgnoreCase))
                projects.Add(project);
        }
        return (tree, added, projects);
    }

    /// <summary>Zapis nakładki po edycji (z historią); ERROR walidacji blokuje zapis.</summary>
    public (List<Issue> Issues, StoreResult? Result) SaveObjectives(string code, PoTree tree)
    {
        var issues = ValidateObjectives(code, tree);
        if (issues.Any(i => i.Level == CheckLevel.Error))
            return (issues, null);
        var result = store.SaveObjectives(code, tree);
        if (result.Success)
            journal.Add(Area, $"{code}: nakładka Performance Objectives +{result.Added} ~{result.Updated} −{result.Removed}", code);
        return (issues, result);
    }

    // ---------- słowniki projektu ----------

    /// <summary>USRID ze słownika globalnego Osoby (CAM wybierany z listy osób; zapisywany USRID).</summary>
    public IReadOnlySet<string> Persons() => PersonLookups().Select(o => o.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Osoby ze słownika Osoby: USRID → imię i nazwisko (posortowane po nazwisku).</summary>
    public IReadOnlyList<LookupOption> PersonLookups() =>
        GlobalDictionaries.LookupOptions(GlobalDictionaries.Persons, _dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.Persons)));

    /// <summary>
    /// Lista wyboru CAM w tabeli struktury: osoby ze słownika Osoby (persons) oraz CAM już wpisane w „WP i CAM”, których
    /// nie ma w słowniku (np. sprzed wczytania osób z HR) – żeby tabela je pokazała.
    /// </summary>
    public static IReadOnlyList<LookupOption> PersonOptions(IReadOnlyList<LookupOption> persons, IEnumerable<DictRow> wpCam)
    {
        var known = persons.Select(p => p.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return persons.Concat(wpCam.Select(r => r["CAM"]).OfType<string>().Where(c => !known.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(c => new LookupOption(c, c)))
            .OrderBy(p => p.Label, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>Kategorie ze słownika „Kategorie WBS” projektu (kolumna Cost Category w „WP i CAM” i strukturze).</summary>
    public IReadOnlyList<LookupOption> CategoryLookups(string code) => CategoryLookups(Rows(ProjectDictionaries.WbsCategories, code));

    /// <summary>Kategorie z wierszy słownika „Kategorie WBS” już wczytanych.</summary>
    public static IReadOnlyList<LookupOption> CategoryLookups(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Cost Category"] is not null)
            .Select(r => new LookupOption(r["Cost Category"]!, r["Cost Category"]!))
            .OrderBy(o => o.Value, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>
    /// Lista wyboru Cost Category w tabeli struktury: kategorie projektu (categories) oraz kategorie już wpisane
    /// w „WP i CAM”, których nie ma w słowniku (np. dawne Labor / Material / Subcontract) – żeby tabela je pokazała.
    /// </summary>
    public static IReadOnlyList<LookupOption> CategoryOptions(IReadOnlyList<LookupOption> categories, IEnumerable<DictRow> wpCam)
    {
        var known = categories.Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return categories.Concat(wpCam.Select(r => r["Cost Category"]).OfType<string>().Where(c => !known.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(c => new LookupOption(c, c)))
            .ToList();
    }

    /// <summary>
    /// Wartości słowników powiązanych do wyboru w tabeli słownika (DictColumn.Lookup): Osoby – USRID → imię i nazwisko
    /// (słownik Osoby wczytany z HR); Kategorie WBS – kategorie projektu.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<LookupOption>> Lookups(IReadOnlyList<LookupOption> persons, IReadOnlyList<LookupOption> categories) =>
        new Dictionary<string, IReadOnlyList<LookupOption>> { [GlobalDictionaries.Persons] = persons, [ProjectDictionaries.WbsCategories] = categories };

    // ---------- mapowanie CES ↔ P1S (strona P1S nakładki) ----------

    /// <summary>
    /// Dane mapowania z bazy PZL-EV i PZLPROD (PZLPROD niedostępny – bez struktury P1S, powód w P1sError). Czytane raz
    /// na sesję (raport mapowań i LOG.WBS są duże, a zmieniają się rzadko); refresh – ponowny odczyt („Odśwież mapowanie”).
    /// Elementy CES z kosztów nie są potrzebne: projekt CES elementu pochodzi z nakładki (ObjectivesMapping).
    /// </summary>
    public MappingInputs Mapping(bool refresh = false)
    {
        lock (_mappingLock)
        {
            if (refresh || _mapping is null)
                _mapping = LoadMapping();
            return _mapping;
        }
    }

    private MappingInputs LoadMapping()
    {
        IReadOnlyList<P1sElement>? p1s = null;
        string? error = pzlProd is null ? "Brak połączenia z PZLPROD (pzl-ev.json, PzlProd)" : null;
        if (pzlProd is not null)
        {
            try
            {
                p1s = pzlProd.Elements();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                error = $"Odczyt PZLPROD (LOG.WBS) nieudany: {ex.Message}";
            }
        }
        return new MappingInputs(mapping.Report(), mapping.ActiveCorrections(), p1s, error);
    }

    public static IReadOnlyDictionary<long, MappingResult> Resolve(PoTree tree, MappingInputs inputs) => ObjectivesMapping.Resolve(tree, inputs);

    public static P1sScope Scope(PoTree tree, MappingInputs inputs) => ObjectivesMapping.Scope(tree, Resolve(tree, inputs), inputs);

    /// <param name="wpCam">Wiersze „WP i CAM” do sprawdzenia harmonogramu (null – słownika nie wczytano).</param>
    public ProjectDictionaryContext Context(string code, PoTree tree, IEnumerable<DictRow>? wpCam, MappingInputs inputs) =>
        new(Scope(tree, inputs),
            store.P1sOwners(code),
            Persons(),
            wpCam?.Select(r => r["WP"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase),
            Categories(code));

    /// <summary>Kategorie „Kategorie WBS” projektu do sprawdzenia Cost Category w „WP i CAM”; słownik pusty – null (bez kontroli).</summary>
    private IReadOnlySet<string>? Categories(string code)
    {
        var categories = CategoryLookups(code);
        return categories.Count == 0 ? null : categories.Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<DictRow> Rows(string dictionary, string code) => _dictionaries.Load(ProjectDictionaries.Base(dictionary), code);

    /// <summary>
    /// Wiersze słownika projektu do edycji w tabeli: Cost Category – zmiany projektu i pozycje globalne
    /// (ProjectDictionaries.WithGlobal), pozostałe – wiersze projektu.
    /// </summary>
    public IReadOnlyList<(DictRow Row, bool Inherited)> EditableRows(string dictionary, string code) => EditableRows(dictionary, Rows(dictionary, code));

    /// <summary>Jak EditableRows(dictionary, code), z wierszami projektu już wczytanymi (bez ponownego odczytu).</summary>
    public IReadOnlyList<(DictRow Row, bool Inherited)> EditableRows(string dictionary, IReadOnlyList<DictRow> rows)
    {
        if (dictionary != GlobalDictionaries.CostCategory)
            return rows.Select(r => (r, false)).ToList();
        var spec = ProjectDictionaries.Base(dictionary);
        return ProjectDictionaries.WithGlobal(spec, _dictionaries.Load(spec), rows);
    }

    /// <summary>
    /// Bieżące wiersze słownika projektu i jego ostatnia zmiana jednym odczytem (ekran projektu – liczba wierszy,
    /// „ostatnia zmiana”, struktura i gotowość korzystają z tego samego odczytu).
    /// </summary>
    public (IReadOnlyList<DictRow> Rows, string LastChange) DictionaryState(string dictionary, string code)
    {
        var current = dictionaries.Current(dictionary, code);
        var last = current.MaxBy(r => r.RecordedAt);
        return (current.Select(r => new DictRow(r.RowId, r.Version, r.Values)).ToList(),
            last is null ? "" : $"{last.RecordedAt:yyyy-MM-dd HH:mm} · {last.RecordedBy}");
    }

    /// <summary>Kontrole folderów projektu (gotowość) – dysk sieciowy, czytane rzadko (ekran projektu trzyma wynik).</summary>
    public (Issue Structure, Issue CamAccess) FolderChecks(string code) => (folders.CheckStructure(code), folders.CamAccessWarning(code));

    /// <summary>Arkusz słownika w skoroszycie (nazwa jak w szablonie); null – plik bez takiego arkusza albo CSV.</summary>
    public static string? FindSheet(string path, ProjectDictionaryItem item) =>
        TabularFileReader.SheetNames(path).FirstOrDefault(s =>
            s.Equals(item.Sheet, StringComparison.OrdinalIgnoreCase) || s.Equals(item.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Podgląd wczytania słownika projektu z pliku. Skoroszyt z kilkoma arkuszami bez arkusza słownika – błąd (zamiast
    /// cichego wczytania pierwszego arkusza, np. „WP i CAM” jako harmonogramu). Cost Category – wiersze identyczne ze
    /// słownikiem globalnym pomijane (nie stają się zmianami projektu).
    /// </summary>
    public ImportPreview PreviewDictionary(string dictionary, ProjectDictionaryContext context, string path, string code, string? sheet)
    {
        var item = ProjectDictionaries.Item(dictionary);
        if (sheet is null && TabularFileReader.ExcelExtensions.Contains(Path.GetExtension(path)) && TabularFileReader.SheetNames(path) is { Count: > 1 } sheets)
            return new ImportPreview(dictionary, Path.GetFileName(path), [], [], [],
                [Issue.Error($"Skoroszyt ma kilka arkuszy ({string.Join(", ", sheets)}), żaden nie nazywa się „{item.Sheet}” ani „{item.Name}” – zmień nazwę arkusza albo zapisz słownik w osobnym pliku", Path.GetFileName(path))],
                [], []);
        var inherited = dictionary == GlobalDictionaries.CostCategory ? _dictionaries.Load(ProjectDictionaries.Base(dictionary)) : null;
        return _dictionaries.PreviewImport(ProjectDictionaries.For(dictionary, context), path, code, sheet, inherited);
    }

    public SaveOutcome ApplyDictionary(string dictionary, ProjectDictionaryContext context, ImportPreview preview, string code) =>
        _dictionaries.ApplyImport(ProjectDictionaries.For(dictionary, context), preview, code);

    /// <summary>
    /// Słowniki projektu do Excela – arkusz na słownik; szablon do uzupełnienia: „WP i CAM” z elementami P1S z zakresu
    /// bez WP, „Harmonogram i budżet” z WP bez harmonogramu.
    /// </summary>
    public void ExportDictionaries(string path, string code, string type, PoTree tree, MappingInputs inputs)
    {
        var sheets = new List<(string, IReadOnlyList<ExcelTableWriter.ExcelColumn>, IEnumerable<IReadOnlyList<object?>>)>();
        foreach (var item in ProjectDictionaries.ForType(type).Where(i => i.Stored))
        {
            var spec = ProjectDictionaries.Base(item.Code);
            var rows = code.Length > 0 ? Rows(item.Code, code) : [];
            IEnumerable<IReadOnlyList<object?>> data = rows.Select(r => (IReadOnlyList<object?>)spec.Columns.Select(c => ValueFormat.ToExcel(c, r[c.Name])).ToList());
            // Szablon do uzupełnienia: elementy P1S z zakresu bez WP i WP bez harmonogramu (wiersze z samym kluczem
            // pomijane przy wczytaniu – SkipKeyOnlyRows).
            if (item.Code == ProjectDictionaries.WpCam)
            {
                var assigned = rows.Select(r => r["Element P1S"]).OfType<string>().Select(MappingKeys.Key).ToHashSet();
                data = data.Concat(Scope(tree, inputs).Elements().Where(l => !assigned.Contains(MappingKeys.Key(l)))
                    .Select(l => (IReadOnlyList<object?>)new object?[] { l, null, null, null }));
            }
            else if (item.Code == ProjectDictionaries.ScheduleBudget && code.Length > 0)
            {
                var planned = rows.Select(r => r["WP"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
                data = data.Concat(Rows(ProjectDictionaries.WpCam, code).Select(r => r["WP"]).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(wp => !planned.Contains(wp)).Select(wp => (IReadOnlyList<object?>)new object?[] { wp, null, null, null, null, null }));
            }
            var columns = _dictionaries.ExcelColumns(spec);
            // Cost Category „WP i CAM” – lista kategorii projektu (słownik projektu – poza listami słowników globalnych).
            if (item.Code == ProjectDictionaries.WpCam && code.Length > 0 && CategoryLookups(code) is { Count: > 0 } categories)
                columns = columns.Select(c => c.Header == "Cost Category" ? c with { Lookup = categories.Select(o => (o.Value, o.Label)).ToList() } : c).ToList();
            sheets.Add((item.Sheet, columns, data));
        }
        ExcelTableWriter.WriteTemplate(path, sheets);
        journal.Add(Area, $"{(code.Length > 0 ? code : "nowy projekt")}: słowniki projektu pobrane do Excela", code.Length > 0 ? code : null);
    }

    /// <summary>Słowniki globalne z bieżącym stanem (krok „Słowniki projektu” kreatora): nazwa, wiersze, ostatnia zmiana.</summary>
    public IReadOnlyList<(string Name, int Rows, string LastChange)> GlobalState() =>
        GlobalDictionaries.All.Select(spec =>
        {
            var rows = dictionaries.Current(spec.Code);
            var last = rows.MaxBy(r => r.RecordedAt);
            return (spec.Name, rows.Count, last is null ? "brak wierszy" : $"{last.RecordedAt:yyyy-MM-dd HH:mm} · {last.RecordedBy}");
        }).ToList();

    /// <summary>Ostatnia zmiana słownika projektu (do pokazania przy słowniku).</summary>
    public string LastChange(string dictionary, string code)
    {
        var last = dictionaries.Current(dictionary, code).MaxBy(r => r.RecordedAt);
        return last is null ? "" : $"{last.RecordedAt:yyyy-MM-dd HH:mm} · {last.RecordedBy}";
    }

    // ---------- struktura projektu (ekran Projekt) ----------

    /// <summary>Struktura projektu: nakładka z rozwinięciem P1S, WP, CAM, budżet i daty (StructureBuilder).</summary>
    public static ProjectStructure Structure(PoTree tree, MappingInputs inputs, IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule,
        IReadOnlyDictionary<string, decimal>? costs = null) =>
        Structure(tree, inputs, Resolve(tree, inputs), wpCam, schedule, costs);

    /// <summary>Struktura z mapowaniem już rozstrzygniętym (Resolve) – rozstrzyganie raz na odczyt ekranu.</summary>
    public static ProjectStructure Structure(PoTree tree, MappingInputs inputs, IReadOnlyDictionary<long, MappingResult> resolved, IReadOnlyList<DictRow> wpCam,
        IReadOnlyList<DictRow> schedule, IReadOnlyDictionary<string, decimal>? costs = null, ProductionData? production = null) =>
        StructureBuilder.Build(tree, resolved, inputs.P1s, wpCam, schedule, costs, production);

    /// <summary>
    /// Dane produkcyjne projektu na żywo z PZLPROD (Operational EV, materiały dostarczone): elementy P1S zakresu projektu
    /// (Legacy WBS i cele mapowania z poddrzewami LOG.WBS) → PSPNR → vAHDD / vAPD. Bez PZLPROD albo błąd odczytu –
    /// dane puste z powodem (struktura działa bez kolumn produkcyjnych).
    /// </summary>
    public ProductionData Production(PoTree tree, MappingInputs inputs, IReadOnlyDictionary<long, MappingResult> resolved)
    {
        var now = DateTimeOffset.Now;
        if (pzlProd is null || inputs.P1s is null)
            return ProductionData.Unavailable(inputs.P1sError ?? "Brak połączenia z PZLPROD (pzl-ev.json, PzlProd)", now);
        var byCode = inputs.P1s.GroupBy(e => MappingKeys.Key(e.WbsElement)).ToDictionary(g => g.Key, g => g.First().Pspnr);
        var pspnrs = ObjectivesMapping.Scope(tree, resolved, inputs).Elements()
            .Select(code => byCode.GetValueOrDefault(MappingKeys.Key(code))).OfType<string>().Distinct().ToList();
        try
        {
            var values = pzlProd.Production(pspnrs);
            return new ProductionData(values.GroupBy(v => MappingKeys.Key(v.Pspnr)).ToDictionary(g => g.Key, g => g.First()), now);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ProductionData.Unavailable($"Odczyt danych produkcyjnych (PZLPROD vAHDD / vAPD) nieudany: {ex.Message}", now);
        }
    }

    /// <summary>Nagłówki arkusza „Struktura” (ExportStructure) – kolumny tabeli w grupach (StructureColumns) i pola pomocnicze.</summary>
    public static readonly IReadOnlyList<string> StructureHeaders =
    [
        "Poziom", "WBS Name", "Rodzaj", "CES Element", "Legacy Element (P1S)", "WP", "CAM", "CAM – imię i nazwisko", "Cost Category", "WP w poddrzewie",
        "Baseline Start", "Baseline Finish", "Actual Start", "Actual Finish",
        "BAC Hours", "PV Hours", "EV Hours", "AC Hours",
        "BAC Material", "Actual Material",
        "BAC Hours baseline", "BAC Cost", "PV Cost", "EV Cost", "ACWP",
        "Braki", "Uwagi",
    ];

    /// <summary>
    /// Wiersze arkusza „Struktura”: całe drzewo (także zwinięte wiersze) w kolejności tabeli, nazwa wcięta według poziomu;
    /// budżet, daty, wartości produkcyjne i koszty jak w tabeli (sumy poddrzewa); CAM – USRID i imię i nazwisko ze słownika Osoby.
    /// </summary>
    public static IEnumerable<IReadOnlyList<object?>> StructureRows(ProjectStructure structure, IReadOnlyList<LookupOption> persons)
    {
        var names = persons.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
        static DateOnly? Date(string? value) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        static object? Round(decimal? value) => value is { } v ? Math.Round(v, 2) : null;
        return structure.Rows.Select(r =>
        {
            var budget = r.Wps.Count > 0;
            return (IReadOnlyList<object?>)new object?[]
            {
                r.Depth + 1, new string(' ', r.Depth * 2) + r.Name,
                r.Kind == GridRowKind.P1s ? "element P1S" : r.IsVirtual ? "węzeł wirtualny" : "element nakładki",
                r.WbsElement, r.P1s, r.Wp, r.Cam, r.Cam is { } cam ? names.GetValueOrDefault(cam) : null, r.CostCategory,
                budget ? string.Join(", ", r.Wps) : null,
                Date(r.Start), Date(r.Finish), Date(r.ActualStart), Date(r.ActualFinish),
                Round(r.OpsBacHours), Round(r.PvHours), Round(r.EvHours), Round(r.AcHours),
                budget ? r.BacMaterial : null, Round(r.ActualMaterial),
                budget ? r.BacHours : null, budget ? r.Bac : null, Round(r.PvCost), Round(r.EvCost), r.Acwp == 0 ? null : r.Acwp,
                r.Gap, r.Note,
            };
        });
    }

    /// <summary>Struktura projektu do Excela – arkusz „Struktura” (StructureRows); zwraca liczbę wierszy.</summary>
    public int ExportStructure(string path, string code, ProjectStructure structure, IReadOnlyList<LookupOption> persons)
    {
        ExcelTableWriter.WriteSheets(path, [("Struktura", StructureHeaders, StructureRows(structure, persons))]);
        journal.Add(Area, $"{code}: struktura projektu pobrana do Excela ({structure.Rows.Count} wierszy)", code);
        return structure.Rows.Count;
    }

    /// <summary>ACWP po elemencie CES z ostatniego importu ACTUALS (waluta obiektu – PLN) – kolumna ACWP struktury.</summary>
    public IReadOnlyDictionary<string, decimal> CostsByElement(string code) => store.CostsByElement(code, DefaultCostValue);

    /// <summary>Z kiedy są dane ACTUALS (data raportu w RABIT, import, ostatnie sprawdzenie) – nagłówek ekranu projektu.</summary>
    public ActualsFreshness ActualsFreshness() => new(store.ActualsFiles());

    /// <summary>
    /// Zapis zmiany wiersza tabeli struktury od razu po jej zatwierdzeniu (bez osobnego „Zapisz”): nazwa i Legacy WBS –
    /// nakładka; WP, CAM, Cost Category – „WP i CAM” (klucz – kod P1S wiersza); budżet i daty – „Harmonogram i budżet”
    /// (klucz – WP wiersza). Ostrzeżenia nie wstrzymują zapisu (są w komunikacie), ERROR – tak. Zmiana WP przenosi budżet
    /// starego WP, jeśli ten nie jest już przypisany do innego elementu (StructureEdits.ScheduleAfterWpChange).
    /// </summary>
    public (bool Saved, string Message) SaveStructureEdit(string code, MappingInputs inputs, StructureRow row, IReadOnlyDictionary<string, string?> changes)
    {
        var messages = new List<string>();
        var tree = store.Objectives(code);
        var element = row.P1s;
        var dictionaryChanges = changes.Keys.Any(k => StructureEdits.WpCamColumns.Contains(k) || StructureEdits.ScheduleColumns.Contains(k));
        if (changes.TryGetValue(StructureEdits.P1s, out var newCode))
        {
            // Legacy WBS i WP / CAM w jednym wierszu (np. wklejenie): WP przypisywany do nowego kodu P1S.
            if (string.IsNullOrWhiteSpace(newCode) && dictionaryChanges)
                return (false, "Usunięcie Legacy WBS i zmiana WP / CAM / budżetu w jednym wierszu – zapisz je osobno.");
            element = string.IsNullOrWhiteSpace(newCode) ? null : newCode.Trim();
        }
        if (row.NodeKey is { } key && (changes.ContainsKey(StructureEdits.Name) || changes.ContainsKey(StructureEdits.P1s)))
        {
            if (tree.Find(key) is not { } node)
                return (false, "Węzła nie ma już w nakładce – odśwież ekran.");
            if (changes.TryGetValue(StructureEdits.Name, out var name))
            {
                if (string.IsNullOrWhiteSpace(name))
                    return (false, "Podaj nazwę węzła.");
                node.Name = name.Trim();
            }
            if (changes.TryGetValue(StructureEdits.P1s, out var legacy))
                node.LegacyWbs = string.IsNullOrWhiteSpace(legacy) ? null : legacy.Trim();
            var (issues, result) = SaveObjectives(code, tree);
            if (result is null)
                return (false, Describe("Nakładka ma błędy – nic nie zapisano", issues));
            if (!result.Success)
                return (false, result.Conflict!);
            messages.Add("nakładka zapisana");
            tree = store.Objectives(code);
            if (changes.ContainsKey(StructureEdits.P1s) && row.Wp is { } assignedWp && !dictionaryChanges)
                messages.Add($"uwaga: WP {assignedWp} jest przypisany do elementu {row.P1s} – zaznacz WP przy nowym kodzie P1S albo zmień „WP i CAM”");
        }

        var wpChanges = changes.Where(c => StructureEdits.WpCamColumns.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
        var wp = row.BudgetWp;   // budżet wiersza: jego WP albo jedyny WP pod nim
        if (wpChanges.Count > 0)
        {
            if (element is null)
                return (false, "Wiersz nie ma kodu P1S – WP przypisuje się do elementu P1S.");
            var (working, removed) = StructureEdits.WpCam(Rows(ProjectDictionaries.WpCam, code), element, wpChanges);
            var outcome = SaveStructureDictionary(ProjectDictionaries.WpCam, Context(code, tree, null, inputs), working, removed, code);
            if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
                return (false, Describe($"WP i CAM: {outcome.Message}", outcome.Issues));
            messages.Add($"WP i CAM {(outcome.Status == SaveStatus.Saved ? "zapisane" : "bez zmian")}");
            var assigned = Rows(ProjectDictionaries.WpCam, code);
            wp = assigned.FirstOrDefault(r => MappingKeys.Key(r["Element P1S"]) == MappingKeys.Key(element))?["WP"];
            if (row.Wp is { } oldWp && !string.Equals(oldWp, wp, StringComparison.OrdinalIgnoreCase)
                && !assigned.Any(r => string.Equals(r["WP"], oldWp, StringComparison.OrdinalIgnoreCase)))
            {
                var (schedule, gone) = StructureEdits.ScheduleAfterWpChange(Rows(ProjectDictionaries.ScheduleBudget, code), oldWp, wp);
                var moved = SaveStructureDictionary(ProjectDictionaries.ScheduleBudget, Context(code, tree, assigned, inputs), schedule, gone, code);
                if (moved.Status == SaveStatus.Saved)
                    messages.Add(gone.Count > 0 ? $"usunięto harmonogram WP {oldWp}" : $"budżet WP {oldWp} przeniesiony na {wp}");
                else if (moved.Status != SaveStatus.NoChanges)
                    messages.Add(Describe($"harmonogram WP {oldWp} bez zmian: {moved.Message}", moved.Issues));
            }
        }

        var budgetChanges = changes.Where(c => StructureEdits.ScheduleColumns.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
        if (budgetChanges.Count > 0)
        {
            if (wp is null && budgetChanges.Values.All(string.IsNullOrWhiteSpace))
                return (true, messages.Count == 0 ? "Brak zmian." : $"Zapisano: {string.Join("; ", messages)}.");   // WP usunięty razem z budżetem (np. cofnięcie wklejenia)
            if (wp is null)
                return (false, string.Join("; ", messages.Append("budżet i daty wymagają WP w wierszu")));
            var (working, removed) = StructureEdits.Schedule(Rows(ProjectDictionaries.ScheduleBudget, code), wp, budgetChanges);
            var outcome = SaveStructureDictionary(ProjectDictionaries.ScheduleBudget, Context(code, tree, Rows(ProjectDictionaries.WpCam, code), inputs), working, removed, code);
            if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
                return (false, Describe(string.Join("; ", messages.Append($"Harmonogram i budżet: {outcome.Message}")), outcome.Issues));
            messages.Add($"harmonogram i budżet {(outcome.Status == SaveStatus.Saved ? "zapisane" : "bez zmian")}");
        }
        return (true, messages.Count == 0 ? "Brak zmian." : $"Zapisano: {string.Join("; ", messages)}.");
    }

    /// <summary>
    /// Zapis wsadowy zmian wierszy tabeli struktury (kolejka wierszy zatwierdzonych w tym czasie – kilka WP, wklejenie
    /// bloku, Delete, Ctrl+D): WP, CAM, Cost Category, budżet i daty wszystkich wierszy są nakładane po kolei na słowniki
    /// wczytane raz i zapisywane jednym zapisem na słownik (jedna transakcja, jeden wpis w dzienniku); kontekst walidacji
    /// liczony raz. Nazwa i Legacy WBS (nakładka) – każdy wiersz osobno (SaveStructureEdit). Paczka z błędem (ERROR) albo
    /// konfliktem – zapis wiersz po wierszu: poprawne wiersze zapisują się, błędne zostają z komunikatem.
    /// </summary>
    public IReadOnlyList<StructureEditResult> SaveStructureEdits(string code, MappingInputs inputs, IReadOnlyList<StructureEdit> edits)
    {
        var overlay = edits.Where(e => e.Changes.ContainsKey(StructureEdits.Name) || e.Changes.ContainsKey(StructureEdits.P1s)).ToList();
        var results = new List<StructureEditResult>();
        var batch = edits.Except(overlay).ToList();
        if (batch.Count > 0)
            results.AddRange(SaveDictionaryEdits(code, inputs, batch));
        foreach (var edit in overlay)
        {
            var (saved, message) = SaveStructureEdit(code, inputs, edit.Row, edit.Changes);
            results.Add(new StructureEditResult(edit.Id, saved, message));
        }
        return results;
    }

    private List<StructureEditResult> SaveDictionaryEdits(string code, MappingInputs inputs, IReadOnlyList<StructureEdit> edits)
    {
        var tree = store.Objectives(code);
        var context = Context(code, tree, null, inputs);
        var wpCamBefore = Rows(ProjectDictionaries.WpCam, code);
        var scheduleBefore = Rows(ProjectDictionaries.ScheduleBudget, code);
        var (wpCam, schedule) = (wpCamBefore.ToList(), scheduleBefore.ToList());
        var (wpRemoved, scheduleRemoved) = (new List<DictRow>(), new List<DictRow>());
        var results = new List<StructureEditResult>();
        var applied = new List<(StructureEdit Edit, string? Wp, bool Budget)>();
        var (wpTouched, scheduleTouched) = (false, false);
        foreach (var edit in edits)
        {
            var wpChanges = edit.Changes.Where(c => StructureEdits.WpCamColumns.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
            var budgetChanges = edit.Changes.Where(c => StructureEdits.ScheduleColumns.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
            var element = edit.Row.P1s;
            var wp = edit.Row.BudgetWp;
            if (wpChanges.Count > 0)
            {
                if (element is null)
                {
                    results.Add(new StructureEditResult(edit.Id, false, "Wiersz nie ma kodu P1S – WP przypisuje się do elementu P1S."));
                    continue;
                }
                (wpCam, var removed) = StructureEdits.WpCam(wpCam, element, wpChanges);
                wpRemoved.AddRange(removed);
                wpTouched = true;
                wp = wpCam.FirstOrDefault(r => MappingKeys.Key(r["Element P1S"]) == MappingKeys.Key(element))?["WP"];
                if (edit.Row.Wp is { } oldWp && !string.Equals(oldWp, wp, StringComparison.OrdinalIgnoreCase)
                    && !wpCam.Any(r => string.Equals(r["WP"], oldWp, StringComparison.OrdinalIgnoreCase)))
                {
                    (schedule, var gone) = StructureEdits.ScheduleAfterWpChange(schedule, oldWp, wp);
                    scheduleRemoved.AddRange(gone);
                    scheduleTouched = true;
                }
            }
            var budget = budgetChanges.Count > 0 && !(wp is null && budgetChanges.Values.All(string.IsNullOrWhiteSpace));
            if (budget && wp is null)
            {
                results.Add(new StructureEditResult(edit.Id, false, "Budżet i daty wymagają WP w wierszu – zaznacz WP."));
                continue;
            }
            if (budget)
            {
                (schedule, var removed) = StructureEdits.Schedule(schedule, wp!, budgetChanges);
                scheduleRemoved.AddRange(removed);
                scheduleTouched = true;
            }
            applied.Add((edit, wp, budget));
        }
        if (applied.Count == 0)
            return results;

        if (wpTouched)
        {
            var spec = ProjectDictionaries.For(ProjectDictionaries.WpCam, context);
            var outcome = _dictionaries.Save(spec, wpCam, wpRemoved, confirmWarnings: true, code, _dictionaries.KnownErrors(spec, wpCamBefore));
            if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
            {
                // Paczka odrzucona – nic nie zapisano: wiersz po wierszu, żeby poprawne wiersze się zapisały.
                foreach (var (edit, _, _) in applied)
                {
                    var (saved, message) = SaveStructureEdit(code, inputs, edit.Row, edit.Changes);
                    results.Add(new StructureEditResult(edit.Id, saved, message));
                }
                return results;
            }
        }
        if (scheduleTouched)
        {
            var spec = ProjectDictionaries.For(ProjectDictionaries.ScheduleBudget,
                context with { Wps = wpCam.Select(r => r["WP"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase) });
            var outcome = _dictionaries.Save(spec, schedule, scheduleRemoved, confirmWarnings: true, code, _dictionaries.KnownErrors(spec, scheduleBefore));
            if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
            {
                // „WP i CAM” zapisane; budżet i daty wiersz po wierszu (WP wiersza – po zapisie paczki).
                foreach (var (edit, wp, budget) in applied)
                {
                    if (!budget)
                    {
                        results.Add(new StructureEditResult(edit.Id, true, "Zapisano."));
                        continue;
                    }
                    var budgetOnly = edit.Changes.Where(c => StructureEdits.ScheduleColumns.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
                    var (saved, message) = SaveStructureEdit(code, inputs, edit.Row with { Wp = wp, Wps = [wp!] }, budgetOnly);
                    results.Add(new StructureEditResult(edit.Id, saved, message));
                }
                return results;
            }
        }
        results.AddRange(applied.Select(a => new StructureEditResult(a.Edit.Id, true, "Zapisano.")));
        return results;
    }

    /// <summary>
    /// Zapis stanu słownika projektu (edycja w zakładce „Słowniki projektu”, zmiana w tabeli struktury) z regułami
    /// projektu; working – wiersze projektu po edycji, removed – usunięte.
    /// </summary>
    public SaveOutcome SaveDictionary(string dictionary, ProjectDictionaryContext context, IReadOnlyList<DictRow> working, IReadOnlyList<DictRow> removed, string code,
        bool confirmWarnings = true) =>
        _dictionaries.Save(ProjectDictionaries.For(dictionary, context), working, removed, confirmWarnings, code);

    /// <summary>
    /// Zapis słownika projektu po zmianie w tabeli struktury: ostrzeżenia nie wstrzymują zapisu, a błędy, które słownik
    /// miał już wcześniej w innych wierszach (np. element poza zakresem po zmianie mapowania), nie blokują zmiany.
    /// </summary>
    private SaveOutcome SaveStructureDictionary(string dictionary, ProjectDictionaryContext context, IReadOnlyList<DictRow> working, IReadOnlyList<DictRow> removed, string code)
    {
        var spec = ProjectDictionaries.For(dictionary, context);
        return _dictionaries.Save(spec, working, removed, confirmWarnings: true, code, _dictionaries.KnownErrors(spec, code));
    }

    /// <summary>Komunikat z pierwszymi problemami (ERROR przed WARNING).</summary>
    private static string Describe(string message, IReadOnlyList<Issue> issues)
    {
        var shown = issues.OrderBy(i => i.Level == CheckLevel.Error ? 0 : 1).Take(3).Select(i => i.Element is null ? i.Message : $"{i.Message} ({i.Element})").ToList();
        return shown.Count == 0 ? message : $"{message} – {string.Join("; ", shown)}{(issues.Count > 3 ? $" (+{issues.Count - 3})" : "")}";
    }

    // ---------- raport kosztów ----------

    /// <summary>Pole kwoty ACTUALS w raporcie kosztów – waluta obiektu (PLN).</summary>
    public const string DefaultCostValue = "ValueObjCrcy";

    /// <summary>Raport kosztów projektu do Excela (REP_ProjectCosts – ostatni import ACTUALS); zwraca liczbę wierszy.</summary>
    public int ExportCostReport(string path, string code, string value = DefaultCostValue)
    {
        var (columns, rows) = store.CostReport(code, value);
        ExcelTableWriter.WriteSheets(path, [("Koszty", columns, rows)]);
        journal.Add(Area, $"{code}: raport kosztów pobrany do Excela ({rows.Count} wierszy)", code);
        return rows.Count;
    }

    // ---------- gotowość, baza analityczna ----------

    public List<Issue> Readiness(ProjectInfo project, PoTree tree, MappingInputs inputs) =>
        Readiness(project, tree, inputs, Resolve(tree, inputs),
            ProjectDictionaries.Items.Where(i => i.Stored).ToDictionary(i => i.Code, i => Rows(i.Code, project.Code)), Persons(), FolderChecks(project.Code));

    /// <summary>
    /// Gotowość z danymi już wczytanymi (ekran projektu): rozstrzygnięte mapowanie, wiersze słowników projektu (kod →
    /// wiersze), USRID osób, wynik kontroli folderów – bez ponownych odczytów z bazy i dysku sieciowego.
    /// </summary>
    public static List<Issue> Readiness(ProjectInfo project, PoTree tree, MappingInputs inputs, IReadOnlyDictionary<long, MappingResult> resolved,
        IReadOnlyDictionary<string, IReadOnlyList<DictRow>> dictionaries, IReadOnlySet<string> persons, (Issue Structure, Issue CamAccess) folderChecks)
    {
        var counts = dictionaries.ToDictionary(d => d.Key, d => d.Value.Count);
        var wpCam = dictionaries.GetValueOrDefault(ProjectDictionaries.WpCam) ?? [];
        var withoutCam = wpCam.Where(r => r["WP"] is not null && r["CAM"] is null).Select(r => r["WP"]!).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var camsOutside = wpCam.Select(r => r["CAM"]).OfType<string>()
            .Where(c => !persons.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        return ProjectReadiness.Check(project.Type, tree, counts, camsOutside, ObjectivesMapping.Check(tree, resolved, inputs),
            folderChecks.Structure, folderChecks.CamAccess, withoutCam);
    }

    public static AnalyticBase Analytic(PoTree tree, IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule, MappingInputs inputs)
    {
        var resolved = Resolve(tree, inputs);
        return AnalyticBaseBuilder.Build(tree, wpCam, schedule, n => ObjectivesMapping.CodesOf(n, resolved), ObjectivesMapping.Scope(tree, resolved, inputs));
    }

    public void ExportAnalytic(string path, string code, AnalyticBase analytic)
    {
        ExcelTableWriter.WriteSheets(path,
        [
            ("Baza analityczna",
                ["Poziom", "Nazwa", "Element CES", "P1S (Legacy WBS, mapowanie)", "Węzeł wirtualny", "WP", "CAM", "BAC HOURS", "BAC MATERIAL", "Start", "Koniec", "Braki"],
                analytic.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
                {
                    r.Depth + 1, new string(' ', r.Depth * 2) + r.Name, r.WbsElement, r.P1s, r.IsVirtual, string.Join(", ", r.Wps), string.Join(", ", r.Cams),
                    r.BacHours, r.BacMaterial, r.Start, r.Finish, r.Gap,
                })),
            ("Wg CAM", ["CAM", "WP", "BAC HOURS", "BAC MATERIAL", "Start", "Koniec"],
                analytic.ByCam.Select(c => (IReadOnlyList<object?>)new object?[] { c.Cam, c.Wps, c.BacHours, c.BacMaterial, c.Start, c.Finish })),
        ]);
        journal.Add(Area, $"{(code.Length > 0 ? code : "nowy projekt")}: baza analityczna pobrana do Excela", code.Length > 0 ? code : null);
    }

    // ---------- utworzenie projektu ----------

    /// <summary>
    /// „Utwórz projekt” (F01, krok 5): kontrola danych, nakładki, słowników i folderów; rejestracja projektu z nakładką
    /// w bazie; zapis słowników (wczytane podglądy); utworzenie folderów i kontrola struktury. Słownik z ERROR blokuje.
    /// </summary>
    /// <param name="previews">Wczytane słowniki projektu: kod słownika → plik (ścieżka, arkusz).</param>
    public CreateOutcome Create(string code, string name, string type, PoTree tree, IReadOnlyDictionary<string, (string Path, string? Sheet)> previews)
    {
        var issues = ValidateBasics(code, name, type);
        issues.AddRange(ValidateObjectives(code, tree));
        var inputs = Mapping();
        var loaded = PreviewAll(code, tree, previews, inputs);
        foreach (var (dictionary, preview) in loaded)
            issues.AddRange(preview.Issues.Where(i => i.Level == CheckLevel.Error).Select(i => i with { Element = $"{ProjectDictionaries.Item(dictionary).Name}: {i.Element}" }));
        issues.AddRange(folders.CheckBeforeCreate(code).Where(i => i.Level == CheckLevel.Error));
        if (issues.Any(i => i.Level == CheckLevel.Error))
            return new CreateOutcome(false, [], issues.Where(i => i.Level == CheckLevel.Error).ToList());

        var steps = new List<string>();
        var created = store.Create(code, name.Trim(), type, tree);
        if (!created.Success)
            return new CreateOutcome(false, [], [Issue.Error(created.Conflict!, code)]);
        steps.Add($"Zarejestrowano projekt {code} ({ProjectTypes.Label(type)}) z nakładką: {tree.ElementCount} elementów CES, {tree.VirtualCount} węzłów wirtualnych");
        journal.Add(Area, $"{code}: utworzono projekt „{name.Trim()}” ({ProjectTypes.Label(type)})", code);

        var problems = new List<Issue>();
        var wpRows = loaded.TryGetValue(ProjectDictionaries.WpCam, out var wpPreview) ? wpPreview.Working : null;
        var context = Context(code, tree, wpRows, inputs);
        foreach (var (dictionary, preview) in loaded)
        {
            var outcome = ApplyDictionary(dictionary, context, preview, code);
            if (outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges)
                steps.Add($"{ProjectDictionaries.Item(dictionary).Name}: {preview.Working.Count} wierszy");
            else
                problems.Add(Issue.Error($"{ProjectDictionaries.Item(dictionary).Name} nie zapisany: {outcome.Message}", dictionary));
        }

        try
        {
            var made = folders.Create(code);
            steps.Add($"Utworzono foldery: {folders.PathOf(code)} ({string.Join(", ", ProjectFolders.Subfolders)}) – {made.Count} nowych");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(Issue.Warning($"Nie utworzono folderów: {ex.Message}", folders.PathOf(code)));
        }
        var structure = folders.CheckStructure(code);
        if (structure.Level == CheckLevel.Pass)
            steps.Add("Struktura folderów zgodna z konfiguracją");
        else
            problems.Add(structure);
        return new CreateOutcome(true, steps, problems);
    }

    /// <summary>Podglądy słowników z plików: najpierw „WP i CAM” (jego WP sprawdza harmonogram).</summary>
    public Dictionary<string, ImportPreview> PreviewAll(string code, PoTree tree, IReadOnlyDictionary<string, (string Path, string? Sheet)> files, MappingInputs inputs)
    {
        var result = new Dictionary<string, ImportPreview>();
        var withoutWp = Context(code, tree, null, inputs);
        if (files.TryGetValue(ProjectDictionaries.WpCam, out var wpFile))
            result[ProjectDictionaries.WpCam] = PreviewDictionary(ProjectDictionaries.WpCam, withoutWp, wpFile.Path, code, wpFile.Sheet);
        var context = withoutWp with { Wps = result.TryGetValue(ProjectDictionaries.WpCam, out var wp)
            ? wp.Working.Select(r => r["WP"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase) : null };
        foreach (var (dictionary, file) in files.Where(f => f.Key != ProjectDictionaries.WpCam))
            result[dictionary] = PreviewDictionary(dictionary, context, file.Path, code, file.Sheet);
        return result;
    }
}
