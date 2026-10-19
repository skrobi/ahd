using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Ekran Projekty: lista projektów (docs/funkcjonalnosc.md, rozdz. 2) i przejście do kreatora / projektu.</summary>
public sealed class ProjectListViewModel : ObservableObject, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly ProjectService _service;
    private ProjectListItem? _selected;
    private string _status = "";
    private bool _loaded;

    public ProjectListViewModel(ProjectService service, BusyState busy, Action newProject, Action<string> open)
    {
        _service = service;
        Busy = busy;
        NewProject = new RelayCommand(_ => newProject(), _ => !Busy.IsBusy);
        Open = new RelayCommand(p => open(((ProjectListItem)p!).Code), p => p is ProjectListItem && !Busy.IsBusy);
        Refresh = new AsyncRelayCommand(Reload, () => !Busy.IsBusy);
        if (_service.Runs is { } runs)
            runs.Finished += OnRunFinished;   // zakończony przebieg – stan na liście od razu
        _ = Reload();
    }

    public BusyState Busy { get; }

    public ObservableCollection<ProjectListItem> Items { get; } = [];

    public ProjectListItem? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool IsEmpty => _loaded && Items.Count == 0;

    public ICommand NewProject { get; }
    public ICommand Open { get; }
    public ICommand Refresh { get; }

    private void OnRunFinished(string project) => _ = Reload();

    /// <summary>Odpięcie od zdarzeń przebiegów przy zmianie strony modułu.</summary>
    public void Dispose()
    {
        if (_service.Runs is { } runs)
            runs.Finished -= OnRunFinished;
    }

    public async Task Reload()
    {
        try
        {
            // Sam odczyt projektów – nakładka, gotowość i mapowanie dopiero na ekranie projektu.
            var items = await Busy.Run("Wczytywanie programów…", () =>
            {
                var runs = _service.Runs?.Latest() ?? new Dictionary<string, RunPinRow>();
                return _service.Projects().Select(project => runs.GetValueOrDefault(project.Code) is { } run
                    ? new ProjectListItem(project.Code, project.Name, ProjectTypes.Label(project.Type),
                        $"{run.Label} ({(run.FinishedAt ?? run.StartedAt).ToLocalTime():yyyy-MM-dd HH:mm})", _service.Folders.PathOf(project.Code),
                        run.Status == RunStatuses.Running ? "w toku" : $"błędy {run.Errors} · ostrzeżenia {run.Warnings}",
                        run.Status == RunStatuses.Running ? "muted" : run.Errors > 0 || run.Status is RunStatuses.Failed or RunStatuses.Abandoned ? "crit" : run.Warnings > 0 ? "warn" : "ok",
                        run.Summary ?? "")
                    : new ProjectListItem(project.Code, project.Name, ProjectTypes.Label(project.Type), "brak przebiegów", _service.Folders.PathOf(project.Code))).ToList();
            });
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            Status = "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Lista programów");
            Status = $"Nie udało się wczytać programów: {ex.Message}";
        }
        _loaded = true;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
