using System.Windows.Input;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
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
/// Przebieg programu (zakładka „Przebieg” programu – docs/funkcjonalnosc.md, F06): przebieg wybrany w Konfiguracji (albo domyślny dla
/// typu), uruchomienia, etapy ze statusami (definicja zapisana w uruchomieniu), wyniki wybranego etapu z „Pokaż zmiany”; akcje: nowe
/// uruchomienie, dalsze etapy, potwierdzenie etapu z komentarzem, ponowne wykonanie etapu (ostatnie uruchomienie). Porównania: macierz
/// reguła × ostatnie uruchomienia, porównanie dwóch uruchomień (wiersze wyników reguł), okno „Zmiany ACWP”. Przebieg liczy się w tle (RunLauncher).
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
    private string _assigned = "";
    private RunMatrix _matrix = new([], []);
    private IReadOnlyList<RunIssueView> _cellIssues = [];
    private string _cellTitle = "";
    private RunPinRow? _beforePin;
    private RunPinRow? _afterPin;
    private bool _onlyChanges = true;
    private IReadOnlyList<RunCompareRow> _compare = [];
    private string _compareNote = "";
    private readonly Dictionary<long, IReadOnlyList<RunIssueRow>> _issueCache = [];

    /// <summary>Liczba uruchomień w macierzy kontroli.</summary>
    public const int MatrixPins = 8;

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
        SelectCell = new RelayCommand(p => ShowCell(p as RunMatrixCell));
        OpenAcwpChanges = new RelayCommand(_ => _acwpChanges?.Show(new AcwpChangesRequest(Project, PinId: _afterPin?.PinId, Scope: "cały program")),
            _ => _acwpChanges is not null && _pins.Count > 0);
        SwapCompare = new RelayCommand(_ => { (_beforePin, _afterPin) = (_afterPin, _beforePin); OnPropertyChanged(nameof(BeforePin)); OnPropertyChanged(nameof(AfterPin)); _ = LoadCompare(); },
            _ => _beforePin is not null && _afterPin is not null);
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

    /// <summary>Komórka macierzy – wyniki kontroli w uruchomieniu.</summary>
    public ICommand SelectCell { get; }

    /// <summary>Okno „Zmiany ACWP” programu (porównanie uruchomień albo okresów).</summary>
    public ICommand OpenAcwpChanges { get; }

    public ICommand SwapCompare { get; }

    /// <summary>Przebieg programu: szablon wybrany w Konfiguracji albo domyślny dla typu (zmiana – Konfiguracja → Parametry programu → Przebieg).</summary>
    public string Assigned { get => _assigned; private set => SetProperty(ref _assigned, value); }

    public RunMatrix Matrix { get => _matrix; private set => SetProperty(ref _matrix, value); }

    public IReadOnlyList<RunIssueView> CellIssues { get => _cellIssues; private set => SetProperty(ref _cellIssues, value); }

    public string CellTitle { get => _cellTitle; private set => SetProperty(ref _cellTitle, value); }

    /// <summary>Porównanie: uruchomienie „było”.</summary>
    public RunPinRow? BeforePin { get => _beforePin; set { if (SetProperty(ref _beforePin, value)) _ = LoadCompare(); } }

    /// <summary>Porównanie: uruchomienie „jest”.</summary>
    public RunPinRow? AfterPin { get => _afterPin; set { if (SetProperty(ref _afterPin, value)) _ = LoadCompare(); } }

    /// <summary>Porównanie: tylko nowe, zmienione i rozwiązane wyniki.</summary>
    public bool OnlyChanges { get => _onlyChanges; set { if (SetProperty(ref _onlyChanges, value)) _ = LoadCompare(); } }

    public IReadOnlyList<RunCompareRow> Compare { get => _compare; private set => SetProperty(ref _compare, value); }

    public string CompareNote { get => _compareNote; private set => SetProperty(ref _compareNote, value); }

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
            var loaded = await Busy.Run<Loaded>($"Wczytywanie przebiegów programu {Project}…", () =>
            {
                var pins = _store.Pins(Project, 20);
                var (headers, steps, elements) = _store.DefinitionRows();
                var assigned = _store.AssignedDefinition(Project);
                var definition = RunDefinitions.For(headers, steps, elements, assigned, _store.ProjectType(Project) ?? "");
                var matrixPins = pins.Where(p => p.Status != RunStatuses.Running).Take(MatrixPins).ToList();
                return new Loaded(pins, definition, assigned, matrixPins.ToDictionary(p => p.PinId, p => _store.Issues(p.PinId)),
                    _store.Rules().Select(r => r.Name).ToList());
            });
            var pins = loaded.Pins;
            _pins = pins;
            _issueCache.Clear();
            foreach (var (pin, issues) in loaded.Issues)
                _issueCache[pin] = issues;
            Assigned = loaded.Definition.BuiltIn ? $"Przebieg: definicja wbudowana ({loaded.Definition.Steps.Count} etapów) – brak aktywnych szablonów (ekran Przebiegi)."
                : $"Przebieg: „{loaded.Definition.Code}” ({loaded.Definition.Steps.Count} etapów) – "
                  + (loaded.Assigned is null ? $"domyślny dla typu programu; wybór – Konfiguracja → Parametry programu → {ProjectSettings.RunDefinition}."
                     : string.Equals(loaded.Assigned, loaded.Definition.Code, StringComparison.OrdinalIgnoreCase) ? "wybrany w Konfiguracji programu."
                     : $"wybrany „{loaded.Assigned}” jest nieaktywny albo bez etapów, obowiązuje domyślny dla typu.");
            Matrix = RunOverview.Matrix(pins.Where(p => loaded.Issues.ContainsKey(p.PinId)).ToList(), loaded.Issues, loaded.Rules);
            var worst = Matrix.Rows.SelectMany(r => r.Cells).Where(c => c.PinId == Matrix.Columns.LastOrDefault()?.PinId && c.Level is not null)
                .OrderByDescending(c => c.Level).FirstOrDefault();
            ShowCell(worst);
            var done = pins.Where(p => p.Status != RunStatuses.Running).ToList();
            _afterPin = done.FirstOrDefault();
            _beforePin = done.Skip(1).FirstOrDefault();
            OnPropertyChanged(nameof(AfterPin));
            OnPropertyChanged(nameof(BeforePin));
            await LoadCompare();
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

    /// <summary>Dane programu: uruchomienia, przebieg programu, wybór w Konfiguracji, wyniki uruchomień macierzy.</summary>
    private sealed record Loaded(IReadOnlyList<RunPinRow> Pins, RunDefinition Definition, string? Assigned, Dictionary<long, IReadOnlyList<RunIssueRow>> Issues,
        IReadOnlyList<string> Rules);

    private void ShowCell(RunMatrixCell? cell)
    {
        if (cell is null)
        {
            CellIssues = [];
            CellTitle = "";
            return;
        }
        CellIssues = (_issueCache.GetValueOrDefault(cell.PinId) ?? []).Where(i => i.CheckName == cell.CheckName)
            .OrderBy(i => i.Level == CheckLevel.Error ? 0 : i.Level == CheckLevel.Warning ? 1 : 2).ThenBy(i => i.Id)
            .Select(i => new RunIssueView(i, RunOverview.ChangesFor(i, Project))).ToList();
        var pin = _pins.FirstOrDefault(p => p.PinId == cell.PinId);
        CellTitle = $"{cell.CheckName} – {pin?.Code} #{pin?.PinNo}: {cell.Text}";
    }

    /// <summary>Porównanie wyników kontroli uruchomień „było” i „jest” (RunOverview.Compare).</summary>
    private async Task LoadCompare()
    {
        if (_beforePin is not { } before || _afterPin is not { } after)
        {
            Compare = [];
            CompareNote = _pins.Count < 2 ? "Porównanie wymaga dwóch zakończonych uruchomień." : "Wybierz uruchomienia „było” i „jest”.";
            return;
        }
        try
        {
            var issues = await Busy.Run("Porównanie uruchomień…", () =>
                new[] { before, after }.ToDictionary(p => p.PinId, p => _issueCache.GetValueOrDefault(p.PinId) ?? _store.Issues(p.PinId)));
            foreach (var (pin, rows) in issues)
                _issueCache[pin] = rows;
            if (!ReferenceEquals(before, _beforePin) || !ReferenceEquals(after, _afterPin))
                return;
            var all = RunOverview.Compare(issues[before.PinId], issues[after.PinId], Project);
            Compare = _onlyChanges ? all.Where(r => r.Status != RunCompareRow.Same).ToList() : all;
            CompareNote = $"{before.Code} #{before.PinNo} → {after.Code} #{after.PinNo}: nowe {all.Count(r => r.Status == RunCompareRow.New)}, "
                + $"zmienione {all.Count(r => r.Status == RunCompareRow.Changed)}, rozwiązane {all.Count(r => r.Status == RunCompareRow.Resolved)}, "
                + $"bez zmian {all.Count(r => r.Status == RunCompareRow.Same)}.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Porównanie uruchomień {Before} {After}", before.PinId, after.PinId);
            Error = $"Nie udało się porównać uruchomień: {ex.Message}";
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
