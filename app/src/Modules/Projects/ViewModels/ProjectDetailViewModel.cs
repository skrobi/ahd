using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Ekran Projekt (docs/funkcjonalnosc.md, rozdz. 2; F02): gotowość, nakładka Performance Objectives (edycja i zapis
/// z historią, odświeżenie z SAP), słowniki projektu (pobierz / wczytaj z Excela z podglądem różnic), historia
/// przebiegów, struktura folderów.
/// </summary>
public sealed class ProjectDetailViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly ProjectService _service;
    private readonly IFileDialogs _dialogs;
    private string _status = "";
    private Pill _readiness = new("muted", "");

    public ProjectDetailViewModel(ProjectService service, IFileDialogs dialogs, ProjectInfo project, Action back)
    {
        _service = service;
        _dialogs = dialogs;
        Project = project;
        Objectives = new PoEditorViewModel(dialogs, service.ReadObjectives, tree => service.ValidateObjectives(project.Code, tree), ProjectService.Refresh);
        Back = new RelayCommand(_ => back());
        SaveObjectives = new RelayCommand(_ => DoSaveObjectives(), _ => Objectives.IsDirty);
        DiscardObjectives = new RelayCommand(_ => { Objectives.Load(_service.Objectives(Code)); Status = "Zmiany nakładki odrzucone."; }, _ => Objectives.IsDirty);
        ExportDictionaries = new RelayCommand(_ => DoExportDictionaries());
        CreateFolders = new RelayCommand(_ => DoCreateFolders());
        foreach (var item in ProjectDictionaries.ForType(project.Type))
        {
            var panel = new DictionaryPanelViewModel(item, project.Type);
            panel.Load = new RelayCommand(_ => LoadDictionary(panel), _ => item.Stored && !Objectives.IsDirty);
            panel.Apply = new RelayCommand(_ => ApplyDictionary(panel), _ => panel.Preview is { HasErrors: false, HasChanges: true });
            panel.Remove = new RelayCommand(_ => { panel.SetPreview(null, ""); Status = "Wczytanie anulowane – słownik bez zmian."; }, _ => panel.HasPreview);
            Dictionaries.Add(panel);
        }
        Objectives.Load(_service.Objectives(Code));
        Reload();
    }

    public ProjectInfo Project { get; }

    public string Code => Project.Code;

    public string Title => $"{Project.Code} · {Project.Name}";

    public string Subtitle => $"{ProjectTypes.Label(Project.Type)} · utworzony {Project.RecordedAt:yyyy-MM-dd HH:mm} przez {Project.RecordedBy}";

    public PoEditorViewModel Objectives { get; }

    public ObservableCollection<Issue> Readiness { get; } = [];

    public ObservableCollection<DictionaryPanelViewModel> Dictionaries { get; } = [];

    public Pill ReadinessPill { get => _readiness; private set => SetProperty(ref _readiness, value); }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string FolderPath => _service.Folders.PathOf(Code);

    public string FolderPreview => _service.Folders.Preview(Code);

    public string ItRequest => _service.Folders.ItRequest(Code);

    public string Runs => "Brak przebiegów. Przebiegi (Nowy przebieg, historia) powstaną w kolejnym etapie – docs/pipeline-fazy.md.";

    public ICommand Back { get; }
    public ICommand SaveObjectives { get; }
    public ICommand DiscardObjectives { get; }
    public ICommand ExportDictionaries { get; }
    public ICommand CreateFolders { get; }

    private void Reload()
    {
        Try(() =>
        {
            foreach (var panel in Dictionaries.Where(p => p.Item.Stored))
            {
                panel.Rows = _service.Rows(panel.Item.Code, Code).Count;
                panel.LastChange = _service.LastChange(panel.Item.Code, Code);
            }
            var checks = _service.Readiness(Project, _service.Objectives(Code));
            Readiness.Clear();
            foreach (var check in checks)
                Readiness.Add(check);
            ReadinessPill = ProjectReadiness.IsReady(checks) ? new Pill("ok", "gotowy") : new Pill("crit", "niegotowy – przebieg zablokowany");
        });
    }

    private void DoSaveObjectives()
    {
        Try(() =>
        {
            var (issues, result) = _service.SaveObjectives(Code, Objectives.Tree);
            if (result is null)
            {
                Status = "Nakładka ma błędy (ERROR) – nic nie zapisano.";
                return;
            }
            if (!result.Success)
            {
                Status = result.Conflict!;
                return;
            }
            Objectives.Load(_service.Objectives(Code));
            Status = $"Zapisano nakładkę: +{result.Added} ~{result.Updated} −{result.Removed}{(issues.Count > 0 ? $" (ostrzeżenia: {issues.Count})" : "")}.";
            Reload();
        });
    }

    private ProjectDictionaryContext Context(string dictionary)
    {
        // Harmonogram sprawdzany względem WP z wczytanego (niezapisanego) „WP i CAM”, jeśli jest podgląd – inaczej z bazy.
        var wpPreview = Dictionaries.FirstOrDefault(p => p.Item.Code == ProjectDictionaries.WpCam)?.Preview;
        IEnumerable<DictRow> wpRows = dictionary != ProjectDictionaries.WpCam && wpPreview is { HasErrors: false }
            ? wpPreview.Working
            : _service.Rows(ProjectDictionaries.WpCam, Code);
        return _service.Context(Code, _service.Objectives(Code), wpRows);
    }

    private void LoadDictionary(DictionaryPanelViewModel panel)
    {
        var path = _dialogs.OpenExcel($"Wczytaj słownik „{panel.Name}” projektu {Code}");
        if (path is null)
            return;
        Try(() =>
        {
            var sheet = ProjectService.FindSheet(path, panel.Item);
            var preview = _service.PreviewDictionary(panel.Item.Code, Context(panel.Item.Code), path, Code, sheet);
            panel.SetPreview(preview, $"{Path.GetFileName(path)}{(sheet is null ? "" : $" · arkusz „{sheet}”")}");
            Status = preview.HasErrors
                ? $"{panel.Name}: plik ma błędy (ERROR) – nie można go wczytać."
                : preview.HasChanges ? $"{panel.Name}: sprawdź różnice i zatwierdź." : $"{panel.Name}: plik nie zawiera zmian.";
        });
    }

    private void ApplyDictionary(DictionaryPanelViewModel panel)
    {
        Try(() =>
        {
            var outcome = _service.ApplyDictionary(panel.Item.Code, Context(panel.Item.Code), panel.Preview!, Code);
            Status = $"{panel.Name}: {outcome.Message}";
            if (outcome.Status == SaveStatus.Saved)
                panel.SetPreview(null, "");
            Reload();
        });
    }

    private void DoExportDictionaries()
    {
        var path = _dialogs.SaveExcel("Pobierz słowniki projektu", $"{Code}_slowniki_projektu.xlsx");
        if (path is null)
            return;
        Try(() =>
        {
            _service.ExportDictionaries(path, Code, Project.Type, _service.Objectives(Code));
            Status = $"Zapisano {path} – popraw w Excelu i wczytaj ponownie (przed zapisem zobaczysz różnice).";
        });
    }

    private void DoCreateFolders()
    {
        Try(() =>
        {
            var created = _service.Folders.Create(Code);
            Status = created.Count == 0 ? "Wszystkie foldery projektu istnieją." : $"Utworzono foldery: {string.Join(", ", created)}.";
            Reload();
        });
    }

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException
                                       or Microsoft.Data.SqlClient.SqlException)
        {
            Logger.Error(ex, "Projekt {Code}", Code);
            Status = $"Nie udało się: {ex.Message}";
        }
    }
}
