using System.Data;
using System.Windows.Input;
using System.Windows.Threading;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>Etap uruchomienia w panelu: wiersz etapu, akcja i opis z definicji.</summary>
public sealed record RunStepView(RunStepRow Row, string Gate, string? Description)
{
    public string Title => $"{Row.StepNo}. {Row.Name}";

    /// <summary>Etap zatrzymany na błędzie – potwierdzenie akceptuje błędy (RunWorkflowEngine.CanConfirm); Zatwierdzenie ACTUALS – dane do struktury.</summary>
    public string ConfirmText => Row.Status == RunStepStatuses.Failed ? "Zaakceptuj błędy i dalej"
        : Row.Action == RunActions.ApproveActuals ? "Zatwierdź dane ACTUALS i dalej" : "Potwierdź i dalej";

    public bool HasComment => !string.IsNullOrWhiteSpace(Row.Comment);

    public string Results => Row.Errors + Row.Warnings == 0 ? "" : $"błędy {Row.Errors}, ostrzeżenia {Row.Warnings}";

    public string When => Row.FinishedAt is { } at ? $"{at.ToLocalTime():yyyy-MM-dd HH:mm} · {Row.ActedBy}" : Row.StartedAt is { } s ? $"od {s.ToLocalTime():HH:mm}" : "";
}

/// <summary>Linia ACTUALS w zrzucie wiersza wyniku reguły.</summary>
public sealed record RunActualsLineView(RunActualsLine Line)
{
    public string Period => Line.FiscalYear is { } year && Line.Period is { } period ? $"{year}/{period:00}" : "";
}

