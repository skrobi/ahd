using System.Collections.ObjectModel;
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
/// Ekran Przebiegi (docs/funkcjonalnosc.md, F06): wybór programu, macierz wyników kontroli ostatnich uruchomień (klik w komórkę –
/// wyniki tej kontroli w uruchomieniu) i definicja przebiegu (reguły kontroli z ustawieniami dla typu programu, ustawienia
/// ostatniego uruchomienia). Tylko odczyt – przebieg uruchamia ekran programu.
/// </summary>
public sealed class RunsViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    /// <summary>Liczba uruchomień w macierzy.</summary>
    public const int PinCount = 8;

    private readonly IRunStore _store;
    private IReadOnlyList<RunProjectRow> _projects = [];
    private RunProjectRow? _selectedProject;
    private RunMatrix _matrix = new([], []);
    private IReadOnlyList<RunDefinitionRow> _definition = [];
    private IReadOnlyList<RunReconciliationView> _reconciliations = [];
    private IReadOnlyList<RunIssueRow> _cellIssues = [];
    private string _cellTitle = "";
    private string _definitionNote = "";
    private string? _error;
    private Dictionary<long, IReadOnlyList<RunIssueRow>> _issues = [];
    private Dictionary<long, string> _pinLabels = [];

    public RunsViewModel(IRunStore store, IAcwpChanges? acwpChanges = null)
    {
        _store = store;
        Refresh = new AsyncRelayCommand(LoadProjects, () => !Busy.IsBusy);
        OpenAcwpChanges = new RelayCommand(_ => acwpChanges?.Show(SelectedProject!.Code, null, null, "cały program"),
            _ => acwpChanges is not null && SelectedProject is not null && Matrix.Columns.Count > 0);
        SelectCell = new RelayCommand(p => ShowCell(p as RunMatrixCell));
        _ = LoadProjects();
    }

    public BusyState Busy { get; } = new();

    public ICommand Refresh { get; }

    public ICommand SelectCell { get; }

    /// <summary>Okno „Zmiany ACWP” programu (porównanie uruchomień albo okresów).</summary>
    public ICommand OpenAcwpChanges { get; }

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

    public IReadOnlyList<RunDefinitionRow> Definition { get => _definition; private set => SetProperty(ref _definition, value); }

    /// <summary>Reguły uzgodnień dla typu programu.</summary>
    public IReadOnlyList<RunReconciliationView> Reconciliations { get => _reconciliations; private set => SetProperty(ref _reconciliations, value); }

    /// <summary>Miary dostępne w formułach reguł uzgodnień.</summary>
    public IReadOnlyList<RunMeasure> Measures => RunMeasures.All;

    /// <summary>Opis definicji: typ programu i uruchomienie, z którego pochodzi kolumna „Ostatnie uruchomienie”.</summary>
    public string DefinitionNote { get => _definitionNote; private set => SetProperty(ref _definitionNote, value); }

    public IReadOnlyList<RunIssueRow> CellIssues { get => _cellIssues; private set => SetProperty(ref _cellIssues, value); }

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
            Projects = await Busy.Run("Wczytywanie programów…", _store.Projects);
            // Odśwież – ten sam program wczytany ponownie (rekord nowej listy jest równy poprzedniemu, setter by go pominął).
            _selectedProject = Projects.FirstOrDefault(p => p.Code == selected) ?? Projects.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedProject));
            await LoadProject(_selectedProject);
        });
    }

    private async Task LoadProject(RunProjectRow? project)
    {
        CellIssues = [];
        CellTitle = "";
        if (project is null)
        {
            Matrix = new([], []);
            Definition = [];
            Reconciliations = [];
            return;
        }
        await Try(async () =>
        {
            var loaded = await Busy.Run<Loaded>($"Wczytywanie przebiegów programu {project.Code}…", () =>
            {
                var pins = _store.Pins(project.Code, PinCount);
                var latest = pins.FirstOrDefault(p => p.Status != RunStatuses.Running);
                return new Loaded(pins, pins.ToDictionary(p => p.PinId, p => _store.Issues(p.PinId)), _store.CheckSettings(),
                    latest, latest is null ? null : _store.CheckSettingsOf(latest.PinId), _store.Reconciliations());
            });
            if (!ReferenceEquals(project, _selectedProject))
                return;
            _issues = loaded.Issues;
            _pinLabels = loaded.Pins.ToDictionary(p => p.PinId, p => $"{p.Code} #{p.PinNo}");
            Matrix = RunOverview.Matrix(loaded.Pins, loaded.Issues);
            var stored = RunOverview.Deserialize(loaded.LatestSettings);
            Definition = RunOverview.Definition(loaded.Settings, project.ProjectType, stored);
            Reconciliations = RunOverview.Reconciliations(loaded.Reconciliations, project.ProjectType);
            DefinitionNote = $"Typ programu: {project.ProjectType}. Kolumna „Ostatnie uruchomienie” – "
                + (loaded.Latest is not { } latest ? "brak uruchomień."
                    : stored is null ? $"{latest.Code} #{latest.PinNo} bez zapisanych ustawień (sprzed migracji 048)."
                    : $"ustawienia użyte w {latest.Code} #{latest.PinNo}.");
            var worst = Matrix.Rows.SelectMany(r => r.Cells).Where(c => c.PinId == Matrix.Columns.LastOrDefault()?.PinId && c.Level is not null)
                .OrderByDescending(c => c.Level).FirstOrDefault();
            ShowCell(worst);
        });
    }

    /// <summary>Dane programu z bazy: uruchomienia, ich wyniki, słownik „Kontrole przebiegu”, ustawienia ostatniego zakończonego uruchomienia, reguły uzgodnień.</summary>
    private sealed record Loaded(IReadOnlyList<RunPinRow> Pins, Dictionary<long, IReadOnlyList<RunIssueRow>> Issues, IReadOnlyList<RunCheckRow> Settings,
        RunPinRow? Latest, string? LatestSettings, IReadOnlyList<RunReconciliationRow> Reconciliations);

    private void ShowCell(RunMatrixCell? cell)
    {
        if (cell is null)
        {
            CellIssues = [];
            CellTitle = "";
            return;
        }
        CellIssues = (_issues.GetValueOrDefault(cell.PinId) ?? []).Where(i => i.CheckName == cell.CheckName)
            .OrderBy(i => i.Level == CheckLevel.Error ? 0 : i.Level == CheckLevel.Warning ? 1 : 2).ThenBy(i => i.Id).ToList();
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
