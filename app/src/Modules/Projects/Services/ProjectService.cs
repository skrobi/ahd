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

    /// <summary>
    /// Wartości słowników powiązanych do wyboru w tabeli słownika (DictColumn.Lookup): Osoby – USRID → imię i nazwisko
    /// (słownik Osoby wczytany z HR).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<LookupOption>> Lookups(IReadOnlyList<LookupOption> persons) =>
        new Dictionary<string, IReadOnlyList<LookupOption>> { [GlobalDictionaries.Persons] = persons };

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
            wpCam?.Select(r => r["WP"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<DictRow> Rows(string dictionary, string code) => _dictionaries.Load(ProjectDictionaries.Base(dictionary), code);

    /// <summary>
    /// Wiersze słownika projektu do edycji w tabeli: Cost Category – zmiany projektu i pozycje globalne
    /// (ProjectDictionaries.WithGlobal), pozostałe – wiersze projektu.
    /// </summary>
    public IReadOnlyList<(DictRow Row, bool Inherited)> EditableRows(string dictionary, string code)
    {
        if (dictionary != GlobalDictionaries.CostCategory)
            return Rows(dictionary, code).Select(r => (r, false)).ToList();
        var spec = ProjectDictionaries.Base(dictionary);
        return ProjectDictionaries.WithGlobal(spec, _dictionaries.Load(spec), Rows(dictionary, code));
    }

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
                    .Where(wp => !planned.Contains(wp)).Select(wp => (IReadOnlyList<object?>)new object?[] { wp, null, null, null, null }));
            }
            sheets.Add((item.Sheet, _dictionaries.ExcelColumns(spec), data));
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
    public static ProjectStructure Structure(PoTree tree, MappingInputs inputs, IReadOnlyList<DictRow> wpCam, IReadOnlyList<DictRow> schedule) =>
        StructureBuilder.Build(tree, Resolve(tree, inputs), inputs.P1s, wpCam, schedule);

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
        }

        var wpChanges = changes.Where(c => StructureEdits.WpCamColumns.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
        var wp = row.Wp;
        if (wpChanges.Count > 0)
        {
            if (row.P1s is null)
                return (false, "Wiersz nie ma kodu P1S – WP przypisuje się do elementu P1S.");
            var (working, removed) = StructureEdits.WpCam(Rows(ProjectDictionaries.WpCam, code), row.P1s, wpChanges);
            var outcome = SaveDictionary(ProjectDictionaries.WpCam, Context(code, tree, null, inputs), working, removed, code);
            if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
                return (false, Describe($"WP i CAM: {outcome.Message}", outcome.Issues));
            messages.Add($"WP i CAM {(outcome.Status == SaveStatus.Saved ? "zapisane" : "bez zmian")}");
            var assigned = Rows(ProjectDictionaries.WpCam, code);
            wp = assigned.FirstOrDefault(r => MappingKeys.Key(r["Element P1S"]) == MappingKeys.Key(row.P1s))?["WP"];
            if (row.Wp is { } oldWp && !string.Equals(oldWp, wp, StringComparison.OrdinalIgnoreCase)
                && !assigned.Any(r => string.Equals(r["WP"], oldWp, StringComparison.OrdinalIgnoreCase)))
            {
                var (schedule, gone) = StructureEdits.ScheduleAfterWpChange(Rows(ProjectDictionaries.ScheduleBudget, code), oldWp, wp);
                var moved = SaveDictionary(ProjectDictionaries.ScheduleBudget, Context(code, tree, assigned, inputs), schedule, gone, code);
                if (moved.Status == SaveStatus.Saved)
                    messages.Add(gone.Count > 0 ? $"usunięto harmonogram WP {oldWp}" : $"budżet WP {oldWp} przeniesiony na {wp}");
                else if (moved.Status != SaveStatus.NoChanges)
                    messages.Add(Describe($"harmonogram WP {oldWp} bez zmian: {moved.Message}", moved.Issues));
            }
        }

        var budgetChanges = changes.Where(c => StructureEdits.ScheduleColumns.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
        if (budgetChanges.Count > 0)
        {
            if (wp is null)
                return (false, string.Join("; ", messages.Append("budżet i daty wymagają WP w wierszu")));
            var (working, removed) = StructureEdits.Schedule(Rows(ProjectDictionaries.ScheduleBudget, code), wp, budgetChanges);
            var outcome = SaveDictionary(ProjectDictionaries.ScheduleBudget, Context(code, tree, Rows(ProjectDictionaries.WpCam, code), inputs), working, removed, code);
            if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
                return (false, Describe(string.Join("; ", messages.Append($"Harmonogram i budżet: {outcome.Message}")), outcome.Issues));
            messages.Add($"harmonogram i budżet {(outcome.Status == SaveStatus.Saved ? "zapisane" : "bez zmian")}");
        }
        return (true, messages.Count == 0 ? "Brak zmian." : $"Zapisano: {string.Join("; ", messages)}.");
    }

    /// <summary>
    /// Zapis stanu słownika projektu (edycja w zakładce „Słowniki projektu”, zmiana w tabeli struktury) z regułami
    /// projektu; working – wiersze projektu po edycji, removed – usunięte.
    /// </summary>
    public SaveOutcome SaveDictionary(string dictionary, ProjectDictionaryContext context, IReadOnlyList<DictRow> working, IReadOnlyList<DictRow> removed, string code,
        bool confirmWarnings = true) =>
        _dictionaries.Save(ProjectDictionaries.For(dictionary, context), working, removed, confirmWarnings, code);

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

    public List<Issue> Readiness(ProjectInfo project, PoTree tree, MappingInputs inputs)
    {
        var counts = ProjectDictionaries.Items.Where(i => i.Stored).ToDictionary(i => i.Code, i => Rows(i.Code, project.Code).Count);
        var persons = Persons();
        var camsOutside = Rows(ProjectDictionaries.WpCam, project.Code).Select(r => r["CAM"]).OfType<string>()
            .Where(c => !persons.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        return ProjectReadiness.Check(project.Type, tree, counts, camsOutside, ObjectivesMapping.Check(tree, Resolve(tree, inputs), inputs),
            folders.CheckStructure(project.Code), folders.CamAccessWarning(project.Code));
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
