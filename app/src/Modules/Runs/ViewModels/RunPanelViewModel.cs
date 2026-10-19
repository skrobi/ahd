using System.Windows.Input;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>Etap uruchomienia w panelu: wiersz etapu, akcja i opis z definicji, elementy kontrolne.</summary>
public sealed record RunStepView(RunStepRow Row, string Gate, string Elements, string? Description)
{
    public string Title => $"{Row.StepNo}. {Row.Name}";

    public string Results => Row.Errors + Row.Warnings == 0 ? "" : $"błędy {Row.Errors}, ostrzeżenia {Row.Warnings}";

    public string When => Row.FinishedAt is { } at ? $"{at.ToLocalTime():yyyy-MM-dd HH:mm} · {Row.ActedBy}" : Row.StartedAt is { } s ? $"od {s.ToLocalTime():HH:mm}" : "";
}

/// <summary>
/// Przebieg programu (zakładka „Przebieg” programu i ekran Przebiegi – docs/funkcjonalnosc.md, F06): uruchomienia, etapy ze statusami
/// (definicja przebiegu zapisana w uruchomieniu), wyniki wybranego etapu z „Pokaż zmiany”; akcje: nowe uruchomienie, dalsze etapy,
/// potwierdzenie etapu z komentarzem, ponowne wykonanie etapu (ostatnie uruchomienie). Przebieg liczy się w tle (RunLauncher).
/// </summary>
public sealed class RunPanelViewModel : ObservableObject, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly IRunStore _store;
    private readonly RunLauncher _launcher;
    private readonly IAcwpChanges? _acwpChanges;
    private IReadOnlyList<RunPinRow> _pins = [];
    private RunPinRow? _selectedPin;
    private IReadOnlyList<RunStepView> _steps = [];
    private RunStepView? _selectedStep;
    private IReadOnlyList<RunIssueRow> _allIssues = [];
    private IReadOnlyList<RunIssueView> _issues = [];
    private string _definition = "";
    private string _comment = "";
    private string _progress = "";
    private string? _error;

    public RunPanelViewModel(IRunStore store, RunLauncher launcher, IAcwpChanges? acwpChanges, string project)
    {
        _store = store;
        _launcher = launcher;
        _acwpChanges = acwpChanges;
        Project = project;
        NewRun = new AsyncRelayCommand(() => Launch(_launcher.Start(Project)), () => !Busy.IsBusy && !_launcher.IsRunning(Project));
        Continue = new AsyncRelayCommand(() => Launch(_launcher.Continue(Project, _selectedPin!.PinId)),
            () => IsLatest && !_launcher.IsRunning(Project) && RunWorkflowEngine.Next(_steps.Select(s => s.Row).ToList()) is not null);
        Confirm = new AsyncRelayCommand(() => Launch(_launcher.Confirm(Project, _selectedPin!.PinId, _selectedStep!.Row.StepNo, Comment)),
            () => IsLatest && !_launcher.IsRunning(Project) && _selectedStep?.Row.Status == RunStepStatuses.NeedsAction && Comment.Trim().Length > 0);
        Rerun = new AsyncRelayCommand(() => Launch(_launcher.Rerun(Project, _selectedPin!.PinId, _selectedStep!.Row.StepNo)),
            () => IsLatest && !_launcher.IsRunning(Project) && _selectedStep is { } step && RunWorkflowEngine.CanRerun(_steps.Select(s => s.Row).ToList(), step.Row.StepNo));
        ShowChanges = new RelayCommand(p => { if (p is RunIssueView { Changes: { } request }) _acwpChanges?.Show(request); }, _ => _acwpChanges is not null);
        Refresh = new AsyncRelayCommand(() => Load(_selectedPin?.PinId), () => !Busy.IsBusy);
        _launcher.Progress += OnProgress;
        _launcher.Finished += OnFinished;
        if (_launcher.IsRunning(Project))
            ProgressText = "Przebieg trwa…";
        _ = Load(null);
    }

    public string Project { get; }

    public BusyState Busy { get; } = new();

    public ICommand NewRun { get; }

    public ICommand Continue { get; }

    public ICommand Confirm { get; }

    public ICommand Rerun { get; }

    public ICommand ShowChanges { get; }

    public ICommand Refresh { get; }

    public IReadOnlyList<RunPinRow> Pins { get => _pins; private set => SetProperty(ref _pins, value); }

    public RunPinRow? SelectedPin
    {
        get => _selectedPin;
        set
        {
            if (SetProperty(ref _selectedPin, value))
            {
                OnPropertyChanged(nameof(IsLatest));
                _ = LoadPin(value, null);
            }
        }
    }

    /// <summary>Akcje etapów – tylko w ostatnim uruchomieniu programu.</summary>
    public bool IsLatest => _selectedPin is not null && _pins.Count > 0 && _selectedPin.PinId == _pins[0].PinId;

    public IReadOnlyList<RunStepView> Steps { get => _steps; private set => SetProperty(ref _steps, value); }

    public RunStepView? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (SetProperty(ref _selectedStep, value))
                ShowIssues();
        }
    }

    public IReadOnlyList<RunIssueView> Issues { get => _issues; private set => SetProperty(ref _issues, value); }

    /// <summary>Definicja przebiegu uruchomienia i stan.</summary>
    public string Definition { get => _definition; private set => SetProperty(ref _definition, value); }

    /// <summary>Komentarz potwierdzenia etapu (wymagany).</summary>
    public string Comment { get => _comment; set => SetProperty(ref _comment, value); }

    public string ProgressText { get => _progress; private set => SetProperty(ref _progress, value); }

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

    public bool NoRuns => _pins.Count == 0;

    private async Task Launch(Task<string?> start)
    {
        var refused = await start;
        ProgressText = refused ?? "Przebieg w toku…";
        if (refused is null)
            Comment = "";
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnProgress(string project, string text)
    {
        if (string.Equals(project, Project, StringComparison.OrdinalIgnoreCase))
            ProgressText = text;
    }

    private void OnFinished(string project)
    {
        if (!string.Equals(project, Project, StringComparison.OrdinalIgnoreCase))
            return;
        ProgressText = "";
        _ = Load(null);
    }

    /// <summary>Uruchomienia programu; wybór – pinId albo najnowsze.</summary>
    private async Task Load(long? pinId)
    {
        try
        {
            Error = null;
            var pins = await Busy.Run($"Wczytywanie przebiegów programu {Project}…", () => _store.Pins(Project, 20));
            _pins = pins;
            OnPropertyChanged(nameof(Pins));
            OnPropertyChanged(nameof(NoRuns));
            var selected = pins.FirstOrDefault(p => p.PinId == pinId) ?? pins.FirstOrDefault();
            var step = _selectedStep?.Row.StepNo;
            _selectedPin = selected;
            OnPropertyChanged(nameof(SelectedPin));
            OnPropertyChanged(nameof(IsLatest));
            await LoadPin(selected, step);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Przebiegi programu {Project}", Project);
            Error = $"Nie udało się wczytać przebiegów: {ex.Message}";
        }
    }

    /// <summary>Etapy i wyniki uruchomienia; wybrany etap – stepNo, a bez niego etap, na którym przebieg stoi (albo ostatni).</summary>
    private async Task LoadPin(RunPinRow? pin, int? stepNo)
    {
        _allIssues = [];
        Steps = [];
        Definition = "";
        if (pin is null)
        {
            ShowIssues();
            return;
        }
        try
        {
            var (steps, issues, json) = await Busy.Run($"Wczytywanie uruchomienia {pin.Code} #{pin.PinNo}…",
                () => (_store.Steps(pin.PinId), _store.Issues(pin.PinId), _store.DefinitionOf(pin.PinId)));
            if (!ReferenceEquals(pin, _selectedPin))
                return;
            var definition = RunWorkflowEngine.Deserialize(json);
            _allIssues = issues;
            Steps = steps.Select(s =>
            {
                var step = definition?.Steps.FirstOrDefault(d => d.No == s.StepNo);
                var gate = step is null ? "" : step.NeedsApproval ? "zatwierdzenie ręczne" : step.Action == RunActions.Checks ? $"WARNING – {step.WarningGate.ToLowerInvariant()}" : "";
                var elements = step is null || step.Elements.Count == 0 ? "" : string.Join(", ", step.Elements.Where(e => e.Active).Select(e => e.Element));
                return new RunStepView(s, gate, elements, step?.Description);
            }).ToList();
            Definition = definition is null
                ? $"{pin.Code} #{pin.PinNo} – {pin.Status} (uruchomienie sprzed przebiegu etapowego – wyniki bez podziału na etapy)"
                : $"{pin.Code} #{pin.PinNo} – {pin.Status}; definicja „{definition.Code}” ({definition.Steps.Count} etapów); {pin.Summary}";
            SelectedStep = Steps.FirstOrDefault(s => s.Row.StepNo == stepNo)
                           ?? Steps.FirstOrDefault(s => s.Row.Status != RunStepStatuses.Done)
                           ?? Steps.LastOrDefault();
            ShowIssues();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Uruchomienie {Pin}", pin.PinId);
            Error = $"Nie udało się wczytać uruchomienia: {ex.Message}";
        }
    }

    // Wyniki wybranego etapu (uruchomienie bez etapów – wszystkie), od błędów.
    private void ShowIssues() =>
        Issues = _allIssues.Where(i => _steps.Count == 0 || i.StepNo == _selectedStep?.Row.StepNo)
            .OrderBy(i => i.Level == CheckLevel.Error ? 0 : i.Level == CheckLevel.Warning ? 1 : 2).ThenBy(i => i.Id)
            .Select(i => new RunIssueView(i, RunOverview.ChangesFor(i, Project))).ToList();

    public void Dispose()
    {
        _launcher.Progress -= OnProgress;
        _launcher.Finished -= OnFinished;
    }
}
