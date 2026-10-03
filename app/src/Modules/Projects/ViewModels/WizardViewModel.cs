using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Kreator projektu (docs/funkcjonalnosc.md, F01): 1. Podstawowe, 2. Performance Objectives, 3. Słowniki projektu,
/// 4. Foldery, 5. Podsumowanie (baza analityczna) i „Utwórz projekt”.
/// </summary>
public sealed class WizardViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> StepTitles = ["Podstawowe", "Performance Objectives", "Słowniki projektu", "Foldery", "Podsumowanie"];

    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly ProjectService _service;
    private readonly IFileDialogs _dialogs;
    private readonly Action<string> _openProject;
    private readonly Dictionary<string, (string Path, string? Sheet)> _files = [];
    private int _step;
    private string _code = "";
    private string _name = "";
    private TypeOption _type;
    private string _status = "";
    private CreateOutcome? _outcome;
    private AnalyticBase? _analytic;
    private MappingInputs _mapping = MappingInputs.None;
    private IReadOnlyDictionary<string, string> _owners = new Dictionary<string, string>();
    private IReadOnlyList<string> _codes = [];

    public WizardViewModel(ProjectService service, IFileDialogs dialogs, BusyState busy, Action cancel, Action<string> openProject)
    {
        _service = service;
        _dialogs = dialogs;
        _openProject = openProject;
        Busy = busy;
        Types = ProjectTypes.All.Select(t => new TypeOption(t)).ToList();
        _type = Types[0];
        // Kontrola nakładki w pamięci: elementy CES innych projektów wczytane raz (InitAsync), zapis sprawdza je ponownie w bazie.
        Objectives = new PoEditorViewModel(dialogs, busy, service.ReadObjectives, tree => ObjectivesValidator.Validate(tree, _owners), (_, imported) => imported);
        Objectives.ResolveMapping = tree => ProjectService.Resolve(tree, _mapping);
        Back = new RelayCommand(_ => GoTo(_step - 1), _ => _step > 0 && _outcome is not { Created: true } && !Busy.IsBusy);
        Next = new RelayCommand(_ => DoNext(), _ => _step < StepTitles.Count - 1 && !Busy.IsBusy);
        Cancel = new RelayCommand(_ => cancel(), _ => !Busy.IsBusy);
        DownloadTemplate = new AsyncRelayCommand(DoDownloadTemplate, () => !Busy.IsBusy);
        LoadWorkbook = new AsyncRelayCommand(DoLoadWorkbook, () => !Busy.IsBusy && !IsCreated);
        ExportAnalytic = new AsyncRelayCommand(DoExportAnalytic, () => _analytic is not null && !Busy.IsBusy);
        Create = new AsyncRelayCommand(DoCreate, () => _outcome is not { Created: true } && !Busy.IsBusy);
        OpenProject = new RelayCommand(_ => _openProject(Code), _ => _outcome is { Created: true } && !Busy.IsBusy);
        Objectives.MappingInfo = "Wczytywanie danych mapowania CES ↔ P1S…";
        Objectives.Load(new PoTree());
        BuildPanels();
        GoTo(0);
        _ = InitAsync();
    }

    public BusyState Busy { get; }

    /// <summary>Dane do kontroli w pamięci: kody projektów, elementy CES innych nakładek, mapowanie (raz na sesję).</summary>
    private async Task InitAsync()
    {
        try
        {
            (_codes, _owners, _mapping) = await Busy.Run("Wczytywanie projektów i mapowania CES ↔ P1S…",
                () => ((IReadOnlyList<string>)_service.Projects().Select(p => p.Code).ToList(), _service.WbsOwners(""), _service.Mapping()));
            Objectives.MappingInfo = _mapping.Describe;
            Objectives.Refresh();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Kreator projektu – dane początkowe");
            Status = $"Nie udało się wczytać danych z bazy: {ex.Message}";
            Objectives.MappingInfo = "Mapowanie CES ↔ P1S niedostępne.";
        }
    }

    public IReadOnlyList<TypeOption> Types { get; }

    public ObservableCollection<StepChip> Steps { get; } = [];

    public PoEditorViewModel Objectives { get; }

    public ObservableCollection<DictionaryPanelViewModel> Dictionaries { get; } = [];

    public IReadOnlyList<GlobalDictionaryState> GlobalDictionaries { get; private set; } = [];

    public ObservableCollection<Issue> BasicIssues { get; } = [];

    public ObservableCollection<Issue> FolderChecks { get; } = [];

    public ObservableCollection<Issue> SummaryChecks { get; } = [];

    public ObservableCollection<string> CreatedSteps { get; } = [];

    public ObservableCollection<Issue> CreateIssues { get; } = [];

    public IReadOnlyList<AnalyticRowViewModel> AnalyticRows => _analytic?.Rows.Select(r => new AnalyticRowViewModel(r)).ToList() ?? [];

    public IReadOnlyList<CamSummary> ByCam => _analytic?.ByCam ?? [];

    public int Step => _step;

    public bool IsStep1 => _step == 0;
    public bool IsStep2 => _step == 1;
    public bool IsStep3 => _step == 2;
    public bool IsStep4 => _step == 3;
    public bool IsStep5 => _step == 4;
    public bool IsLastStep => _step == StepTitles.Count - 1;
    public bool IsNotLastStep => !IsLastStep;
    public bool IsCreated => _outcome is { Created: true };
    public bool HasCreateIssues => CreateIssues.Count > 0;

    public string Code
    {
        get => _code;
        set
        {
            if (SetProperty(ref _code, ProjectRules.NormalizeCode(value)))
                OnPropertyChanged(nameof(FolderPreview));
        }
    }

    public string Name { get => _name; set => SetProperty(ref _name, value); }

    public TypeOption Type
    {
        get => _type;
        set
        {
            if (value is null || !SetProperty(ref _type, value))
                return;
            BuildPanels();
            _ = RefreshDictionaries();
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string FolderPreview => _service.Folders.Preview(Code);

    public string ItRequest => _service.Folders.ItRequest(Code.Length > 0 ? Code : "<KOD>");

    public string SummaryText =>
        $"{Code} · {Name.Trim()} · {Type.Label} · folder {_service.Folders.PathOf(Code)}";

    public string Totals => _analytic is null ? "" :
        $"Sumy kontrolne: {_analytic.WpCount} WP · BAC HOURS {_analytic.BacHours:N2} h · BAC MATERIAL {_analytic.BacMaterial:N2} · CAM: {_analytic.ByCam.Count}";

    public ICommand Back { get; }
    public ICommand Next { get; }
    public ICommand Cancel { get; }
    public ICommand DownloadTemplate { get; }
    public ICommand LoadWorkbook { get; }
    public ICommand ExportAnalytic { get; }
    public ICommand Create { get; }
    public ICommand OpenProject { get; }

    /// <summary>Dalej; z kroku 1 tylko przy poprawnych danych podstawowych (kod używany w kolejnych krokach).</summary>
    private void DoNext()
    {
        if (_step == 0)
        {
            SetAll(BasicIssues, ProjectRules.ValidateBasics(Code, Name, Type.Code, _codes));
            if (BasicIssues.Any(i => i.Level == CheckLevel.Error))
            {
                Status = "Popraw dane podstawowe projektu.";
                return;
            }
        }
        GoTo(_step + 1);
    }

    private void GoTo(int step)
    {
        _step = Math.Clamp(step, 0, StepTitles.Count - 1);
        Steps.Clear();
        for (var i = 0; i < StepTitles.Count; i++)
            Steps.Add(new StepChip($"{i + 1}. {StepTitles[i]}", i == _step, i < _step));
        Status = "";
        foreach (var name in new[] { nameof(Step), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsStep5), nameof(IsLastStep), nameof(IsNotLastStep) })
            OnPropertyChanged(name);
        switch (_step)
        {
            case 1:
                Objectives.Validate();
                break;
            case 2:
                _ = EnterDictionariesAsync();
                break;
            case 3:
                _ = CheckFoldersAsync();
                break;
            case 4:
                _ = BuildSummaryAsync();
                break;
        }
    }

    private async Task EnterDictionariesAsync()
    {
        await Try(async () =>
        {
            var state = await Busy.Run("Wczytywanie słowników globalnych…", _service.GlobalState);
            GlobalDictionaries = state.Select(g => new GlobalDictionaryState(g.Name, g.Rows, g.LastChange)).ToList();
            OnPropertyChanged(nameof(GlobalDictionaries));
        });
        await RefreshDictionaries();
    }

    private async Task CheckFoldersAsync()
    {
        OnPropertyChanged(nameof(ItRequest));
        if (Code.Length == 0)
        {
            SetAll(FolderChecks, [Issue.Error("Podaj kod projektu w kroku 1", "Kod")]);
            return;
        }
        FolderChecks.Clear();
        var code = Code;
        await Try(async () => SetAll(FolderChecks, await Busy.Run("Sprawdzanie folderów na dysku sieciowym…", () => _service.Folders.CheckBeforeCreate(code))));
    }

    // ---------- krok 3: słowniki projektu ----------

    private void BuildPanels()
    {
        Dictionaries.Clear();
        foreach (var item in ProjectDictionaries.ForType(Type.Code))
        {
            var panel = new DictionaryPanelViewModel(item, Type.Code);
            panel.Load = new AsyncRelayCommand(() => LoadDictionary(panel), () => item.Stored && !IsCreated && !Busy.IsBusy);
            panel.Remove = new AsyncRelayCommand(() => { _files.Remove(item.Code); return RefreshDictionaries(); }, () => _files.ContainsKey(item.Code) && !IsCreated && !Busy.IsBusy);
            Dictionaries.Add(panel);
        }
        foreach (var code in _files.Keys.Where(k => Dictionaries.All(p => p.Item.Code != k)).ToList())
            _files.Remove(code);
    }

    private async Task LoadDictionary(DictionaryPanelViewModel panel)
    {
        var path = _dialogs.OpenExcel($"Wczytaj słownik „{panel.Name}” (Excel albo CSV)");
        if (path is null)
            return;
        await Try(async () =>
        {
            var sheet = await Busy.Run("Odczyt arkuszy pliku…", () => ProjectService.FindSheet(path, panel.Item));
            _files[panel.Item.Code] = (path, sheet);
            await RefreshDictionaries();
        });
    }

    private async Task DoLoadWorkbook()
    {
        var path = _dialogs.OpenExcel("Wczytaj słowniki projektu – skoroszyt z arkuszami jak w szablonie");
        if (path is null)
            return;
        await Try(async () =>
        {
            var items = Dictionaries.Where(p => p.Item.Stored).Select(p => p.Item).ToList();
            var sheets = await Busy.Run("Odczyt arkuszy skoroszytu…", () => items.Select(i => (Item: i, Sheet: ProjectService.FindSheet(path, i))).ToList());
            foreach (var (item, sheet) in sheets.Where(x => x.Sheet is not null))
                _files[item.Code] = (path, sheet);
            await RefreshDictionaries();
            var found = sheets.Where(x => x.Sheet is not null).Select(x => x.Item.Name).ToList();
            Status = found.Count > 0
                ? $"Wczytano arkusze: {string.Join(", ", found)}."
                : "Skoroszyt nie ma arkuszy o nazwach słowników (jak w szablonie) – wczytaj słowniki pojedynczo.";
        });
    }

    /// <summary>Podglądy wczytanych słowników na bieżącej nakładce (w tle: odczyt plików i kontekstu z bazy).</summary>
    private async Task RefreshDictionaries()
    {
        if (Dictionaries.Count == 0)
            return;
        await Try(async () =>
        {
            var (code, tree, files, mapping) = (Code, Objectives.Tree.Copy(), new Dictionary<string, (string Path, string? Sheet)>(_files), _mapping);
            var previews = files.Count == 0 ? [] : await Busy.Run("Sprawdzanie słowników projektu…", () => _service.PreviewAll(code, tree, files, mapping));
            foreach (var panel in Dictionaries)
            {
                var file = files.TryGetValue(panel.Item.Code, out var f) ? $"{Path.GetFileName(f.Path)}{(f.Sheet is null ? "" : $" · arkusz „{f.Sheet}”")}" : "";
                panel.SetPreview(previews.GetValueOrDefault(panel.Item.Code), file);
            }
        });
    }

    private async Task DoDownloadTemplate()
    {
        var path = _dialogs.SaveExcel("Pobierz szablon słowników projektu", $"{(Code.Length > 0 ? Code : "Projekt")}_slowniki_projektu.xlsx");
        if (path is null)
            return;
        var (type, tree, mapping) = (Type.Code, Objectives.Tree.Copy(), _mapping);
        await Try(async () =>
        {
            await Busy.Run("Zapisywanie szablonu Excel…", () => _service.ExportDictionaries(path, "", type, tree, mapping));
            Status = $"Zapisano szablon {path} – arkusz „WP i CAM” zawiera elementy P1S z zakresu (Legacy WBS i mapowanie nakładki).";
        });
    }

    // ---------- krok 5: podsumowanie i utworzenie ----------

    private async Task BuildSummaryAsync()
    {
        var checks = new List<Issue>();
        checks.AddRange(ProjectRules.ValidateBasics(Code, Name, Type.Code, _codes).Where(i => i.Level == CheckLevel.Error));
        checks.AddRange(Objectives.Tree.ElementCount > 0
            ? [new Issue(CheckLevel.Pass, $"Performance Objectives: {Objectives.Summary}")]
            : []);
        checks.AddRange(ObjectivesValidator.Validate(Objectives.Tree, _owners).Where(i => i.Level == CheckLevel.Error));
        await RefreshDictionaries();
        foreach (var panel in Dictionaries)
        {
            if (!panel.Item.Stored)
                checks.Add(Issue.Warning($"{panel.Name}: zawartość nieustalona (O37) – projekt CAS będzie niegotowy", panel.Name));
            else if (panel.Preview is { HasErrors: true })
                checks.Add(Issue.Error($"{panel.Name}: plik z błędami – popraw plik albo usuń wczytanie w kroku 3", panel.Name));
            else if (panel.Preview is { } p)
                checks.Add(new Issue(CheckLevel.Pass, $"{panel.Name}: {p.Working.Count} wierszy", panel.Name));
            else if (panel.Item.IsRequired(Type.Code))
                checks.Add(Issue.Warning($"{panel.Name}: nie wczytano – słownik pusty, pierwszy przebieg zablokowany (projekt niegotowy)", panel.Name));
        }
        var wp = Dictionaries.FirstOrDefault(p => p.Item.Code == ProjectDictionaries.WpCam)?.Preview;
        var plan = Dictionaries.FirstOrDefault(p => p.Item.Code == ProjectDictionaries.ScheduleBudget)?.Preview;
        var (code, tree, mapping) = (Code, Objectives.Tree.Copy(), _mapping);
        try
        {
            var (folders, analytic) = await Busy.Run("Przygotowanie podsumowania i bazy analitycznej…", () =>
                (code.Length > 0 ? _service.Folders.CheckBeforeCreate(code).Where(i => i.Level == CheckLevel.Error).ToList() : [],
                 ProjectService.Analytic(tree, wp is { HasErrors: false } ? wp.Working : [], plan is { HasErrors: false } ? plan.Working : [], mapping)));
            checks.AddRange(folders);
            _analytic = analytic;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Kreator projektu – podsumowanie");
            Status = $"Nie udało się przygotować podsumowania: {ex.Message}";
            return;
        }
        checks.Add(ObjectivesMapping.Check(Objectives.Tree, ProjectService.Resolve(Objectives.Tree, _mapping), _mapping));
        if (_analytic.ElementsWithoutWp.Count > 0)
            checks.Add(Issue.Warning($"Elementy nakładki bez WP: {string.Join(", ", _analytic.ElementsWithoutWp.Take(15))}{(_analytic.ElementsWithoutWp.Count > 15 ? "…" : "")}", "baza analityczna"));
        if (_analytic.WpsWithoutBudget.Count > 0)
            checks.Add(Issue.Warning($"WP bez budżetu: {string.Join(", ", _analytic.WpsWithoutBudget.Take(15))}", "baza analityczna"));
        SetAll(SummaryChecks, checks);
        OnPropertyChanged(nameof(AnalyticRows));
        OnPropertyChanged(nameof(ByCam));
        OnPropertyChanged(nameof(Totals));
        OnPropertyChanged(nameof(SummaryText));
    }

    private async Task DoExportAnalytic()
    {
        var path = _dialogs.SaveExcel("Pobierz bazę analityczną", $"{(Code.Length > 0 ? Code : "Projekt")}_baza_analityczna.xlsx");
        if (path is null)
            return;
        var analytic = _analytic!;
        await Try(async () =>
        {
            await Busy.Run("Zapisywanie bazy analitycznej do Excela…", () => _service.ExportAnalytic(path, "", analytic));
            Status = $"Zapisano {path}.";
        });
    }

    private async Task DoCreate()
    {
        var (code, name, type, tree, files) = (Code, Name, Type.Code, Objectives.Tree.Copy(), new Dictionary<string, (string Path, string? Sheet)>(_files));
        await Try(async () =>
        {
            _outcome = await Busy.Run($"Tworzenie projektu {code}: zapis w bazie, słowniki, foldery…", () => _service.Create(code, name, type, tree, files));
            SetAll(CreateIssues, _outcome.Issues);
            CreatedSteps.Clear();
            foreach (var step in _outcome.Steps)
                CreatedSteps.Add(step);
            Status = _outcome.Created
                ? $"Projekt {Code} utworzony."
                : "Projekt nie został utworzony – popraw błędy (ERROR) poniżej.";
            Logger.Information("Utworzenie projektu {Code}: {Created}", Code, _outcome.Created);
            OnPropertyChanged(nameof(IsCreated));
            OnPropertyChanged(nameof(HasCreateIssues));
        });
    }

    private void SetAll(ObservableCollection<Issue> target, IEnumerable<Issue> issues)
    {
        target.Clear();
        foreach (var issue in issues)
            target.Add(issue);
        OnPropertyChanged(nameof(HasCreateIssues));
    }

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Kreator projektu");
            Status = $"Nie udało się: {ex.Message}";
        }
    }
}
