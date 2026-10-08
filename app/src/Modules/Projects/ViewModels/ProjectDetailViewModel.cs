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
/// Ekran Projekt (docs/funkcjonalnosc.md, rozdz. 2; F02) w zakładkach: Wskaźniki (sumy projektu, miejsce na wskaźniki
/// EV, gotowość do przebiegu), Struktura (nakładka Performance Objectives z rozwinięciem P1S, WP, CAM, budżetem
/// i datami – edycja w komórkach zapisywana od razu, dokładanie elementów z kolejnego eksportu SAP), Słowniki projektu
/// (pobierz / wczytaj z Excela z podglądem różnic), Przebiegi, Foldery.
/// </summary>
public sealed class ProjectDetailViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly ProjectService _service;
    private readonly IFileDialogs _dialogs;
    private string _status = "";
    private Pill _readiness = new("muted", "");
    private MappingInputs _mapping = MappingInputs.None;
    private PoTree _saved = new();
    private IReadOnlyDictionary<string, string> _owners = new Dictionary<string, string>();
    private bool _isEditingObjectives;
    private bool _isReady;

    public ProjectDetailViewModel(ProjectService service, IFileDialogs dialogs, BusyState busy, ProjectInfo project, Action back)
    {
        _service = service;
        _dialogs = dialogs;
        Busy = busy;
        Project = project;
        Structure = new StructureViewModel { Commit = CommitRow, CanEditNow = () => !Busy.IsBusy };
        // Edycja nakładki tym samym edytorem co w kreatorze (przeciąganie, przesuwanie, usuwanie, węzły wirtualne);
        // „Wczytaj Excel” podmienia strukturę (elementy o tym samym WBS element zachowują historię).
        Objectives = new PoEditorViewModel(dialogs, busy, service.ReadObjectives, tree => ObjectivesValidator.Validate(tree, _owners), ProjectService.Refresh);
        Objectives.ResolveMapping = tree => ProjectService.Resolve(tree, _mapping);
        EditObjectives = new RelayCommand(_ => StartEditObjectives(), _ => !IsEditingObjectives && !Busy.IsBusy);
        SaveObjectives = new AsyncRelayCommand(DoSaveObjectives, () => IsEditingObjectives && Objectives.IsDirty && !Busy.IsBusy);
        CancelObjectives = new RelayCommand(_ => { IsEditingObjectives = false; Status = Objectives.IsDirty ? "Zmiany Performance Objectives odrzucone." : ""; },
            _ => IsEditingObjectives && !Busy.IsBusy);
        Back = new RelayCommand(_ => back(), _ => !Busy.IsBusy);
        AddFromExcel = new AsyncRelayCommand(DoAddFromExcel, () => !Busy.IsBusy);
        ExportCostReport = new AsyncRelayCommand(DoExportCostReport, () => !Busy.IsBusy);
        ExportDictionaries = new AsyncRelayCommand(DoExportDictionaries, () => !Busy.IsBusy);
        CreateFolders = new AsyncRelayCommand(DoCreateFolders, () => !Busy.IsBusy);
        RefreshMapping = new AsyncRelayCommand(() => Reload(refreshMapping: true), () => !Busy.IsBusy);
        foreach (var item in ProjectDictionaries.ForType(project.Type))
        {
            var panel = new DictionaryPanelViewModel(item, project.Type);
            panel.Load = new AsyncRelayCommand(() => LoadDictionary(panel), () => item.Stored && !Busy.IsBusy);
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

    public StructureViewModel Structure { get; }

    /// <summary>Edytor nakładki Performance Objectives (ten sam co w kreatorze), widoczny w trybie edycji.</summary>
    public PoEditorViewModel Objectives { get; }

    public bool IsEditingObjectives
    {
        get => _isEditingObjectives;
        private set
        {
            if (SetProperty(ref _isEditingObjectives, value))
                OnPropertyChanged(nameof(IsViewingStructure));
        }
    }

    public bool IsViewingStructure => !_isEditingObjectives;

    public ObservableCollection<KpiTile> Kpis { get; } = [];

    /// <summary>Wskaźniki EV – liczone w przebiegach (docs/ev-obliczenia.md); do tego czasu kafelki bez wartości.</summary>
    public IReadOnlyList<KpiTile> EvKpis { get; } =
        new[] { "BCWS", "BCWP", "ACWP", "CPI", "SPI", "EAC" }.Select(k => new KpiTile(k, "—", "po obliczeniu EV w przebiegu", Pending: true)).ToList();

    public ObservableCollection<Issue> Readiness { get; } = [];

    /// <summary>Czy projekt jest gotowy do przebiegu (kontrole F02 bez ERROR).</summary>
    public bool IsReady { get => _isReady; private set => SetProperty(ref _isReady, value); }

    public string MappingInfo => _mapping.Describe;

    public ObservableCollection<DictionaryPanelViewModel> Dictionaries { get; } = [];

    public Pill ReadinessPill { get => _readiness; private set => SetProperty(ref _readiness, value); }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string FolderPath => _service.Folders.PathOf(Code);

    public string FolderPreview => _service.Folders.Preview(Code);

    public string ItRequest => _service.Folders.ItRequest(Code);

    public string Runs => "Brak przebiegów. Przebiegi (Nowy przebieg, historia) powstaną w kolejnym etapie – docs/pipeline-fazy.md.";

    public ICommand Back { get; }
    public ICommand AddFromExcel { get; }
    public ICommand EditObjectives { get; }
    public ICommand SaveObjectives { get; }
    public ICommand CancelObjectives { get; }
    public ICommand ExportCostReport { get; }
    public ICommand ExportDictionaries { get; }
    public ICommand CreateFolders { get; }
    public ICommand RefreshMapping { get; }

    /// <summary>Stan projektu z bazy w jednym odczycie w tle: nakładka, mapowanie, gotowość, słowniki, struktura.</summary>
    private sealed record Snapshot(PoTree Tree, MappingInputs Mapping, IReadOnlyDictionary<string, string> Owners, List<Issue> Readiness,
        IReadOnlyDictionary<string, (int Rows, string LastChange)> Dictionaries, ProjectStructure Structure);

    /// <summary>Wczytuje stan projektu w tle (struktura zachowuje rozwinięcie i zaznaczenie).</summary>
    private async Task Reload(bool refreshMapping)
    {
        var codes = Dictionaries.Where(p => p.Item.Stored).Select(p => p.Item.Code).ToList();
        await Try(async () =>
        {
            var snapshot = await Busy.Run(refreshMapping ? "Odświeżanie mapowania CES ↔ P1S i struktury projektu…" : "Wczytywanie projektu, mapowania i struktury…", () =>
            {
                var tree = _service.Objectives(Code);
                var mapping = _service.Mapping(refreshMapping);
                var structure = ProjectService.Structure(tree, mapping, _service.Rows(ProjectDictionaries.WpCam, Code), _service.Rows(ProjectDictionaries.ScheduleBudget, Code));
                return new Snapshot(tree, mapping, _service.WbsOwners(Code), _service.Readiness(Project, tree, mapping),
                    codes.ToDictionary(c => c, c => (_service.Rows(c, Code).Count, _service.LastChange(c, Code))), structure);
            });
            _saved = snapshot.Tree;
            _mapping = snapshot.Mapping;
            _owners = snapshot.Owners;
            Objectives.MappingInfo = _mapping.Describe;
            if (IsEditingObjectives)
                Objectives.Refresh();
            OnPropertyChanged(nameof(MappingInfo));
            Structure.Load(snapshot.Structure);
            ShowKpis(snapshot.Structure.Summary);
            foreach (var panel in Dictionaries.Where(p => p.Item.Stored))
                (panel.Rows, panel.LastChange) = snapshot.Dictionaries[panel.Item.Code];
            Readiness.Clear();
            foreach (var check in snapshot.Readiness)
                Readiness.Add(check);
            IsReady = ProjectReadiness.IsReady(snapshot.Readiness);
            ReadinessPill = IsReady ? new Pill("ok", "gotowy do przebiegu") : new Pill("crit", "niegotowy – przebieg zablokowany");
            if (refreshMapping)
                Status = "Odświeżono mapowanie CES ↔ P1S.";
        });
    }

    private void ShowKpis(StructureSummary summary)
    {
        static string Amount(decimal value) => PzlEv.Shared.Utils.Files.PolishNumber.ToDisplay(value);
        Kpis.Clear();
        Kpis.Add(new KpiTile("BAC HOURS", Amount(summary.BacHours), "budżet godzin – Harmonogram i budżet"));
        Kpis.Add(new KpiTile("BAC MATERIAL", Amount(summary.BacMaterial), "budżet materiałów"));
        Kpis.Add(new KpiTile("WP / CAM", $"{summary.Wps} / {summary.Cams}", "pakiety pracy i ich CAM w strukturze"));
        Kpis.Add(new KpiTile("Okres", summary.Start is null && summary.Finish is null ? "—" : $"{summary.Start ?? "?"} – {summary.Finish ?? "?"}", "planowany start i koniec"));
        Kpis.Add(new KpiTile("Braki", $"{summary.ElementsWithoutWp} / {summary.WpsWithoutBudget}", "elementy CES bez WP / WP bez budżetu"));
    }

    /// <summary>Zapis zmian wiersza struktury od razu po zatwierdzeniu; błąd – zmiany zostają w wierszu, komunikat wyżej.</summary>
    private async Task CommitRow(StructureRowViewModel row)
    {
        var (changes, structureRow, mapping) = (row.Changes.ToDictionary(c => c.Key, c => c.Value), row.Row, _mapping);
        await Try(async () =>
        {
            var (saved, message) = await Busy.Run("Zapisywanie zmiany…", () => _service.SaveStructureEdit(Code, mapping, structureRow, changes));
            if (saved)
                await Reload(refreshMapping: false);
            Status = saved ? message : $"Nie zapisano ({row.Row.Name}): {message} Popraw komórki albo „Odrzuć niezapisane”.";
        });
    }

    private void StartEditObjectives()
    {
        Objectives.Load(_saved.Copy());
        IsEditingObjectives = true;
        Status = "Edycja Performance Objectives – zapis tworzy nowe wersje zmienionych węzłów (historia).";
    }

    private async Task DoSaveObjectives()
    {
        var tree = Objectives.Tree.Copy();
        await Try(async () =>
        {
            var (issues, result) = await Busy.Run("Zapisywanie Performance Objectives…", () => _service.SaveObjectives(Code, tree));
            if (result is null)
            {
                Status = "Performance Objectives mają błędy (ERROR) – nic nie zapisano.";
                return;
            }
            if (!result.Success)
            {
                Status = result.Conflict!;
                return;
            }
            IsEditingObjectives = false;
            await Reload(refreshMapping: false);
            Status = $"Zapisano Performance Objectives: +{result.Added} ~{result.Updated} −{result.Removed}{(issues.Count > 0 ? $" (ostrzeżenia: {issues.Count})" : "")}.";
        });
    }

    /// <summary>Raport kosztów projektu z ostatniego importu ACTUALS do Excela (procedura REP_ProjectCosts).</summary>
    private async Task DoExportCostReport()
    {
        var path = _dialogs.SaveExcel("Raport kosztów projektu", $"{Code}_koszty.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            var rows = await Busy.Run("Raport kosztów – ostatni import ACTUALS projektu…", () => _service.ExportCostReport(path, Code));
            Status = $"Zapisano {path} – {rows} wierszy (Project definition × Cost Element).";
        });
    }

    /// <summary>Dołożenie elementów z kolejnego eksportu SAP (np. nowe Project definition) – istniejąca nakładka bez zmian.</summary>
    private async Task DoAddFromExcel()
    {
        var path = _dialogs.OpenExcel($"Dołóż elementy do projektu {Code} – eksport struktury WBS z SAP");
        if (path is null)
            return;
        await Try(async () =>
        {
            var (message, changed) = await Busy.Run($"Wczytywanie {Path.GetFileName(path)} i dokładanie nowych elementów…", () =>
            {
                var read = _service.ReadObjectives(path);
                if (read.HasErrors)
                    return ($"Plik {read.FileName} ma błędy (ERROR) – nic nie dołożono: {string.Join("; ", read.Issues.Where(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error).Take(3).Select(i => i.Message))}", false);
                var (tree, added, projects) = ProjectService.AddNew(_service.Objectives(Code), read.Tree);
                if (added == 0)
                    return ($"Plik {read.FileName} nie zawiera nowych elementów – struktura bez zmian.", false);
                var (issues, result) = _service.SaveObjectives(Code, tree);
                if (result is null)
                    return ($"Nie dołożono – nakładka miałaby błędy: {string.Join("; ", issues.Where(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error).Take(3).Select(i => i.Message))}", false);
                if (!result.Success)
                    return (result.Conflict!, false);
                return ($"Dołożono {added} elementów{(projects.Count > 0 ? $" (Project definition: {string.Join(", ", projects)})" : "")}.", true);
            });
            if (changed)
                await Reload(refreshMapping: false);
            Status = message;
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
            await Reload(refreshMapping: false);
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
            await Reload(refreshMapping: false);
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
