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

    public WizardViewModel(ProjectService service, IFileDialogs dialogs, Action cancel, Action<string> openProject)
    {
        _service = service;
        _dialogs = dialogs;
        _openProject = openProject;
        Types = ProjectTypes.All.Select(t => new TypeOption(t)).ToList();
        _type = Types[0];
        Objectives = new PoEditorViewModel(dialogs, service.ReadObjectives, tree => service.ValidateObjectives(Code, tree), (_, imported) => imported);
        Objectives.Changed += RefreshDictionaries;   // zmiana zakresu – ponowna walidacja wczytanych słowników
        Back = new RelayCommand(_ => GoTo(_step - 1), _ => _step > 0 && _outcome is not { Created: true });
        Next = new RelayCommand(_ => DoNext(), _ => _step < StepTitles.Count - 1);
        Cancel = new RelayCommand(_ => cancel());
        DownloadTemplate = new RelayCommand(_ => DoDownloadTemplate());
        LoadWorkbook = new RelayCommand(_ => DoLoadWorkbook());
        ExportAnalytic = new RelayCommand(_ => DoExportAnalytic(), _ => _analytic is not null);
        Create = new RelayCommand(_ => DoCreate(), _ => _outcome is not { Created: true });
        OpenProject = new RelayCommand(_ => _openProject(Code), _ => _outcome is { Created: true });
        Objectives.Load(new PoTree());
        BuildPanels();
        GoTo(0);
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
            RefreshDictionaries();
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
            SetAll(BasicIssues, _service.ValidateBasics(Code, Name, Type.Code));
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
        switch (_step)
        {
            case 1:
                Objectives.Validate();
                break;
            case 2:
                GlobalDictionaries = _service.GlobalState().Select(g => new GlobalDictionaryState(g.Name, g.Rows, g.LastChange)).ToList();
                OnPropertyChanged(nameof(GlobalDictionaries));
                RefreshDictionaries();
                break;
            case 3:
                SetAll(FolderChecks, Code.Length > 0 ? _service.Folders.CheckBeforeCreate(Code) : [Issue.Error("Podaj kod projektu w kroku 1", "Kod")]);
                OnPropertyChanged(nameof(ItRequest));
                break;
            case 4:
                BuildSummary();
                break;
        }
        foreach (var name in new[] { nameof(Step), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsStep5), nameof(IsLastStep), nameof(IsNotLastStep) })
            OnPropertyChanged(name);
    }

    // ---------- krok 3: słowniki projektu ----------

    private void BuildPanels()
    {
        Dictionaries.Clear();
        foreach (var item in ProjectDictionaries.ForType(Type.Code))
        {
            var panel = new DictionaryPanelViewModel(item, Type.Code);
            panel.Load = new RelayCommand(_ => LoadDictionary(panel), _ => item.Stored && !IsCreated);
            panel.Remove = new RelayCommand(_ => { _files.Remove(item.Code); RefreshDictionaries(); }, _ => _files.ContainsKey(item.Code) && !IsCreated);
            Dictionaries.Add(panel);
        }
        foreach (var code in _files.Keys.Where(k => Dictionaries.All(p => p.Item.Code != k)).ToList())
            _files.Remove(code);
    }

    private void LoadDictionary(DictionaryPanelViewModel panel)
    {
        var path = _dialogs.OpenExcel($"Wczytaj słownik „{panel.Name}” (Excel albo CSV)");
        if (path is null)
            return;
        Try(() =>
        {
            _files[panel.Item.Code] = (path, ProjectService.FindSheet(path, panel.Item));
            RefreshDictionaries();
        });
    }

    private void DoLoadWorkbook()
    {
        var path = _dialogs.OpenExcel("Wczytaj słowniki projektu – skoroszyt z arkuszami jak w szablonie");
        if (path is null)
            return;
        Try(() =>
        {
            var found = new List<string>();
            foreach (var panel in Dictionaries.Where(p => p.Item.Stored))
            {
                if (ProjectService.FindSheet(path, panel.Item) is { } sheet)
                {
                    _files[panel.Item.Code] = (path, sheet);
                    found.Add(panel.Name);
                }
            }
            RefreshDictionaries();
            Status = found.Count > 0
                ? $"Wczytano arkusze: {string.Join(", ", found)}."
                : "Skoroszyt nie ma arkuszy o nazwach słowników (jak w szablonie) – wczytaj słowniki pojedynczo.";
        });
    }

    private void RefreshDictionaries()
    {
        if (Dictionaries.Count == 0)
            return;
        Try(() =>
        {
            var previews = _files.Count == 0 ? [] : _service.PreviewAll(Code, Objectives.Tree, _files);
            foreach (var panel in Dictionaries)
            {
                var file = _files.TryGetValue(panel.Item.Code, out var f) ? $"{Path.GetFileName(f.Path)}{(f.Sheet is null ? "" : $" · arkusz „{f.Sheet}”")}" : "";
                panel.SetPreview(previews.GetValueOrDefault(panel.Item.Code), file);
            }
        });
    }

    private void DoDownloadTemplate()
    {
        var path = _dialogs.SaveExcel("Pobierz szablon słowników projektu", $"{(Code.Length > 0 ? Code : "Projekt")}_slowniki_projektu.xlsx");
        if (path is null)
            return;
        Try(() =>
        {
            _service.ExportDictionaries(path, "", Type.Code, Objectives.Tree);
            Status = $"Zapisano szablon {path} – arkusz „WP i CAM” zawiera elementy P1S z zakresu (Legacy WBS nakładki).";
        });
    }

    // ---------- krok 5: podsumowanie i utworzenie ----------

    private void BuildSummary()
    {
        var checks = new List<Issue>();
        checks.AddRange(_service.ValidateBasics(Code, Name, Type.Code).Where(i => i.Level == CheckLevel.Error));
        checks.AddRange(Objectives.Tree.ElementCount > 0
            ? [new Issue(CheckLevel.Pass, $"Performance Objectives: {Objectives.Summary}")]
            : []);
        checks.AddRange(_service.ValidateObjectives(Code, Objectives.Tree).Where(i => i.Level == CheckLevel.Error));
        RefreshDictionaries();
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
        checks.AddRange(Code.Length > 0 ? _service.Folders.CheckBeforeCreate(Code).Where(i => i.Level == CheckLevel.Error) : []);

        var wp = Dictionaries.FirstOrDefault(p => p.Item.Code == ProjectDictionaries.WpCam)?.Preview;
        var plan = Dictionaries.FirstOrDefault(p => p.Item.Code == ProjectDictionaries.ScheduleBudget)?.Preview;
        _analytic = ProjectService.Analytic(Objectives.Tree, wp is { HasErrors: false } ? wp.Working : [], plan is { HasErrors: false } ? plan.Working : []);
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

    private void DoExportAnalytic()
    {
        var path = _dialogs.SaveExcel("Pobierz bazę analityczną", $"{(Code.Length > 0 ? Code : "Projekt")}_baza_analityczna.xlsx");
        if (path is null)
            return;
        Try(() =>
        {
            _service.ExportAnalytic(path, "", _analytic!);
            Status = $"Zapisano {path}.";
        });
    }

    private void DoCreate()
    {
        Try(() =>
        {
            _outcome = _service.Create(Code, Name, Type.Code, Objectives.Tree, _files);
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

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException or Microsoft.Data.SqlClient.SqlException)
        {
            Logger.Error(ex, "Kreator projektu");
            Status = $"Nie udało się: {ex.Message}";
        }
    }
}
