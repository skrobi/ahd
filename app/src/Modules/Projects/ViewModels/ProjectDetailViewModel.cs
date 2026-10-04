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
    private MappingInputs _mapping = MappingInputs.None;
    private IReadOnlyDictionary<string, string> _owners = new Dictionary<string, string>();
    private PoTree _saved = new();

    public ProjectDetailViewModel(ProjectService service, IFileDialogs dialogs, BusyState busy, ProjectInfo project, Action back)
    {
        _service = service;
        _dialogs = dialogs;
        Busy = busy;
        Project = project;
        // Kontrola nakładki w pamięci (elementy CES innych projektów wczytane przy otwarciu); zapis sprawdza je w bazie.
        Objectives = new PoEditorViewModel(dialogs, busy, service.ReadObjectives, tree => ObjectivesValidator.Validate(tree, _owners), ProjectService.Refresh);
        Objectives.ResolveMapping = tree => ProjectService.Resolve(tree, _mapping);
        Objectives.MappingInfo = "Wczytywanie danych mapowania CES ↔ P1S…";
        Back = new RelayCommand(_ => back(), _ => !Busy.IsBusy);
        SaveObjectives = new AsyncRelayCommand(DoSaveObjectives, () => Objectives.IsDirty && !Busy.IsBusy);
        DiscardObjectives = new RelayCommand(_ => { Objectives.Load(_saved.Copy()); Status = "Zmiany nakładki odrzucone."; }, _ => Objectives.IsDirty && !Busy.IsBusy);
        ExportDictionaries = new AsyncRelayCommand(DoExportDictionaries, () => !Busy.IsBusy);
        CreateFolders = new AsyncRelayCommand(DoCreateFolders, () => !Busy.IsBusy);
        RefreshMapping = new AsyncRelayCommand(() => Reload(refreshMapping: true), () => !Objectives.IsDirty && !Busy.IsBusy);
        foreach (var item in ProjectDictionaries.ForType(project.Type))
        {
            var panel = new DictionaryPanelViewModel(item, project.Type);
            panel.Load = new AsyncRelayCommand(() => LoadDictionary(panel), () => item.Stored && !Objectives.IsDirty && !Busy.IsBusy);
            panel.Apply = new AsyncRelayCommand(() => ApplyDictionary(panel), () => panel.Preview is { HasErrors: false, HasChanges: true } && !Busy.IsBusy);
            panel.Remove = new RelayCommand(_ => { panel.SetPreview(null, ""); Status = "Wczytanie anulowane – słownik bez zmian."; }, _ => panel.HasPreview && !Busy.IsBusy);
            Dictionaries.Add(panel);
        }
        _ = Reload(refreshMapping: false);
    }

    public BusyState Busy { get; }

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
    public ICommand RefreshMapping { get; }

    /// <summary>Stan projektu z bazy w jednym odczycie w tle: nakładka, mapowanie, gotowość, słowniki.</summary>
    private sealed record Snapshot(PoTree Tree, MappingInputs Mapping, IReadOnlyDictionary<string, string> Owners, List<Issue> Readiness,
        IReadOnlyDictionary<string, (int Rows, string LastChange)> Dictionaries);

    /// <summary>Wczytuje stan projektu w tle; keepEdits – nakładka w edycji zostaje (odświeżane są gotowość i słowniki).</summary>
    private async Task Reload(bool refreshMapping, bool keepEdits = false)
    {
        var codes = Dictionaries.Where(p => p.Item.Stored).Select(p => p.Item.Code).ToList();
        await Try(async () =>
        {
            var snapshot = await Busy.Run(refreshMapping ? "Odświeżanie mapowania CES ↔ P1S i gotowości projektu…" : "Wczytywanie projektu, mapowania i gotowości…", () =>
            {
                var tree = _service.Objectives(Code);
                var mapping = _service.Mapping(refreshMapping);
                return new Snapshot(tree, mapping, _service.WbsOwners(Code), _service.Readiness(Project, tree, mapping),
                    codes.ToDictionary(c => c, c => (_service.Rows(c, Code).Count, _service.LastChange(c, Code))));
            });
            _saved = snapshot.Tree;
            _mapping = snapshot.Mapping;
            _owners = snapshot.Owners;
            Objectives.MappingInfo = _mapping.Describe;
            if (keepEdits && Objectives.IsDirty)
                Objectives.Refresh();
            else
                Objectives.Load(_saved.Copy());
            foreach (var panel in Dictionaries.Where(p => p.Item.Stored))
                (panel.Rows, panel.LastChange) = snapshot.Dictionaries[panel.Item.Code];
            Readiness.Clear();
            foreach (var check in snapshot.Readiness)
                Readiness.Add(check);
            ReadinessPill = ProjectReadiness.IsReady(snapshot.Readiness) ? new Pill("ok", "gotowy") : new Pill("crit", "niegotowy – przebieg zablokowany");
            if (refreshMapping)
                Status = "Odświeżono mapowanie CES ↔ P1S.";
        });
    }

    private async Task DoSaveObjectives()
    {
        var tree = Objectives.Tree.Copy();
        await Try(async () =>
        {
            var (issues, result) = await Busy.Run("Zapisywanie nakładki Performance Objectives…", () => _service.SaveObjectives(Code, tree));
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
            await Reload(refreshMapping: false);
            Status = $"Zapisano nakładkę: +{result.Added} ~{result.Updated} −{result.Removed}{(issues.Count > 0 ? $" (ostrzeżenia: {issues.Count})" : "")}.";
        });
    }

    /// <summary>Kontekst walidacji (w tle): harmonogram względem WP z wczytanego „WP i CAM”, jeśli jest podgląd – inaczej z bazy.</summary>
    private Func<ProjectDictionaryContext> Context(string dictionary)
    {
        var wpPreview = Dictionaries.FirstOrDefault(p => p.Item.Code == ProjectDictionaries.WpCam)?.Preview;
        var fromPreview = dictionary != ProjectDictionaries.WpCam && wpPreview is { HasErrors: false } ? wpPreview.Working : null;
        var (tree, mapping) = (_saved, _mapping);
        return () => _service.Context(Code, tree, fromPreview ?? _service.Rows(ProjectDictionaries.WpCam, Code), mapping);
    }

    private async Task LoadDictionary(DictionaryPanelViewModel panel)
    {
        var path = _dialogs.OpenExcel($"Wczytaj słownik „{panel.Name}” projektu {Code}");
        if (path is null)
            return;
        var context = Context(panel.Item.Code);
        await Try(async () =>
        {
            var (sheet, preview) = await Busy.Run($"Wczytywanie i sprawdzanie słownika „{panel.Name}”…", () =>
            {
                var found = ProjectService.FindSheet(path, panel.Item);
                return (found, _service.PreviewDictionary(panel.Item.Code, context(), path, Code, found));
            });
            panel.SetPreview(preview, $"{Path.GetFileName(path)}{(sheet is null ? "" : $" · arkusz „{sheet}”")}");
            Status = preview.HasErrors
                ? $"{panel.Name}: plik ma błędy (ERROR) – nie można go wczytać."
                : preview.HasChanges ? $"{panel.Name}: sprawdź różnice i zatwierdź." : $"{panel.Name}: plik nie zawiera zmian.";
        });
    }

    private async Task ApplyDictionary(DictionaryPanelViewModel panel)
    {
        var (context, preview) = (Context(panel.Item.Code), panel.Preview!);
        await Try(async () =>
        {
            var outcome = await Busy.Run($"Zapisywanie słownika „{panel.Name}”…", () => _service.ApplyDictionary(panel.Item.Code, context(), preview, Code));
            if (outcome.Status == SaveStatus.Saved)
                panel.SetPreview(null, "");
            await Reload(refreshMapping: false, keepEdits: true);
            Status = $"{panel.Name}: {outcome.Message}";
        });
    }

    private async Task DoExportDictionaries()
    {
        var path = _dialogs.SaveExcel("Pobierz słowniki projektu", $"{Code}_slowniki_projektu.xlsx");
        if (path is null)
            return;
        var (tree, mapping) = (_saved, _mapping);
        await Try(async () =>
        {
            await Busy.Run("Zapisywanie słowników projektu do Excela…", () => _service.ExportDictionaries(path, Code, Project.Type, tree, mapping));
            Status = $"Zapisano {path} – popraw w Excelu i wczytaj ponownie (przed zapisem zobaczysz różnice).";
        });
    }

    private async Task DoCreateFolders()
    {
        await Try(async () =>
        {
            var created = await Busy.Run("Tworzenie folderów projektu…", () => _service.Folders.Create(Code));
            await Reload(refreshMapping: false, keepEdits: true);
            Status = created.Count == 0 ? "Wszystkie foldery projektu istnieją." : $"Utworzono foldery: {string.Join(", ", created)}.";
        });
    }

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Projekt {Code}", Code);
            Status = $"Nie udało się: {ex.Message}";
        }
    }
}
