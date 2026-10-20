using System.IO;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Process;
using PzlEv.Shared.Utils.Mapping;
using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Projekt PZL-EV (docs/funkcjonalnosc.md, F01, F02): utworzenie z nakładką Performance Objectives, słownikami
/// projektu i folderami; utrzymanie nakładki i słowników projektu; gotowość; baza analityczna i eksport do Excela.
/// </summary>
public sealed class ProjectService(IProjectStore store, IDictionaryStore dictionaries, IJournal journal, ProjectFolders folders,
    IPzlProdSource? pzlProd, Func<ISourceImport?>? imports = null, Func<IRunLauncher?>? runs = null, Func<IAcwpChanges?>? acwpChanges = null,
    Func<IRunPanels?>? runPanels = null)
{
    private const string Area = "Programy";

    private readonly DictionaryService _dictionaries = new(dictionaries, journal);
    private readonly object _mappingLock = new();
    private P1sInputs? _mapping;

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
    /// (słownik Osoby wczytany z HR); Kategorie WBS – kategorie projektu; Źródła danych – aktywne definicje źródeł;
    /// Parametry projektu – pola kwoty ACTUALS; Cost Category – grupy narzutowe CAS (identyfikator → nazwa).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<LookupOption>> Lookups(IReadOnlyList<LookupOption> persons, IReadOnlyList<LookupOption> categories,
        IReadOnlyList<LookupOption>? sources = null, IReadOnlyList<LookupOption>? burdenGroups = null, IReadOnlyList<LookupOption>? mpks = null,
        IReadOnlyList<RunDefinitionHeaderRow>? runTemplates = null) =>
        new Dictionary<string, IReadOnlyList<LookupOption>>
        {
            [GlobalDictionaries.Persons] = persons, [ProjectDictionaries.WbsCategories] = categories, [ProjectDictionaries.Sources] = sources ?? [],
            [GlobalDictionaries.CasBurdenGroups] = burdenGroups ?? [], [GlobalDictionaries.DepartmentRates] = mpks ?? [],
            [ProjectDictionaries.Settings] = [.. CurrencySetOptions, .. RunTemplateOptions(runTemplates ?? [])],
        };

    /// <summary>
    /// Szablony przebiegów do wyboru w programie (Parametry programu → „Przebieg”, kreator): aktywne, z etapami – słowniki globalne
    /// „Definicje przebiegów”, „Etapy przebiegów” (ekran Przebiegi).
    /// </summary>
    public IReadOnlyList<RunDefinitionHeaderRow> RunTemplates() =>
        RunDefinitions.Available(RunDefinitionRows.Headers(_dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunDefinitionHeaders))),
            RunDefinitionRows.Steps(_dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.RunSteps))));

    /// <summary>Szablon → opcja wyboru („CAS – domyślny dla CAS”).</summary>
    public static IReadOnlyList<LookupOption> RunTemplateOptions(IEnumerable<RunDefinitionHeaderRow> templates) =>
        templates.Select(t => new LookupOption(t.Code, $"{t.Code} (przebieg; domyślny dla: {t.ProjectType})")).ToList();

    /// <summary>Stawki wydziałów (MPK × rok: Labor Rate, Overhead) – lista MPK w „WP i CAM” i stawka ETC Cost struktury.</summary>
    public IReadOnlyList<DictRow> DepartmentRates() => _dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.DepartmentRates));

    /// <summary>Kursy walut (waluta × okres → kurs do PLN) – przeliczenie ETC Cost na walutę projektu.</summary>
    public IReadOnlyList<DictRow> FxRates() => _dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.FxRates));

    /// <summary>Zestawy walutowe ACTUALS do wyboru (kreator, „Parametry projektu”): pole kwoty → kolumna waluty / kwoty.</summary>
    public static IReadOnlyList<LookupOption> CurrencySetOptions { get; } =
        ProjectSettings.ActualsSets.Select(s => new LookupOption(s.Field, s.Label)).ToList();

    /// <summary>
    /// Waluta projektu z danych: kody z kolumny waluty zestawu (pole kwoty field) w ostatnim imporcie ACTUALS projektu –
    /// jeden kod: waluta; kilka: null i powód (kwoty w różnych walutach się nie sumują); brak danych – null.
    /// </summary>
    public (string? Currency, string? Problem) ProjectCurrency(string code, string field)
    {
        var set = ProjectSettings.SetOf(field);
        return CurrencyOf(store.ProjectCurrencies(code, set.CurrencyField), set);
    }

    private static (string? Currency, string? Problem) CurrencyOf(IReadOnlyList<string> codes, ProjectSettings.ActualsSet set)
    {
        return codes.Count switch
        {
            0 => (null, null),
            1 => (codes[0], null),
            _ => (null, $"kilka walut w kolumnie {set.CurrencyColumn}: {string.Join(", ", codes)} – kwoty w różnych walutach się nie sumują; wybierz inny zestaw walutowy w „Parametrach programu”"),
        };
    }

    /// <summary>
    /// Stawki ETC Cost: Labor Rate × Overhead (brak – 1) MPK z roku dnia stanu (stawki w PLN); waluta projektu – z danych
    /// (ProjectCurrency); kursy do PLN – obowiązujące w dniu stanu („Kursy walut”, kolumna Kurs). Praca: PLN → waluta
    /// projektu, materiały (vAPD): USD → waluta projektu; kwoty „Harmonogram i budżet” w innej walucie – przeliczniki Factors.
    /// </summary>
    public static EtcRates EtcRatesOf(IReadOnlyList<DictRow> departmentRates, IReadOnlyList<DictRow> fxRates, string? currency, string? currencyProblem,
        DateOnly today)
    {
        static decimal? Dec(string? text) => decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        var year = today.Year;
        var rates = departmentRates.Where(r => r["MPK"] is not null && r["Year"] == year.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                                && Dec(r["Labor Rate"]) is not null)
            .GroupBy(r => r["MPK"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Dec(g.First()["Labor Rate"])!.Value * (Dec(g.First()["Overhead"]) ?? 1), StringComparer.OrdinalIgnoreCase);
        if (currency is null)
            return new EtcRates(rates, year, null, null, null, currencyProblem ?? "waluta programu nieznana – brak danych ACTUALS programu");
        decimal? ToPln(string code) => FxRate.On(fxRates, code, today);
        var project = ToPln(currency);
        var usd = ToPln("USD");
        var missing = new[] { (Code: currency, Rate: project), (Code: "USD", Rate: usd) }.Where(x => x.Rate is null).Select(x => x.Code).Distinct().ToList();
        var factors = project is not { } toProject ? null
            : fxRates.Select(r => r["Waluta"]).OfType<string>().Append("PLN").Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(c => (Code: c, Rate: ToPln(c))).Where(x => x.Rate is not null)
                .ToDictionary(x => x.Code, x => x.Rate!.Value / toProject, StringComparer.OrdinalIgnoreCase);
        return new EtcRates(rates, year, currency, project is { } p1 ? 1 / p1 : null, project is { } p2 && usd is { } u ? u / p2 : null,
            missing.Count > 0 ? $"brak kursu {string.Join(", ", missing)} w „Kursy walut” obowiązującego {today:yyyy-MM-dd}" : null, factors);
    }

    /// <summary>Grupy narzutowe CAS do wyboru w kolumnie „Grupa narzutowa CAS” Cost Category projektu (identyfikator → nazwa).</summary>
    public IReadOnlyList<LookupOption> BurdenGroupLookups() =>
        GlobalDictionaries.LookupOptions(GlobalDictionaries.CasBurdenGroups, _dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.CasBurdenGroups)));

    /// <summary>Aktywne definicje źródeł do wyboru w słowniku „Źródła danych” (kod → kod i prefiks plików).</summary>
    public IReadOnlyList<LookupOption> SourceLookups() => SourceOptions(SourceDefinitions());

    /// <summary>Aktywne definicje źródeł (Administracja; kontrakt modułu Import) – brak modułu Import: pusta lista.</summary>
    public IReadOnlyList<SourceDefinitionRow> SourceDefinitions() => imports?.Invoke()?.Definitions() ?? [];

    public static IReadOnlyList<LookupOption> SourceOptions(IEnumerable<SourceDefinitionRow> definitions) =>
        definitions.Select(d => new LookupOption(d.Code, $"{d.Code} ({d.Prefix})")).ToList();

    /// <summary>Kody definicji ze słownika „Źródła danych” projektu.</summary>
    public IReadOnlySet<string> ProjectSources(string code) =>
        Rows(ProjectDictionaries.Sources, code).Select(r => r["Definicja źródła"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Przebiegi projektów (kontrakt modułu Przebiegi); null – moduł niedostępny.</summary>
    public IRunLauncher? Runs => runs?.Invoke();

    /// <summary>Okno „Zmiany ACWP” (moduł Przebiegi); null – moduł niedostępny.</summary>
    public IAcwpChanges? AcwpChanges => acwpChanges?.Invoke();

    /// <summary>Panel przebiegu programu (moduł Przebiegi); null – moduł niedostępny.</summary>
    public IRunPanels? RunPanels => runPanels?.Invoke();

    /// <summary>Import źródeł projektu (kontrakt modułu Import); null – moduł Import niedostępny.</summary>
    public ISourceImport? Imports => imports?.Invoke();

    // ---------- mapowanie CES ↔ P1S (strona P1S nakładki) ----------

    /// <summary>
    /// Struktura P1S z PZLPROD (niedostępny – bez struktury P1S, powód w P1sError). Czytana raz na sesję (LOG.WBS jest
    /// duża, a zmienia się rzadko); refresh – ponowny odczyt („Odśwież P1S i koszty”).
    /// </summary>
    public P1sInputs Mapping(bool refresh = false)
    {
        lock (_mappingLock)
        {
            if (refresh || _mapping is null)
                _mapping = LoadMapping();
            return _mapping;
        }
    }

    private P1sInputs LoadMapping()
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
        return new P1sInputs(p1s, error);
    }

    public static P1sScope Scope(PoTree tree, P1sInputs inputs) => ObjectivesMapping.Scope(tree, inputs);

    /// <param name="wpCam">Wiersze „WP i CAM” do sprawdzenia harmonogramu (null – słownika nie wczytano).</param>
    public ProjectDictionaryContext Context(string code, PoTree tree, IEnumerable<DictRow>? wpCam, P1sInputs inputs)
    {
        // Waluta projektu i przeliczniki walut – zapis waluty kwot „Harmonogram i budżet” (ProjectDictionaries.StampCurrency).
        var currency = code.Length > 0 ? ProjectCurrency(code, ActualsAmount(code)).Currency : null;
        return new ProjectDictionaryContext(Scope(tree, inputs),
            store.P1sOwners(code),
            Persons(),
            wpCam?.Select(r => r["WP"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase),
            Categories(code),
            imports?.Invoke() is null ? null : SourceLookups().Select(s => s.Value).ToHashSet(StringComparer.OrdinalIgnoreCase),
            currency,
            currency is null ? null : EtcRatesOf([], FxRates(), currency, null, DateOnly.FromDateTime(DateTime.Today)).Factor);
    }

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
    public void ExportDictionaries(string path, string code, string type, PoTree tree, P1sInputs inputs)
    {
        var sheets = new List<(string, IReadOnlyList<ExcelTableWriter.ExcelColumn>, IEnumerable<IReadOnlyList<object?>>)>();
        foreach (var item in ProjectDictionaries.ForType(type).Where(i => i.Stored))
        {
            var spec = ProjectDictionaries.Base(item.Code);
            var rows = code.Length > 0 ? Rows(item.Code, code) : [];
            var lookups = _dictionaries.Lookups(spec);
            IEnumerable<IReadOnlyList<object?>> data = rows.Select(r => (IReadOnlyList<object?>)spec.Columns.Select(c => DictionaryService.ExcelValue(c, r[c.Name], lookups)).ToList());
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
        journal.Add(Area, $"{(code.Length > 0 ? code : "nowy program")}: słowniki programu pobrane do Excela", code.Length > 0 ? code : null);
    }

    /// <summary>Słowniki globalne z bieżącym stanem (krok „Słowniki projektu” kreatora): nazwa, wiersze, ostatnia zmiana.</summary>
    public IReadOnlyList<(string Name, int Rows, string LastChange)> GlobalState() =>
        GlobalDictionaries.All.Where(spec => !spec.Hidden).Select(spec =>
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
    public static ProjectStructure Structure(PoTree tree, P1sInputs inputs, IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule,
        IReadOnlyDictionary<string, decimal>? costs = null, ProductionData? production = null, IReadOnlyDictionary<string, decimal>? dlh = null,
        IReadOnlyDictionary<string, decimal>? labor = null, IReadOnlyDictionary<string, decimal>? material = null, EtcRates? rates = null) =>
        StructureBuilder.Build(tree, inputs.P1s, wpCam, schedule, costs, production, dlh: dlh, labor: labor, material: material, rates: rates);

    /// <summary>
    /// Dane produkcyjne projektu na żywo z PZLPROD (Operational EV, daty rzeczywiste, materiały dostarczone): elementy P1S
    /// zakresu projektu (Legacy WBS z poddrzewami LOG.WBS) → PSPNR → sql/pzlprod/produkcja.sql; wydzielenia –
    /// węzły wirtualne nakładki z kodem ELEMENT_P1S.NAZWA.CPLGR (ProductionSplit) – reguła na PSPNR elementu; parametry ze
    /// słowników globalnych „Wskaźniki DJK” i „Parametry produkcji”. Bez PZLPROD albo błąd odczytu – dane puste z powodem
    /// (struktura działa bez kolumn produkcyjnych).
    /// </summary>
    public ProductionData Production(PoTree tree, P1sInputs inputs)
    {
        var now = DateTimeOffset.Now;
        if (pzlProd is null || inputs.P1s is null)
            return ProductionData.Unavailable(inputs.P1sError ?? "Brak połączenia z PZLPROD (pzl-ev.json, PzlProd)", now);
        var index = ProductionSplit.Index(inputs.P1s);
        var pspnrs = ObjectivesMapping.Scope(tree, inputs).Elements()
            .Select(element => index.GetValueOrDefault(MappingKeys.Key(element))?.Pspnr).OfType<string>().Distinct().ToList();
        var notes = new List<string>();
        var rules = new List<VirtualRule>();
        foreach (var node in tree.Nodes.Where(n => n.IsVirtual && !string.IsNullOrWhiteSpace(n.LegacyWbs)))
        {
            if (ProductionSplit.Parse(node.LegacyWbs, index) is { } split)
                rules.Add(new VirtualRule(split.Code, split.Pspnr, null, split.Cplgr, null));
            else if (!index.ContainsKey(MappingKeys.Key(node.LegacyWbs)))
                notes.Add($"węzeł „{node.Name}” – kod {node.LegacyWbs} nie jest wydzieleniem ELEMENT_P1S.NAZWA.CPLGR (element z LOG.WBS) – bez godzin");
        }
        var parameters = GlobalDictionaries.ProductionParametersFrom(
            _dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.DjkRates)), _dictionaries.Load(GlobalDictionaries.Get(GlobalDictionaries.ProductionParameters)));
        if (parameters.IsDefault)
            notes.Add("słownik „Wskaźniki DJK” albo „Parametry produkcji” pusty – wartości raportu S70MR (DJK W2–W4 10%, W5 15%, W6 20%; 12 mies.; DOST, WYD; ÷ (1 + Z_CLO))");
        try
        {
            var values = pzlProd.Production(pspnrs, rules, parameters);
            return new ProductionData(values
                    .GroupBy(v => v.VirtualCode is { } virtualCode ? ProductionData.VirtualKey(MappingKeys.Key(virtualCode)) : MappingKeys.Key(v.Pspnr))
                    .ToDictionary(g => g.Key, g => g.First()),
                now, Note: notes.Count == 0 ? null : string.Join("; ", notes));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ProductionData.Unavailable($"Odczyt danych produkcyjnych (PZLPROD vAHDD / vAPD) nieudany: {ex.Message}", now);
        }
    }

    /// <summary>Kolumny tabeli struktury w arkuszu „Struktura” – wszystkie poza nazwą i brakami (osobne kolumny arkusza).</summary>
    private static IReadOnlyList<StructureColumns.Column> SheetColumns { get; } =
        StructureColumns.All.Where(c => c.Key is not StructureEdits.Name and not StructureColumns.Gaps).ToList();

    /// <summary>
    /// Nagłówki arkusza „Struktura” (ExportStructure): poziom, nazwa i rodzaj wiersza, kolumny tabeli w kolejności grup
    /// (StructureColumns; przy CAM – imię i nazwisko), WP w poddrzewie, braki, uwagi.
    /// </summary>
    public static readonly IReadOnlyList<string> StructureHeaders =
    [
        "Poziom", "WBS Name", "Rodzaj",
        .. SheetColumns.SelectMany(c => c.Key == StructureEdits.Cam ? [c.Header, CamNameHeader] : new[] { c.Header }),
        "WP w poddrzewie", "Braki", "Uwagi",
    ];

    private const string CamNameHeader = "CAM – imię i nazwisko";

    /// <summary>
    /// Wiersze arkusza „Struktura”: całe drzewo (także zwinięte wiersze) w kolejności tabeli, nazwa wcięta według poziomu;
    /// wartości jak w tabeli (sumy poddrzewa): liczby i daty jako wartości Excela, budżet tylko w wierszach z WP; CAM – USRID
    /// i imię i nazwisko ze słownika Osoby.
    /// </summary>
    public static IEnumerable<IReadOnlyList<object?>> StructureRows(ProjectStructure structure, IReadOnlyList<LookupOption> persons)
    {
        var names = persons.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
        return structure.Rows.Select(r =>
        {
            var cells = new List<object?> { r.Depth + 1, new string(' ', r.Depth * 2) + r.Name,
                r.Kind == GridRowKind.P1s ? "element P1S" : r.IsVirtual ? "węzeł wirtualny" : "element nakładki" };
            foreach (var column in SheetColumns)
            {
                cells.Add(SheetValue(r, column.Key));
                if (column.Key == StructureEdits.Cam)
                    cells.Add(r.Cam is { } cam ? names.GetValueOrDefault(cam) : null);
            }
            cells.Add(r.Wps.Count > 0 ? string.Join(", ", r.Wps) : null);
            cells.Add(r.Gap);
            cells.Add(r.Note);
            return (IReadOnlyList<object?>)cells;
        });
    }

    /// <summary>Wartość komórki arkusza: data (RRRR-MM-DD) – DateOnly, liczba – zaokrąglona do 2 miejsc, puste – null.</summary>
    private static object? SheetValue(StructureRow r, string key)
    {
        static DateOnly? Date(string? value) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        static object? Round(decimal? value) => value is { } v ? Math.Round(v, 2) : null;
        static object? NonZero(decimal value) => value == 0 ? null : value;
        // Kwota ACWP – zaokrąglona jak pozostałe kwoty (koszt CAS po narzutach ma pełną precyzję obliczeń); 0 – puste.
        static object? Amount(decimal value) => value == 0 ? null : Math.Round(value, 2);
        var budget = r.Wps.Count > 0;
        return key switch
        {
            StructureColumns.WbsElement => r.WbsElement,
            StructureEdits.P1s => r.P1s,
            StructureColumns.PerformanceObligation => r.PerformanceObligation,
            StructureEdits.Wp => r.Wp,
            StructureEdits.Cam => r.Cam,
            StructureEdits.CostCategory => r.CostCategory,
            StructureEdits.Mpk => r.Mpk,
            StructureEdits.Start => Date(r.Start),
            StructureEdits.Finish => Date(r.Finish),
            StructureEdits.BacHours => budget ? r.BacHours : null,
            StructureEdits.BacMaterial => budget ? r.BacMaterial : null,
            StructureEdits.Bac => budget ? r.Bac : null,
            StructureColumns.ActualStart => Date(r.ActualStart),
            StructureColumns.ActualFinish => Date(r.ActualFinish),
            StructureEdits.ForecastStart => Date(r.ForecastStart),
            StructureEdits.ForecastFinish => Date(r.ForecastFinish),
            StructureColumns.Status => r.Status,
            StructureColumns.OpsBacHours => Round(r.OpsBacHours),
            StructureColumns.PvHours => Round(r.PvHours),
            StructureColumns.EvHours => Round(r.EvHours),
            StructureColumns.PercentComplete => Round(r.PercentComplete),
            StructureColumns.AcHours => Round(r.AcHours),
            StructureEdits.EtcHours => Round(r.EtcHours),
            StructureColumns.EacHours => Round(r.EacHours),
            StructureColumns.SpiHours => Round(r.SpiHours),
            StructureColumns.CpiHours => Round(r.CpiHours),
            StructureColumns.MaterialValue => Round(r.MaterialValue),
            StructureColumns.MaterialFee => Round(r.MaterialFee),
            StructureColumns.ActualMaterial => Round(r.ActualMaterial),
            StructureEdits.MaterialEtc => Round(r.MaterialEtc),
            StructureColumns.PercentDelivered => Round(r.PercentDelivered),
            StructureColumns.Parts => r.Parts is { } parts ? $"{parts} / {r.PartsDelivered ?? 0}" : null,
            StructureColumns.NextDeliveryYear => r.NextDeliveryYear,
            StructureColumns.PvCost => Round(r.PvCost),
            StructureColumns.EvCost => Round(r.EvCost),
            StructureColumns.AcwpLabor => Amount(r.AcwpLabor),
            StructureColumns.AcwpMaterial => Amount(r.AcwpMaterial),
            StructureColumns.AcwpOther => Amount(r.AcwpOther),
            StructureColumns.Acwp => Amount(r.Acwp),
            StructureColumns.ActualDlh => NonZero(r.ActualDlh),
            StructureColumns.DlhDelta => Round(r.DlhDelta),
            StructureColumns.Sv => Round(r.Sv),
            StructureColumns.Cv => Round(r.Cv),
            StructureColumns.Spi => Round(r.Spi),
            StructureColumns.Cpi => Round(r.Cpi),
            StructureColumns.EtcCost => Round(r.EtcCost),
            StructureColumns.Eac => Round(r.Eac),
            StructureColumns.Vac => Round(r.Vac),
            StructureColumns.PercentSpent => Round(r.PercentSpent),
            _ => null,
        };
    }

    /// <summary>
    /// Kolumny arkusza „Struktura”: format kolumny; kolumny czytane przy wczytaniu (StructureImport.Inputs) wyróżnione,
    /// z opisem w komentarzu; CAM – lista osób, MPK – lista MPK (arkusz „Listy”).
    /// </summary>
    public static IReadOnlyList<ExcelTableWriter.ExcelColumn> StructureSheetColumns(IReadOnlyList<LookupOption> persons, IReadOnlyList<LookupOption>? mpks = null)
    {
        var kinds = SheetColumns.ToDictionary(c => c.Header, c => c);
        return StructureHeaders.Select(header => header switch
        {
            "Poziom" => new ExcelTableWriter.ExcelColumn(header, ExcelTableWriter.Kind.Integer),
            StructureImport.WpHeader => new(header, Note: "Do uzupełnienia: wpisz cokolwiek (np. x) przy elemencie P1S, który jest WP – kod WP = kod elementu. Pusta komórka – bez zmiany.", Input: true),
            StructureImport.CamHeader => new(header, Note: "Do uzupełnienia: numer znaczka (USRID) CAM – lista w arkuszu „Listy”. Pusta komórka – bez zmiany.",
                Lookup: persons.Select(p => (p.Value, p.Label)).ToList(), Input: true),
            StructureImport.CategoryHeader => new(header, Note: "Do uzupełnienia: kategoria ze słownika „Kategorie WBS” programu. Pusta komórka – bez zmiany.", Input: true),
            StructureImport.MpkHeader => new(header, Note: "Do uzupełnienia: MPK WP ze słownika „Stawki wydziałów” (stawka ETC Cost). Pusta komórka – bez zmiany.",
                Lookup: mpks?.Select(m => (m.Value, m.Label)).ToList(), Input: true),
            StructureImport.StartHeader or StructureImport.FinishHeader or StructureImport.ForecastStartHeader or StructureImport.ForecastFinishHeader
                => new(header, ExcelTableWriter.Kind.Date, Note: "Do uzupełnienia w wierszu z WP (RRRR-MM-DD); w wierszach bez WP – wartość poddrzewa, nie jest wczytywana.", Input: true),
            StructureImport.ActualStartHeader or StructureImport.ActualFinishHeader => new(header, ExcelTableWriter.Kind.Date,
                Note: "Do uzupełnienia w wierszu z WP (RRRR-MM-DD): data rzeczywista WP nadpisuje datę z raportu produkcyjnego (np. WP bez danych produkcyjnych, prace zakończone przed zamknięciem operacji). Komórka z datą z produkcji – bez zmiany.", Input: true),
            StructureImport.BacHoursHeader or StructureImport.BacMaterialHeader or StructureImport.BacHeader => new(header, ExcelTableWriter.Kind.Number,
                Note: "Do uzupełnienia w wierszu z WP; w wierszach bez WP – suma poddrzewa, nie jest wczytywana.", Input: true),
            StructureImport.EtcHoursHeader or StructureImport.MaterialEtcHeader => new(header, ExcelTableWriter.Kind.Number,
                Note: "Do uzupełnienia w wierszu z WP: wartość do poniesienia nadpisuje wartość z produkcji. Komórka z wartością z produkcji – bez zmiany.", Input: true),
            _ when kinds.TryGetValue(header, out var column) && column.Key is StructureColumns.ActualStart or StructureColumns.ActualFinish
                => new(header, ExcelTableWriter.Kind.Date),
            _ when kinds.TryGetValue(header, out var column) && column.Right && column.Key is not StructureColumns.Parts and not StructureColumns.NextDeliveryYear
                => new(header, ExcelTableWriter.Kind.Number),
            _ => new ExcelTableWriter.ExcelColumn(header),
        }).ToList();
    }

    /// <summary>Struktura projektu do Excela – arkusz „Struktura” (StructureRows); zwraca liczbę wierszy.</summary>
    public int ExportStructure(string path, string code, ProjectStructure structure, IReadOnlyList<LookupOption> persons, IReadOnlyList<LookupOption>? mpks = null)
    {
        ExcelTableWriter.WriteTemplate(path, [("Struktura", StructureSheetColumns(persons, mpks), StructureRows(structure, persons))]);
        journal.Add(Area, $"{code}: struktura programu pobrana do Excela ({structure.Rows.Count} wierszy)", code);
        return structure.Rows.Count;
    }

    /// <summary>
    /// Zmiany z arkusza „Struktura” uzupełnionego w Excelu (StructureImport) wobec bieżącej struktury i „Harmonogramu
    /// i budżetu” projektu – do potwierdzenia i zapisu (SaveStructureEdits).
    /// </summary>
    public StructureImportResult ReadStructureFile(string path, string code, ProjectStructure structure)
    {
        var sheet = TabularFileReader.ExcelExtensions.Contains(Path.GetExtension(path))
            ? TabularFileReader.SheetNames(path).FirstOrDefault(s => s.Equals("Struktura", StringComparison.OrdinalIgnoreCase))
            : null;
        return StructureImport.Read(TabularFileReader.Read(path, sheet), structure, Rows(ProjectDictionaries.ScheduleBudget, code));
    }

    /// <summary>ACWP po elemencie CES z ostatniego importu ACTUALS (pole kwoty z „Parametrów projektu”) – kolumna ACWP struktury.</summary>
    public IReadOnlyDictionary<string, decimal> CostsByElement(string code) => CostsByElement(code, ActualsAmount(code));

    /// <summary>ACWP po elemencie CES z podanego pola kwoty ACTUALS.</summary>
    public IReadOnlyDictionary<string, decimal> CostsByElement(string code, string field) => store.CostsByElement(code, field);

    /// <summary>Pole godzin parsera ACTUALS (kolumna „Total Quantity”) – Actual DLH struktury.</summary>
    public const string ActualsHours = "TotalQuantity";

    /// <summary>
    /// ACWP (pole kwoty field) i Actual DLH (suma „Total Quantity” z tych samych wierszy) po elemencie CES – jedno wywołanie
    /// REP_ProjectCostsByElement (migracja 036; wcześniej dwa odczyty dużego zrzutu ACTUALS). asOf – stan danych zatwierdzonego
    /// przebiegu (ApprovedActuals, migracja 054); null – ostatni import.
    /// </summary>
    public ElementActuals CostsAndHoursByElement(string code, string field, DateTimeOffset? asOf = null)
    {
        var process = Process(code, field);
        var actuals = store.CostsAndHoursByElement(code, process.AmountField(field), ActualsHours, asOf);
        if (!process.TransformsCosts)
            return actuals;
        // Koszt liczony procesem projektu z kosztów bazowych po okresach (np. CAS – narzuty, kurs CAS); Actual DLH – godziny
        // z tych samych wierszy co dotąd.
        var hours = actuals.Hours;
        var data = store.CasCosts(code, asOf);
        var result = process.Costs(CostRows(data), LastPeriod(data));
        if (result.Error is { } error)
            throw new InvalidOperationException(error);
        var kinds = EditableRows(GlobalDictionaries.CostCategory, code).Select(x => x.Row)
            .Where(r => r["Numer elementu kosztowego"] is not null)
            .GroupBy(r => r["Numer elementu kosztowego"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[GlobalDictionaries.CostKindColumn], StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, decimal> Sum(Func<CasCostRow, bool> include) =>
            data.Select((r, i) => (Row: r, Cost: result.Costs[i])).Where(x => include(x.Row))
                .GroupBy(x => x.Row.WbsElement, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Cost), StringComparer.OrdinalIgnoreCase);
        string? Kind(CasCostRow r) => r.CostElement is { } ce ? kinds.GetValueOrDefault(ce) : null;
        return new ElementActuals(Sum(_ => true), hours, Sum(r => Kind(r) == GlobalDictionaries.LaborKind), Sum(r => Kind(r) == GlobalDictionaries.MaterialKind));
    }

    /// <summary>
    /// Waluta kosztów projektu CAS: zestaw walutowy z „Parametrów projektu” – Object currency (PLN) albo Report currency
    /// (np. USD); kwoty zawsze z PLN (Value in Obj. Crcy), USD – kursem CAS, nie kwotą USD z SAP.
    /// </summary>
    public string CasCurrency(string code, string field)
    {
        var (currency, problem) = ProjectCurrency(code, field);
        if (problem is not null)
            throw new InvalidOperationException(problem);
        return currency ?? "PLN";
    }

    /// <summary>
    /// Kalkulator kosztu CAS projektu (CasCosts): pule, grupy, formuły i wskaźniki ze słowników globalnych narzutów CAS, grupa
    /// narzutowa cost elementu z efektywnego słownika Cost Category projektu, kurs CAS z „Kursy walut”.
    /// </summary>
    public CasCosts CasCalculator(string code, string currency)
    {
        IReadOnlyList<DictRow> Global(string dictionary) => _dictionaries.Load(GlobalDictionaries.Get(dictionary));
        var mapping = EditableRows(GlobalDictionaries.CostCategory, code).Select(x => x.Row)
            .Where(r => r["Numer elementu kosztowego"] is not null)
            .GroupBy(r => r["Numer elementu kosztowego"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[GlobalDictionaries.BurdenGroupColumn], StringComparer.OrdinalIgnoreCase);
        return new CasCosts(CasBurden.PoolsFrom(Global(GlobalDictionaries.CasPools)),
            CasBurden.GroupsFrom(Global(GlobalDictionaries.CasBurdenGroups), Global(GlobalDictionaries.CasBurdenFormulas)),
            CasBurden.RatesFrom(Global(GlobalDictionaries.CasBurdenRates)), mapping, FxRates(), currency);
    }

    private static IReadOnlyList<CostRow> CostRows(IReadOnlyList<CasCostRow> data) =>
        data.Select(r => new CostRow(r.WbsElement, r.CostElement, r.FiscalYear, r.Period, r.Amount)).ToList();

    /// <summary>
    /// Proces projektu (docs/architektura.md, rozdz. 6.1) – jedyne miejsce wyboru wariantu według typu projektu: CAS – narzuty
    /// CAS i kurs CAS (CasProcess), pozostałe typy – proces bazowy (SAC – do czasu własnych reguł). field – pole kwoty
    /// ACTUALS projektu (null – z „Parametrów projektu”).
    /// </summary>
    public ProjectProcess Process(string code, string? field = null) => Process(code, Find(code)?.Type, field);

    /// <summary>Proces projektu o znanym typie (bez odczytu projektu); kalkulator wariantu tworzony przy pierwszym użyciu.</summary>
    public ProjectProcess Process(string code, string? type, string? field) => type switch
    {
        ProjectTypes.Cas => new CasProcess(() => CasCalculator(code, CasCurrency(code, field ?? ActualsAmount(code)))),
        _ => ProjectProcess.Default,
    };

    /// <summary>Ostatni okres w danych ACTUALS (kolumna okresu raportu); bez niego – najpóźniejszy okres kosztów projektu.</summary>
    private static (int Year, int Period) LastPeriod(IReadOnlyList<CasCostRow> data) =>
        data.FirstOrDefault(r => r.LastYear is not null) is { LastYear: { } year, LastPeriod: { } period } ? (year, period)
        : data.Count == 0 ? (DateTime.Today.Year, DateTime.Today.Month) : data.Max(r => (r.FiscalYear, r.Period));

    /// <summary>Pole kwoty ACTUALS projektu – słownik „Parametry projektu” (brak – ValueObjCrcy).</summary>
    public string ActualsAmount(string code) => ProjectDictionaries.ActualsAmount(Rows(ProjectDictionaries.Settings, code));

    /// <summary>Z kiedy są dane ACTUALS (data raportu w RABIT, import, ostatnie sprawdzenie) – nagłówek ekranu projektu.</summary>
    public ActualsFreshness ActualsFreshness() => new(store.ActualsFiles());

    /// <summary>Ostatni przebieg programu z zatwierdzonymi danymi ACTUALS – stan danych struktury; null – ostatni import.</summary>
    public ApprovedActuals? ApprovedActuals(string code) => store.ApprovedActuals(code);

    /// <summary>
    /// Zapis zmiany wiersza tabeli struktury od razu po jej zatwierdzeniu (bez osobnego „Zapisz”): nazwa i Legacy WBS –
    /// nakładka; WP, CAM, Cost Category – „WP i CAM” (klucz – kod P1S wiersza); budżet i daty – „Harmonogram i budżet”
    /// (klucz – WP wiersza). Ostrzeżenia nie wstrzymują zapisu (są w komunikacie), ERROR – tak. Zmiana WP przenosi budżet
    /// starego WP, jeśli ten nie jest już przypisany do innego elementu (StructureEdits.ScheduleAfterWpChange).
    /// </summary>
    public (bool Saved, string Message) SaveStructureEdit(string code, P1sInputs inputs, StructureRow row, IReadOnlyDictionary<string, string?> changes)
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
    public IReadOnlyList<StructureEditResult> SaveStructureEdits(string code, P1sInputs inputs, IReadOnlyList<StructureEdit> edits)
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

    private List<StructureEditResult> SaveDictionaryEdits(string code, P1sInputs inputs, IReadOnlyList<StructureEdit> edits)
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

    /// <summary>Raport kosztów projektu do Excela (REP_ProjectCosts – ostatni import ACTUALS, pole kwoty z „Parametrów projektu”); zwraca liczbę wierszy.</summary>
    public int ExportCostReport(string path, string code)
    {
        var (columns, rows) = store.CostReport(code, ActualsAmount(code));
        ExcelTableWriter.WriteSheets(path, [("Koszty", columns, rows)]);
        journal.Add(Area, $"{code}: raport kosztów pobrany do Excela ({rows.Count} wierszy)", code);
        return rows.Count;
    }

    /// <summary>
    /// Raport kosztów CES do Excela (projekty CAS): raport kosztów z narzutami CAS w formułach – grupa narzutowa
    /// z efektywnego słownika Cost Category projektu, pule i formuły ze słowników „Kategorie narzutu CAS”, „Grupy narzutowe CAS”
    /// i „Formuły narzutów CAS”, wskaźniki ze „Wskaźników narzutu CAS” (CasCostReport); arkusze „Wskaźniki” i „Uwagi” (braki mapowania i wskaźników). Zwraca liczbę wierszy i uwag.
    /// </summary>
    public (int Rows, int Notes) ExportCasCostReport(string path, string code)
    {
        // Koszt bazowy zawsze z PLN (Value in Obj. Crcy), w walucie projektu kursem CAS okresu kosztu – jak ACWP struktury.
        var currency = CasCurrency(code, ActualsAmount(code));
        var data = store.CasCosts(code);
        var calculator = CasCalculator(code, currency);
        var missing = data.Select(r => (r.FiscalYear, r.Period)).Distinct().Where(p => calculator.Fx(p.FiscalYear, p.Period) is null)
            .Select(p => $"{p.FiscalYear}-{Math.Clamp(p.Period, 1, 12):00}").Distinct().Order().ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(new CasCosts.Result([], missing, []).Error);
        var (columns, rows) = CasCostReport.Table(data, data.Select(r => r.Amount * calculator.Fx(r.FiscalYear, r.Period)!.Value).ToList(), LastPeriod(data));
        var mapping = EditableRows(GlobalDictionaries.CostCategory, code).Select(x => x.Row)
            .Where(r => r["Numer elementu kosztowego"] is not null)
            .GroupBy(r => r["Numer elementu kosztowego"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[GlobalDictionaries.BurdenGroupColumn], StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<DictRow> Global(string dictionary) => _dictionaries.Load(GlobalDictionaries.Get(dictionary));
        var result = CasCostReport.Build(columns, rows, mapping,
            CasBurden.PoolsFrom(Global(GlobalDictionaries.CasPools)),
            CasBurden.GroupsFrom(Global(GlobalDictionaries.CasBurdenGroups), Global(GlobalDictionaries.CasBurdenFormulas)),
            CasBurden.RatesFrom(Global(GlobalDictionaries.CasBurdenRates)));
        List<string> notes = [.. result.Notes];
        if (!string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
            notes.Insert(0, $"Kwoty w {currency}: koszt PLN z ACTUALS (Value in Obj. Crcy) przeliczony kursem CAS („Kursy walut”, {GlobalDictionaries.CasFxColumn}) obowiązującym w okresie kosztu, narzuty od kwot w {currency}.");
        List<ExcelTableWriter.ReportSheet> sheets = [result.Report, result.Rates];
        if (notes.Count > 0)
            sheets.Add(new ExcelTableWriter.ReportSheet("Uwagi", ["Uwaga"], notes.Select(n => (IReadOnlyList<object?>)[n])));
        ExcelTableWriter.WriteReport(path, sheets);
        journal.Add(Area, $"{code}: raport kosztów CES pobrany do Excela ({rows.Count} wierszy, {currency}, uwagi: {notes.Count})", code);
        return (rows.Count, notes.Count);
    }

    // ---------- gotowość, baza analityczna ----------

    public List<Issue> Readiness(ProjectInfo project, PoTree tree, P1sInputs inputs) =>
        Readiness(project, tree, inputs,
            ProjectDictionaries.Items.Where(i => i.Stored).ToDictionary(i => i.Code, i => Rows(i.Code, project.Code)), Persons(), FolderChecks(project.Code));

    /// <summary>
    /// Gotowość z danymi już wczytanymi (ekran projektu): struktura P1S, wiersze słowników projektu (kod →
    /// wiersze), USRID osób, wynik kontroli folderów – bez ponownych odczytów z bazy i dysku sieciowego.
    /// </summary>
    public static List<Issue> Readiness(ProjectInfo project, PoTree tree, P1sInputs inputs,
        IReadOnlyDictionary<string, IReadOnlyList<DictRow>> dictionaries, IReadOnlySet<string> persons, (Issue Structure, Issue CamAccess) folderChecks)
    {
        var counts = dictionaries.ToDictionary(d => d.Key, d => d.Value.Count);
        var wpCam = dictionaries.GetValueOrDefault(ProjectDictionaries.WpCam) ?? [];
        var withoutCam = wpCam.Where(r => r["WP"] is not null && r["CAM"] is null).Select(r => r["WP"]!).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var camsOutside = wpCam.Select(r => r["CAM"]).OfType<string>()
            .Where(c => !persons.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        return ProjectReadiness.Check(project.Type, tree, counts, camsOutside, ObjectivesMapping.Check(tree, inputs),
            folderChecks.Structure, folderChecks.CamAccess, withoutCam);
    }

    /// <param name="rates">Waluta projektu i kursy (EtcRatesOf) – kwoty „Harmonogram i budżet” w innej walucie przeliczone; null – bez przeliczenia.</param>
    public static AnalyticBase Analytic(PoTree tree, IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule, P1sInputs inputs, EtcRates? rates = null)
    {
        var gaps = new List<string>();
        var converted = schedule.Select(r =>
        {
            var row = ProjectDictionaries.InProjectCurrency(r, rates, out var gap);
            if (gap is not null)
                gaps.Add($"WP {r["WP"]}: {gap}");
            return row;
        }).ToList();
        return AnalyticBaseBuilder.Build(tree, wpCam, converted, ObjectivesMapping.CodesOf, ObjectivesMapping.Scope(tree, inputs))
            with { Currency = rates?.Currency, CurrencyGaps = gaps };
    }

    /// <summary>
    /// Waluta projektu niezapisanego (kreator): kody z kolumny waluty zestawu w ostatnim imporcie ACTUALS dla Project
    /// definition i WBS element nakładki – jak ProjectCurrency.
    /// </summary>
    public (string? Currency, string? Problem) ProjectCurrency(PoTree tree, string field)
    {
        var set = ProjectSettings.SetOf(field);
        var codes = tree.Nodes.SelectMany(n => new[] { n.ProjectDefinition, n.WbsElement }).OfType<string>()
            .Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return CurrencyOf(store.CurrenciesOf(codes, set.CurrencyField), set);
    }

    /// <summary>Kursy kreatora: waluta projektu niezapisanego i kursy bieżące („Kursy walut”) – przeliczenie kwot budżetu.</summary>
    public EtcRates WizardRates(PoTree tree, string field)
    {
        var (currency, problem) = ProjectCurrency(tree, field);
        return EtcRatesOf([], FxRates(), currency, problem, DateOnly.FromDateTime(DateTime.Today));
    }

    public void ExportAnalytic(string path, string code, AnalyticBase analytic)
    {
        var material = analytic.Currency is { } currency ? $"BAC MATERIAL ({currency})" : "BAC MATERIAL";
        ExcelTableWriter.WriteSheets(path,
        [
            ("Baza analityczna",
                ["Poziom", "Nazwa", "Element CES", "P1S (Legacy WBS)", "Węzeł wirtualny", "WP", "CAM", "BAC HOURS", material, "Start", "Koniec", "Braki"],
                analytic.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
                {
                    r.Depth + 1, new string(' ', r.Depth * 2) + r.Name, r.WbsElement, r.P1s, r.IsVirtual, string.Join(", ", r.Wps), string.Join(", ", r.Cams),
                    r.BacHours, r.BacMaterial, r.Start, r.Finish, r.Gap,
                })),
            ("Wg CAM", ["CAM", "WP", "BAC HOURS", material, "Start", "Koniec"],
                analytic.ByCam.Select(c => (IReadOnlyList<object?>)new object?[] { c.Cam, c.Wps, c.BacHours, c.BacMaterial, c.Start, c.Finish })),
        ]);
        journal.Add(Area, $"{(code.Length > 0 ? code : "nowy program")}: baza analityczna pobrana do Excela", code.Length > 0 ? code : null);
    }

    // ---------- utworzenie projektu ----------

    /// <summary>
    /// „Utwórz projekt” (F01, krok 5): kontrola danych, nakładki, słowników i folderów; rejestracja projektu z nakładką
    /// w bazie; zapis słowników (wczytane podglądy); utworzenie folderów i kontrola struktury. Słownik z ERROR blokuje.
    /// </summary>
    /// <param name="previews">Wczytane słowniki projektu: kod słownika → plik (ścieżka, arkusz).</param>
    public CreateOutcome Create(string code, string name, string type, PoTree tree, IReadOnlyDictionary<string, (string Path, string? Sheet)> previews,
        string? actualsAmount = null, string? runDefinition = null)
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
        steps.Add($"Zarejestrowano program {code} ({ProjectTypes.Label(type)}) z nakładką: {tree.ElementCount} elementów CES, {tree.VirtualCount} węzłów wirtualnych");
        journal.Add(Area, $"{code}: utworzono program „{name.Trim()}” ({ProjectTypes.Label(type)})", code);

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

        // Zestaw walutowy ACTUALS i przebieg wybrane w kreatorze – „Parametry projektu” (gdy skoroszyt nie podał parametrów).
        if ((actualsAmount is not null || runDefinition is not null) && !loaded.ContainsKey(ProjectDictionaries.Settings))
        {
            var settings = new List<DictRow>();
            if (actualsAmount is not null)
                settings.Add(new DictRow(null, null, new Dictionary<string, string?>
                {
                    ["Parametr"] = ProjectSettings.ActualsAmount, ["Wartość"] = ProjectSettings.ActualsAmountOf(actualsAmount), ["Opis"] = null,
                }));
            if (runDefinition is not null)
                settings.Add(new DictRow(null, null, new Dictionary<string, string?>
                {
                    ["Parametr"] = ProjectSettings.RunDefinition, ["Wartość"] = runDefinition, ["Opis"] = null,
                }));
            var saved = SaveStructureDictionary(ProjectDictionaries.Settings, context, settings, [], code);
            if (saved.Status is SaveStatus.Saved or SaveStatus.NoChanges)
            {
                if (actualsAmount is not null)
                    steps.Add($"Zestaw walutowy ACTUALS: {ProjectSettings.SetOf(ProjectSettings.ActualsAmountOf(actualsAmount)).Label}");
                if (runDefinition is not null)
                    steps.Add($"Przebieg: {runDefinition}");
            }
            else
                problems.Add(Issue.Error($"Parametry programu nie zapisane: {saved.Message}", ProjectDictionaries.Settings));
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
    public Dictionary<string, ImportPreview> PreviewAll(string code, PoTree tree, IReadOnlyDictionary<string, (string Path, string? Sheet)> files, P1sInputs inputs)
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
