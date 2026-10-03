using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Ekran Projekty: lista projektów (docs/funkcjonalnosc.md, rozdz. 2) i przejście do kreatora / projektu.</summary>
public sealed class ProjectListViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly ProjectService _service;
    private ProjectListItem? _selected;
    private string _status = "";

    public ProjectListViewModel(ProjectService service, Action newProject, Action<string> open)
    {
        _service = service;
        NewProject = new RelayCommand(_ => newProject());
        Open = new RelayCommand(p => open(((ProjectListItem)p!).Code), p => p is ProjectListItem);
        Refresh = new RelayCommand(_ => Reload());
        Reload();
    }

    public ObservableCollection<ProjectListItem> Items { get; } = [];

    public ProjectListItem? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool IsEmpty => Items.Count == 0;

    public ICommand NewProject { get; }
    public ICommand Open { get; }
    public ICommand Refresh { get; }

    public void Reload()
    {
        Items.Clear();
        try
        {
            var mapping = _service.Mapping();
            foreach (var project in _service.Projects())
            {
                var tree = _service.Objectives(project.Code);
                var ready = ProjectReadiness.IsReady(_service.Readiness(project, tree, mapping));
                Items.Add(new ProjectListItem(project.Code, project.Name, ProjectTypes.Label(project.Type),
                    $"{tree.ElementCount} el. · {tree.VirtualCount} węzłów",
                    ready ? new Pill("ok", "gotowy") : new Pill("crit", "niegotowy"),
                    "brak przebiegów", _service.Folders.PathOf(project.Code)));
            }
            Status = "";
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Logger.Error(ex, "Lista projektów");
            Status = $"Nie udało się wczytać projektów: {ex.Message}";
        }
        OnPropertyChanged(nameof(IsEmpty));
    }
}
