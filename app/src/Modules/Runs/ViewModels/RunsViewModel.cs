using System.Windows.Input;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>
/// Ekran Przebiegi (docs/funkcjonalnosc.md, F06): przebieg wybranego programu (panel etapów – RunPanelViewModel), przegląd programów
/// (ostatnie uruchomienie i etap, na którym stoi), macierz wyników kontroli ostatnich uruchomień (klik w komórkę – wyniki tej
/// kontroli), definicja przebiegu programu (etapy i elementy kontrolne z ustawieniami, wariant przebiegu) oraz edycja definicji
/// przebiegów, reguł uzgodnień i ustawień kontroli (RunDictionaryEditorViewModel).
/// </summary>
public sealed class RunsViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    /// <summary>Liczba uruchomień w macierzy.</summary>
    public const int PinCount = 8;

    private readonly IRunStore _store;
    private readonly Func<string, RunPanelViewModel> _panels;
    private IReadOnlyList<RunProjectRow> _projects = [];
    private RunProjectRow? _selectedProject;
    private RunPanelViewModel? _panel;
    private IReadOnlyList<RunProgramRow> _programs = [];
    private RunMatrix _matrix = new([], []);
    private Rules _rules = new(([], [], []), [], []);
    private bool _closing;
    private int _selectedTab;
    private IReadOnlyList<RunWorkflowRow> _workflow = [];
    private IReadOnlyList<RunReconciliationView> _reconciliations = [];
    private IReadOnlyList<RunIssueView> _cellIssues = [];
    private string _cellTitle = "";
    private string _workflowNote = "";
    private string? _error;
    private Dictionary<long, IReadOnlyList<RunIssueRow>> _issues = [];
    private Dictionary<long, string> _pinLabels = [];

    public RunsViewModel(IRunStore store, Func<string, RunPanelViewModel> panels, IAcwpChanges? acwpChanges, RunDictionaryEditorViewModel editor)
    {
        _store = store;
        _panels = panels;
        Editor = editor;
        Editor.Saved += () => _ = ReloadRules();
        Refresh = new AsyncRelayCommand(LoadProjects, () => !Busy.IsBusy);
        OpenAcwpChanges = new RelayCommand(_ => acwpChanges?.Show(new AcwpChangesRequest(SelectedProject!.Code, Scope: "cały program")),
            _ => acwpChanges is not null && SelectedProject is not null && Matrix.Columns.Count > 0);
        ShowChanges = new RelayCommand(p => { if (p is RunIssueView { Changes: { } request }) acwpChanges?.Show(request); }, _ => acwpChanges is not null);
        SelectCell = new RelayCommand(p => ShowCell(p as RunMatrixCell));
        OpenProgram = new RelayCommand(p =>
        {
            if (p is RunProgramRow row && Projects.FirstOrDefault(r => r.Code == row.Code) is { } project)
            {
                SelectedProject = project;
                SelectedTab = 0;
            }
        });
        _ = LoadProjects();
    }

    public BusyState Busy { get; } = new();

    public ICommand Refresh { get; }

    public ICommand SelectCell { get; }

    /// <summary>Przegląd programów – przejście do przebiegu programu.</summary>
    public ICommand OpenProgram { get; }

    /// <summary>„Pokaż zmiany” przy wyniku kontroli – okno „Zmiany ACWP” z zakresem wyniku (RunOverview.ChangesFor).</summary>
    public ICommand ShowChanges { get; }

    /// <summary>Okno „Zmiany ACWP” programu (porównanie uruchomień albo okresów).</summary>
    public ICommand OpenAcwpChanges { get; }

    /// <summary>Definicje przebiegów, etapy, elementy kontrolne, reguły uzgodnień, ustawienia kontroli – edycja słowników.</summary>
    public RunDictionaryEditorViewModel Editor { get; }

    public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }

    public IReadOnlyList<RunProjectRow> Projects { get => _projects; private set => SetProperty(ref _projects, value); }

    public RunProjectRow? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
                _ = LoadProject(value);
        }
    }

    /// <summary>Przebieg wybranego programu (etapy, potwierdzenia, ponowne wykonanie).</summary>
    public RunPanelViewModel? Panel
    {
        get => _panel;
        private set
        {
            var previous = _panel;
            if (SetProperty(ref _panel, value))
                previous?.Dispose();
        }
    }

    /// <summary>Wszystkie programy: ostatnie uruchomienie i etap, na którym stoi.</summary>
    public IReadOnlyList<RunProgramRow> Programs { get => _programs; private set => SetProperty(ref _programs, value); }

    public RunMatrix Matrix
    {
        get => _matrix;
        private set
        {
            if (SetProperty(ref _matrix, value))
                OnPropertyChanged(nameof(NoRuns));
        }
    }

    public bool NoRuns => SelectedProject is not null && Matrix.Columns.Count == 0;

    /// <summary>Wariant definicji przebiegu na zakładce „Definicja przebiegu”: zamykający okres albo w środku okresu.</summary>
    public bool ShowClosing
    {
        get => _closing;
        set
        {
            if (SetProperty(ref _closing, value))
                ShowWorkflow();
        }
    }

    /// <summary>Definicja przebiegu programu: etapy i elementy kontrolne z ustawieniami.</summary>
    public IReadOnlyList<RunWorkflowRow> Workflow { get => _workflow; private set => SetProperty(ref _workflow, value); }

    /// <summary>Opis definicji: nazwa, zakres, źródło (słownik albo wbudowana).</summary>
    public string WorkflowNote { get => _workflowNote; private set => SetProperty(ref _workflowNote, value); }

    /// <summary>Reguły uzgodnień dla typu programu.</summary>
    public IReadOnlyList<RunReconciliationView> Reconciliations { get => _reconciliations; private set => SetProperty(ref _reconciliations, value); }

    /// <summary>Miary dostępne w formułach reguł uzgodnień.</summary>
    public IReadOnlyList<RunMeasure> Measures => RunMeasures.All;

    public IReadOnlyList<RunIssueView> CellIssues { get => _cellIssues; private set => SetProperty(ref _cellIssues, value); }

    public string CellTitle { get => _cellTitle; private set => SetProperty(ref _cellTitle, value); }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;

    private async Task LoadProjects()
    {
        await Try(async () =>
        {
            var selected = SelectedProject?.Code;
            var loaded = await Busy.Run<Overview>("Wczytywanie programów i przebiegów…", () =>
            {
                var projects = _store.Projects();
                var latest = _store.Latest();
                return new Overview(projects, latest, latest.Values.ToDictionary(p => p.PinId, p => _store.Steps(p.PinId)), LoadRules());
            });
            Projects = loaded.Projects;
            Programs = RunOverview.Programs(loaded.Projects, loaded.Latest, loaded.Steps);
            _rules = loaded.Rules;
            // Odśwież – ten sam program wczytany ponownie (rekord nowej listy jest równy poprzedniemu, setter by go pominął).
            _selectedProject = Projects.FirstOrDefault(p => p.Code == selected) ?? Projects.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedProject));
            await LoadProject(_selectedProject);
        });
    }

    /// <summary>Słowniki przebiegu: definicje (etapy, elementy), „Kontrole przebiegu”, reguły uzgodnień.</summary>
    private Rules LoadRules() => new(_store.DefinitionRows(), _store.CheckSettings(), _store.Reconciliations());

    /// <summary>Po zapisie słowników przebiegu: definicja przebiegu programu i reguły uzgodnień od nowa.</summary>
    private async Task ReloadRules()
    {
        await Try(async () =>
        {
            _rules = await Busy.Run("Wczytywanie definicji przebiegów…", LoadRules);
            ShowWorkflow();
        });
    }

    private async Task LoadProject(RunProjectRow? project)
    {
        CellIssues = [];
        CellTitle = "";
        Panel = project is null ? null : _panels(project.Code);
        ShowWorkflow();
        if (project is null)
        {
            Matrix = new([], []);
            return;
        }
        await Try(async () =>
        {
            var loaded = await Busy.Run<Loaded>($"Wczytywanie przebiegów programu {project.Code}…", () =>
            {
                var pins = _store.Pins(project.Code, PinCount);
                return new Loaded(pins, pins.ToDictionary(p => p.PinId, p => _store.Issues(p.PinId)));
            });
            if (!ReferenceEquals(project, _selectedProject))
                return;
            _issues = loaded.Issues;
            _pinLabels = loaded.Pins.ToDictionary(p => p.PinId, p => $"{p.Code} #{p.PinNo}");
            Matrix = RunOverview.Matrix(loaded.Pins, loaded.Issues);
            var worst = Matrix.Rows.SelectMany(r => r.Cells).Where(c => c.PinId == Matrix.Columns.LastOrDefault()?.PinId && c.Level is not null)
                .OrderByDescending(c => c.Level).FirstOrDefault();
            ShowCell(worst);
        });
    }

    /// <summary>Definicja przebiegu wybranego programu w wybranym wariancie (jak w RunService: program, typ, wariant; brak – wbudowana).</summary>
    private void ShowWorkflow()
    {
        if (_selectedProject is not { } project)
        {
            Workflow = [];
            Reconciliations = [];
            WorkflowNote = "";
            return;
        }
        var (headers, steps, elements) = _rules.Definitions;
        var definition = RunDefinitions.For(headers, steps, elements, project.Code, project.ProjectType, _closing);
        Workflow = RunOverview.Workflow(definition, RunChecks.Settings(_rules.Checks, project.ProjectType, _closing), _rules.Reconciliations,
            project.ProjectType, _closing);
        Reconciliations = RunOverview.Reconciliations(_rules.Reconciliations, project.ProjectType);
        var scope = definition.BuiltIn ? "brak aktywnej definicji w słownikach dla programu i typu"
            : $"typ {definition.ProjectType}{(definition.Project is { } p ? $", program {p}" : "")}, przebieg {definition.RunKind}";
        WorkflowNote = $"Program {project.Code} (typ {project.ProjectType}), przebieg {(_closing ? "zamykający okres" : "w środku okresu")}: "
            + $"definicja „{definition.Code}” – {scope}; etapy: {definition.Steps.Count}. {definition.Description}";
    }

    /// <summary>Przegląd programów: programy, ostatnie uruchomienia, ich etapy, słowniki przebiegu.</summary>
    private sealed record Overview(IReadOnlyList<RunProjectRow> Projects, IReadOnlyDictionary<string, RunPinRow> Latest,
        Dictionary<long, IReadOnlyList<RunStepRow>> Steps, Rules Rules);

    /// <summary>Słowniki przebiegu: definicje przebiegów, „Kontrole przebiegu”, „Reguły uzgodnień”.</summary>
    private sealed record Rules(
        (IReadOnlyList<RunDefinitionHeaderRow> Headers, IReadOnlyList<RunStepRowData> Steps, IReadOnlyList<RunStepElementRow> Elements) Definitions,
        IReadOnlyList<RunCheckRow> Checks, IReadOnlyList<RunReconciliationRow> Reconciliations);

    /// <summary>Dane programu z bazy: uruchomienia i ich wyniki.</summary>
    private sealed record Loaded(IReadOnlyList<RunPinRow> Pins, Dictionary<long, IReadOnlyList<RunIssueRow>> Issues);

    private void ShowCell(RunMatrixCell? cell)
    {
        if (cell is null)
        {
            CellIssues = [];
            CellTitle = "";
            return;
        }
        CellIssues = (_issues.GetValueOrDefault(cell.PinId) ?? []).Where(i => i.CheckName == cell.CheckName)
            .OrderBy(i => i.Level == CheckLevel.Error ? 0 : i.Level == CheckLevel.Warning ? 1 : 2).ThenBy(i => i.Id)
            .Select(i => new RunIssueView(i, SelectedProject is { } project ? RunOverview.ChangesFor(i, project.Code) : null)).ToList();
        CellTitle = $"{cell.CheckName} – {_pinLabels.GetValueOrDefault(cell.PinId)}: {cell.Text}";
    }

    private async Task Try(Func<Task> action)
    {
        try
        {
            Error = null;
            await action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Przebiegi");
            Error = $"Nie udało się wczytać przebiegów: {ex.Message}";
        }
    }
}