/// <summary>
/// Przebieg programu (zakładka „Przebieg” programu – docs/funkcjonalnosc.md, F06): przebieg wybrany w Konfiguracji (albo domyślny dla
/// typu), uruchomienia, etapy ze statusami (definicja zapisana w uruchomieniu), wyniki wybranego etapu z „Pokaż zmiany”; akcje: nowe
/// uruchomienie, dalsze etapy, potwierdzenie etapu z komentarzem, ponowne wykonanie etapu (ostatnie uruchomienie). Wyniki etapu: reguły
/// z liczbą problemów, wybrana reguła – kontekst (co sprawdza, dane, zakres), wynik zapytania z problemem w wierszu, linie ACTUALS
/// zaznaczonego wiersza (zakres z kolumn wiersza), do Excela. Porównania: macierz reguła × ostatnie uruchomienia, porównanie dwóch
/// uruchomień (wiersze wyników reguł), okno „Zmiany ACWP”. Przebieg liczy się w tle (RunLauncher).
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
    private IReadOnlyList<RunRuleResultView> _ruleResults = [];
    private RunRuleResultView? _selectedRuleResult;
    private RunResultTable _resultTable = new([], []);
    private DataView? _resultView;
    private object? _selectedResultRow;
    private RunIssueView? _selectedIssue;
    private IReadOnlyList<RunActualsLineView> _lines = [];
    private string _linesTitle = "";
    private RunActualsScope? _linesScope;
    private string _ruleContext = "";
    private string _ruleData = "";
    private string _resultHint = "";
    private IReadOnlySet<string> _numericColumns = new HashSet<string>();
    private string _runScope = "";
    private IReadOnlyList<RunRule> _library = [];
    private readonly IFileDialogs _dialogs;
    private string _definition = "";
    private string _comment = "";
    private string _progress = "";
    private string _notice = "";
    private string? _pinChanges;
    private DateTime? _runStarted;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _error;
    private string _assigned = "";
    private RunMatrix _matrix = new([], []);
    private IReadOnlyList<RunIssueView> _cellIssues = [];
    private string _cellTitle = "";
    private RunPinRow? _beforePin;
    private RunPinRow? _afterPin;
    private bool _onlyChanges = true;
    private bool _onlyClosings;
    private IReadOnlyList<RunPinRow> _closings = [];
    private IReadOnlyList<RunCompareRow> _compare = [];
    private string _compareNote = "";
    private readonly Dictionary<long, IReadOnlyList<RunIssueRow>> _issueCache = [];

    /// <summary>Liczba uruchomień w macierzy kontroli.</summary>
    public const int MatrixPins = 8;

    /// <summary>Najwyżej tyle linii ACTUALS w zrzucie na ekranie (do Excela – ExportLines).</summary>
    public const int LineCount = 500;
    private const int ExportLines = 100_000;

    public RunPanelViewModel(IRunStore store, RunLauncher launcher, IAcwpChanges? acwpChanges, string project, IFileDialogs dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        _launcher = launcher;
        _acwpChanges = acwpChanges;
        Project = project;
        // Bez kontroli „na boku” – czego wymaga przebieg, sprawdzają reguły etapów przebiegu (docs/pipeline-fazy.md, rozdz. 4.1).
        NewRun = new AsyncRelayCommand(() => Launch(_launcher.Start(Project)), () => !Busy.IsBusy && !_launcher.IsRunning(Project));
        Continue = new AsyncRelayCommand(() => Launch(_launcher.Continue(Project, _selectedPin!.PinId)),
            () => IsLatest && !_launcher.IsRunning(Project) && RunWorkflowEngine.Next(_steps.Select(s => s.Row).ToList()) is not null);
        Confirm = new AsyncRelayCommand(() => Launch(_launcher.Confirm(Project, _selectedPin!.PinId, _selectedStep!.Row.StepNo, Comment)),
            () => IsLatest && !_launcher.IsRunning(Project) && _selectedStep is { } step && RunWorkflowEngine.CanConfirm(_steps.Select(s => s.Row).ToList(), step.Row.StepNo)
                  && Comment.Trim().Length > 0);
        Rerun = new AsyncRelayCommand(() => Launch(_launcher.Rerun(Project, _selectedPin!.PinId, _selectedStep!.Row.StepNo)),
            () => IsLatest && !_launcher.IsRunning(Project) && _selectedStep is { } step && RunWorkflowEngine.CanRerun(_steps.Select(s => s.Row).ToList(), step.Row.StepNo));
        ShowChanges = new RelayCommand(p => { if (p is RunIssueView { Changes: { } request }) _acwpChanges?.Show(request); }, _ => _acwpChanges is not null);
        Fix = new RelayCommand(p => { if (p is RunIssueView { Fix: { } link }) FixRequested?.Invoke(link); });
        ExportResult = new AsyncRelayCommand(DoExportResult, () => !Busy.IsBusy && _selectedRuleResult is not null);
        Refresh = new AsyncRelayCommand(() => Load(_selectedPin?.PinId), () => !Busy.IsBusy);
        SelectCell = new RelayCommand(p => ShowCell(p as RunMatrixCell));
        OpenAcwpChanges = new RelayCommand(_ => _acwpChanges?.Show(new AcwpChangesRequest(Project, PinId: _afterPin?.PinId, Scope: "cały program")),
            _ => _acwpChanges is not null && _pins.Count > 0);
        SwapCompare = new RelayCommand(_ => { (_beforePin, _afterPin) = (_afterPin, _beforePin); OnPropertyChanged(nameof(BeforePin)); OnPropertyChanged(nameof(AfterPin)); _ = LoadCompare(); },
            _ => _beforePin is not null && _afterPin is not null);
        _timer.Tick += (_, _) => OnPropertyChanged(nameof(ProgressText));
        _launcher.Progress += OnProgress;
        _launcher.Finished += OnFinished;
        if (_launcher.IsRunning(Project))
            StartProgress("Przebieg trwa…");
        _ = Load(null);
    }

    public string Project { get; }

    public BusyState Busy { get; } = new();

    public ICommand NewRun { get; }

    public ICommand Continue { get; }

    public ICommand Confirm { get; }

    public ICommand Rerun { get; }

    public ICommand ShowChanges { get; }

    /// <summary>„Popraw w aplikacji”: słownik z kolumn Slownik i Filtr wyniku – pokazuje go ekran programu (FixRequested).</summary>
    public ICommand Fix { get; }

    public event Action<DictionaryLink>? FixRequested;

    /// <summary>Wynik reguły i linie ACTUALS zaznaczonego wiersza do Excela.</summary>
    public ICommand ExportResult { get; }

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

    /// <summary>
    /// Uruchomienia do porównania: ostatnie uruchomienia i wszystkie zamknięcia okresów (RunPinRow.IsPeriodClose), od najnowszego;
    /// OnlyClosings – same zamknięcia (porównanie „od zamknięcia do zamknięcia”).
    /// </summary>
    public IReadOnlyList<RunPinRow> ComparePins => _onlyClosings ? _closings
        : _pins.Concat(_closings.Where(c => _pins.All(p => p.PinId != c.PinId))).OrderByDescending(p => p.StartedAt).ToList();

    /// <summary>Porównanie tylko zamknięć okresów – wybór: dwa ostatnie zamknięcia.</summary>
    public bool OnlyClosings
    {
        get => _onlyClosings;
        set
        {
            if (!SetProperty(ref _onlyClosings, value))
                return;
            OnPropertyChanged(nameof(ComparePins));
            if (value)
            {
                (_afterPin, _beforePin) = (_closings.FirstOrDefault(), _closings.Skip(1).FirstOrDefault());
                OnPropertyChanged(nameof(AfterPin));
                OnPropertyChanged(nameof(BeforePin));
                _ = LoadCompare();
            }
        }
    }

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

    /// <summary>Wyniki wybranego etapu po regułach: poziom, nazwa, liczba problemów.</summary>
    public IReadOnlyList<RunRuleResultView> RuleResults { get => _ruleResults; private set => SetProperty(ref _ruleResults, value); }

    public RunRuleResultView? SelectedRuleResult
    {
        get => _selectedRuleResult;
        set
        {
            if (SetProperty(ref _selectedRuleResult, value))
                ShowRule();
        }
    }

    /// <summary>Kontekst wybranej reguły: co sprawdza i na jakich danych.</summary>
    public string RuleContext { get => _ruleContext; private set => SetProperty(ref _ruleContext, value); }

    /// <summary>Dane użyte w zapytaniu reguły (tabele z opisem) – podpowiedź kontekstu razem z zakresem uruchomienia.</summary>
    public string RuleData { get => _ruleData; private set { if (SetProperty(ref _ruleData, value)) OnPropertyChanged(nameof(RuleDataTip)); } }

    public string RuleDataTip => string.Join("\n", new[] { RuleData, RunScope }.Where(t => t.Length > 0));

    /// <summary>Wskazówka pod tabelą wyniku: wspólna dla wszystkich wierszy albo zaznaczonego wiersza.</summary>
    public string ResultHint { get => _resultHint; private set => SetProperty(ref _resultHint, value); }

    /// <summary>Kolumna techniczna tabeli wyniku – numer wiersza wyniku (ukryta w widoku).</summary>
    public const string RowColumn = "_wiersz";

    /// <summary>Kolumny liczbowe tabeli wyniku (nazwy kolumn DataTable) – w widoku wyrównane do prawej.</summary>
    public IReadOnlySet<string> NumericColumns => _numericColumns;

    /// <summary>Zakres uruchomienia: okresy, poprzedni przebieg do porównania.</summary>
    public string RunScope { get => _runScope; private set { if (SetProperty(ref _runScope, value)) OnPropertyChanged(nameof(RuleDataTip)); } }

    /// <summary>Wynik zapytania reguły: kolumny zapytania, Problem (wskazówka – ResultHint).</summary>
    public DataView? ResultView { get => _resultView; private set => SetProperty(ref _resultView, value); }

    /// <summary>Zaznaczony wiersz wyniku (DataRowView) – linie ACTUALS jego zakresu.</summary>
    public object? SelectedResultRow
    {
        get => _selectedResultRow;
        set
        {
            if (SetProperty(ref _selectedResultRow, value))
                _ = LoadLines();
        }
    }

    /// <summary>Wynik zaznaczonego wiersza – „Pokaż zmiany”.</summary>
    public RunIssueView? SelectedIssue { get => _selectedIssue; private set => SetProperty(ref _selectedIssue, value); }

    public IReadOnlyList<RunActualsLineView> Lines { get => _lines; private set => SetProperty(ref _lines, value); }

    public string LinesTitle { get => _linesTitle; private set => SetProperty(ref _linesTitle, value); }

    /// <summary>Definicja przebiegu uruchomienia i stan.</summary>
    public string Definition { get => _definition; private set => SetProperty(ref _definition, value); }

    /// <summary>Komentarz potwierdzenia etapu (wymagany).</summary>
    public string Comment { get => _comment; set => SetProperty(ref _comment, value); }

    /// <summary>Postęp przebiegu w toku: etap, reguła, czas od uruchomienia (licznik co sekundę – widać, że przebieg się liczy).</summary>
    public string ProgressText => _runStarted is { } at && (int)(DateTime.Now - at).TotalSeconds is var seconds and >= 2
        ? $"{_progress} ({(seconds < 60 ? $"{seconds} s" : $"{seconds / 60} min {seconds % 60:00} s")})" : _progress;

    public bool IsRunning => _runStarted is not null;

    /// <summary>Zmiany po przypięciu ostatniego uruchomienia (nowe pliki ACTUALS, słowniki) – informacja; null – bez zmian.</summary>
    public string? PinChanges
    {
        get => _pinChanges;
        private set
        {
            if (SetProperty(ref _pinChanges, value))
                OnPropertyChanged(nameof(HasPinChanges));
        }
    }

    public bool HasPinChanges => _pinChanges is not null;

    /// <summary>Komunikat ostatniej akcji (np. powód, dla którego przebieg nie ruszył).</summary>
    public string Notice { get => _notice; private set => SetProperty(ref _notice, value); }


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
        Notice = refused ?? "";   // uruchomiony – postęp z RunLauncher.Progress (OnProgress)
        if (refused is null)
            Comment = "";
        CommandManager.InvalidateRequerySuggested();
    }

    private void StartProgress(string text)
    {
        _runStarted ??= DateTime.Now;
        _progress = text;
        _timer.Start();
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void OnProgress(string project, string text)
    {
        if (string.Equals(project, Project, StringComparison.OrdinalIgnoreCase))
            StartProgress(text);
    }

    private void OnFinished(string project)
    {
        if (!string.Equals(project, Project, StringComparison.OrdinalIgnoreCase))
            return;
        _timer.Stop();
        _runStarted = null;
        _progress = "";
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ProgressText));
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
                return new Loaded(pins, definition, assigned, matrixPins.ToDictionary(p => p.PinId, p => _store.Issues(p.PinId)), _store.Rules(),
                    _store.ClosingPins(Project));
            });
            var pins = loaded.Pins;
            _pins = pins;
            _closings = loaded.Closings;
            _issueCache.Clear();
            foreach (var (pin, issues) in loaded.Issues)
                _issueCache[pin] = issues;
            Assigned = loaded.Definition.BuiltIn ? $"Przebieg: definicja wbudowana ({loaded.Definition.Steps.Count} etapów) – brak aktywnych szablonów (ekran Przebiegi)."
                : $"Przebieg: „{loaded.Definition.Code}” ({loaded.Definition.Steps.Count} etapów) – "
                  + (loaded.Assigned is null ? $"domyślny dla typu programu; wybór – Konfiguracja → Parametry programu → {ProjectSettings.RunDefinition}."
                     : string.Equals(loaded.Assigned, loaded.Definition.Code, StringComparison.OrdinalIgnoreCase) ? "wybrany w Konfiguracji programu."
                     : $"wybrany „{loaded.Assigned}” jest nieaktywny albo bez etapów, obowiązuje domyślny dla typu.");
            _library = loaded.Rules;
            Matrix = RunOverview.Matrix(pins.Where(p => loaded.Issues.ContainsKey(p.PinId)).ToList(), loaded.Issues, [.. loaded.Rules.Select(r => r.Name)]);
            var worst = Matrix.Rows.SelectMany(r => r.Cells).Where(c => c.PinId == Matrix.Columns.LastOrDefault()?.PinId && c.Level is not null)
                .OrderByDescending(c => c.Level).FirstOrDefault();
            ShowCell(worst);
            var done = _onlyClosings ? _closings : pins.Where(p => p.Status != RunStatuses.Running).ToList();
            _afterPin = done.FirstOrDefault();
            _beforePin = done.Skip(1).FirstOrDefault();
            OnPropertyChanged(nameof(ComparePins));
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
        IReadOnlyList<RunRule> Rules, IReadOnlyList<RunPinRow> Closings);

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
            .Select(i => RunOverview.View(i, Project)).ToList();
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
        PinChanges = null;
        if (pin is null)
        {
            ShowIssues();
            return;
        }
        try
        {
            var (steps, issues, json, scope) = await Busy.Run($"Wczytywanie uruchomienia {pin.Code} #{pin.PinNo}…",
                () => (_store.Steps(pin.PinId), _store.Issues(pin.PinId), _store.DefinitionOf(pin.PinId), Scope(pin)));
            if (!ReferenceEquals(pin, _selectedPin))
                return;
            var definition = RunWorkflowEngine.Deserialize(json);
            _allIssues = issues;
            RunScope = scope;
            Steps = steps.Select(s =>
            {
                var step = definition?.Steps.FirstOrDefault(d => d.No == s.StepNo);
                var gate = step is null ? "" : step.NeedsApproval ? "zatwierdzenie ręczne" : step.Action == RunActions.Checks ? $"WARNING – {step.WarningGate.ToLowerInvariant()}" : "";
                return new RunStepView(s, gate, step?.Description);
            }).ToList();
            Definition = definition is null
                ? $"{pin.Code} #{pin.PinNo} – {pin.Status} (uruchomienie sprzed przebiegu etapowego – wyniki bez podziału na etapy)"
                : $"{pin.Code} #{pin.PinNo} – {pin.Status}; definicja „{definition.Code}” ({definition.Steps.Count} etapów); {pin.Summary}";
            SelectedStep = Steps.FirstOrDefault(s => s.Row.StepNo == stepNo)
                           ?? Steps.FirstOrDefault(s => s.Row.Status != RunStepStatuses.Done)
                           ?? Steps.LastOrDefault();
            ShowIssues();
            _ = LoadPinChanges(pin, steps);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Uruchomienie {Pin}", pin.PinId);
            Error = $"Nie udało się wczytać uruchomienia: {ex.Message}";
        }
    }

    // Zmiany po przypięciu – tylko ostatnie uruchomienie, nie w toku (w tle, bez paska; błąd odczytu – bez informacji).
    private async Task LoadPinChanges(RunPinRow pin, IReadOnlyList<RunStepRow> steps)
    {
        if (!IsLatest || pin.Status == RunStatuses.Running || steps.FirstOrDefault(s => s.Action == RunActions.Pin) is not { Status: RunStepStatuses.Done, FinishedAt: { } pinnedAt })
            return;
        try
        {
            var changes = await Task.Run(() =>
                RunOverview.ChangesSincePin(pin, pinnedAt, _store.PinFiles(Project, DateTimeOffset.Now), _store.DictionaryChanges(Project, pinnedAt)));
            if (ReferenceEquals(pin, _selectedPin))
                PinChanges = changes;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Zmiany po przypięciu {Pin}", pin.PinId);
        }
    }

    // Wyniki wybranego etapu (uruchomienie bez etapów – wszystkie) po regułach; pierwsza – najgorsza.
    private void ShowIssues()
    {
        RuleResults = RunOverview.RuleResults(_allIssues.Where(i => _steps.Count == 0 || i.StepNo == _selectedStep?.Row.StepNo));
        SelectedRuleResult = RuleResults.FirstOrDefault();
        ShowRule();
    }

    // Wybrana reguła: kontekst z biblioteki reguł, tabela wyniku; zaznaczenie – pierwszy wiersz z problemem.
    private void ShowRule()
    {
        var result = _selectedRuleResult;
        _resultTable = RunOverview.ResultTable(result?.Issues ?? []);
        var rule = result is null ? null : _library.FirstOrDefault(r => string.Equals(r.Name, result.Name, StringComparison.OrdinalIgnoreCase));
        RuleContext = result is null ? ""
            : rule is null ? "Kontrola etapu przebiegu (import, przypięcie stanu) albo reguła usunięta z reguł walidacji."
            : rule.Description ?? "(reguła bez opisu)";
        RuleData = rule is null ? ""
            : "Dane: " + (RunRules.TablesUsed(rule.Sql) is { Count: > 0 } tables
                  ? string.Join("; ", tables.Select(t => $"{t} – {RunRules.Tables.First(x => x.Name == t).Description}"))
                  : "–");
        var (table, numeric) = Table(_resultTable);
        _numericColumns = numeric;
        ResultView = table.DefaultView;
        SelectedResultRow = ResultView.Count > 0 ? ResultView[0] : null;
    }

    // Tabela wyniku do DataGrid: kolumny zapytania (najważniejsze dane na początku), Problem, ukryta kolumna numeru wiersza (RowColumn);
    // wskazówka – pod tabelą (ResultHint). Znaki psujące wiązanie kolumny DataGrid (. / [ ] ( ) ^) – podkreślenie; Numeric – nazwy kolumn
    // liczbowych w DataTable.
    private static (DataTable Table, IReadOnlySet<string> Numeric) Table(RunResultTable result)
    {
        var table = new DataTable();
        var numeric = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Add(string column)
        {
            var name = string.Concat(column.Select(c => ".[]()/^".Contains(c) ? '_' : c));
            name = Enumerable.Range(1, 99).Select(i => i == 1 ? name : $"{name}_{i}").First(n => !table.Columns.Contains(n));
            table.Columns.Add(name, typeof(string));
            return name;
        }
        foreach (var column in result.Columns)
        {
            var name = Add(column);
            if (result.Numeric?.Contains(column) == true)
                numeric.Add(name);
        }
        Add("Problem");
        table.Columns.Add(RowColumn, typeof(int));
        for (var i = 0; i < result.Rows.Count; i++)
        {
            var row = result.Rows[i];
            table.Rows.Add([.. row.Values, row.Problem, i]);
        }
        return (table, numeric);
    }

    private RunResultRow? SelectedRow() =>
        _selectedResultRow is DataRowView view && view[RowColumn] is int index && index >= 0 && index < _resultTable.Rows.Count ? _resultTable.Rows[index] : null;

    // Linie ACTUALS zaznaczonego wiersza: zakres z kolumn wiersza (RunOverview.ActualsScope), stan plików z chwili przypięcia uruchomienia.
    private async Task LoadLines()
    {
        var row = SelectedRow();
        SelectedIssue = row is null ? null : RunOverview.View(row.Issue, Project);
        ResultHint = (_resultTable.CommonHint ?? row?.Hint) is { Length: > 0 } hint ? $"Wskazówka: {hint}" : "";
        Lines = [];
        _linesScope = null;
        if (row is null || _selectedPin is not { } pin)
        {
            LinesTitle = "";
            return;
        }
        try
        {
            var loaded = await Busy.Run<(RunActualsScope Scope, string Description, RunActualsLines Lines)?>("Linie ACTUALS wiersza…", () =>
            {
                var scope = RunOverview.ActualsScope(row.Issue, kind => CostElementsOfKind(pin, kind));
                return scope is not { } s ? null
                    : (s.Scope, s.Description, _store.ActualsLines(s.Scope, AsOf(pin), pin.AmountField ?? ProjectSettings.DefaultActualsAmount, LineCount));
            });
            if (!ReferenceEquals(row, SelectedRow()))
                return;
            if (loaded is not { } l)
            {
                LinesTitle = "Wiersz nie wskazuje elementu WBS, cost elementu ani rodzaju kosztu (kolumny ElementWBS, CostElement, RodzajKosztu) – bez linii ACTUALS.";
                return;
            }
            _linesScope = l.Scope;
            Lines = l.Lines.Rows.Select(r => new RunActualsLineView(r)).ToList();
            LinesTitle = $"Linie ACTUALS – {l.Description}: {l.Lines.Count:#,0} linii, suma {l.Lines.Sum:#,0.00}"
                + (l.Lines.Count > l.Lines.Rows.Count ? $"; pokazano {l.Lines.Rows.Count} największych (wszystkie – do Excela)" : "")
                + (l.Scope.WbsElement is null ? "; linie z elementem WBS programu (bez sieci i zleceń)" : "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Linie ACTUALS {Project}", Project);
            LinesTitle = $"Nie udało się wczytać linii ACTUALS: {ex.Message}";
        }
    }

    // Cost elementy rodzaju kosztu w danych przypiętych uruchomienia (Cost Category programu; bez rodzaju – Other).
    private IReadOnlyCollection<string> CostElementsOfKind(RunPinRow pin, string kind)
    {
        var kinds = _store.Dictionaries(Project).CostKinds;
        return _store.Balance(pin.PinId).Where(r => r.CostElement.Length > 0 && RunCostKinds.Of(r, kinds) == kind)
            .Select(r => r.CostElement).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Stan plików ACTUALS jak w przypięciu uruchomienia: koniec etapu Przypięcie stanu, a bez niego – koniec uruchomienia albo teraz.
    private DateTimeOffset AsOf(RunPinRow pin) =>
        _steps.FirstOrDefault(s => s.Row.Action == RunActions.Pin)?.Row.FinishedAt ?? pin.FinishedAt ?? DateTimeOffset.Now;

    // Zakres uruchomienia: okresy z tygodnia przebiegu i dnia uruchomienia, poprzedni przebieg do porównania.
    private string Scope(RunPinRow pin)
    {
        var (closedBefore, today) = RunService.Periods(_store.RunWeek(pin.RunId).Period, pin.StartedAt.LocalDateTime.Date);
        var previous = RunService.Comparable(_store, pin);
        return $"Uruchomienie {pin.Code} #{pin.PinNo}: bieżący okres {today.Year}/{today.Period:00}, okresy zamknięte przed {closedBefore.Year}/{closedBefore.Period:00}; "
            + (previous is null ? "bez poprzedniego przebiegu do porównania" : $"porównanie z {previous.Code} #{previous.PinNo}");
    }

    private async Task DoExportResult()
    {
        var result = _selectedRuleResult!;
        var path = _dialogs.SaveExcel("Wynik reguły do Excela", $"{Project}_{string.Concat(result.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_'))}.xlsx");
        if (path is null)
            return;
        var table = _resultTable;
        var scope = _linesScope;
        var pin = _selectedPin;
        try
        {
            await Busy.Run("Zapis wyniku do Excela…", () =>
            {
                var lines = scope is null || pin is null ? null
                    : _store.ActualsLines(scope, AsOf(pin), pin.AmountField ?? ProjectSettings.DefaultActualsAmount, ExportLines);
                ExcelTableWriter.WriteSheets(path,
                [
                    ("Wynik", ["Nr", "Poziom", "Problem", "Wskazówka", .. table.Columns],
                        table.Rows.Select((r, i) => (IReadOnlyList<object?>)[i + 1, r.LevelText, r.Problem, r.Hint, .. r.Values])),
                    .. lines is null ? Array.Empty<(string, IReadOnlyList<string>, IEnumerable<IReadOnlyList<object?>>)>() :
                    [
                        ("Linie ACTUALS", ["Element WBS", "Cost element", "Rok", "Okres", "Created on", "Opis cost elementu", "Opis", "Partner", "Zamówienie", "Faktura",
                            "Zlecenie", "Ilość", "Kwota", "Plik", "Wiersz"],
                            lines.Rows.Select(l => (IReadOnlyList<object?>)[l.WbsElement, l.CostElement, l.FiscalYear, l.Period, l.CreatedOn, l.CostElementName, l.CoObjectName,
                                l.PartnerObject, l.PurchaseOrderNumber, l.InvoiceNumber, l.OriginalOrderNumber, l.Quantity, l.Amount, l.FileName, l.RowNumber])),
                    ],
                    ("Opis", ["Opis"], [[$"{Project} – {result.Name}"], [RunScope], [RuleContext], [RuleData], [LinesTitle]]),
                ]);
                return true;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Wynik reguły do Excela {Project}", Project);
            Error = $"Nie udało się zapisać wyniku do Excela: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _launcher.Progress -= OnProgress;
        _launcher.Finished -= OnFinished;
    }
}
