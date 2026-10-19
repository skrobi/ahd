using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Shared.Utils.Files;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Process;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Ekran Projekt (docs/funkcjonalnosc.md, rozdz. 2; F02) w zakładkach: Wskaźniki (sumy projektu, miejsce na wskaźniki
/// EV, gotowość do przebiegu), Struktura (nakładka Performance Objectives z rozwinięciem P1S, WP, CAM, budżetem
/// i datami – edycja w komórkach zapisywana od razu, dokładanie elementów z kolejnego eksportu SAP), Słowniki projektu
/// (wybrany słownik w tabeli z edycją w komórkach jak na ekranie Słowniki; pobierz / wczytaj z Excela z podglądem
/// różnic), Przebiegi, Foldery.
/// </summary>
public sealed class ProjectDetailViewModel : ObservableObject, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly ProjectService _service;
    private readonly IFileDialogs _dialogs;
    private string _status = "";
    private Pill _readiness = new("muted", "");
    private RunPinRow? _selectedPin;
    private string _runProgress = "";
    private P1sInputs _mapping = P1sInputs.None;
    private PoTree _saved = new();
    private IReadOnlyDictionary<string, string> _owners = new Dictionary<string, string>();
    // Dane zmieniające się rzadko – czytane przy otwarciu projektu i „Odśwież P1S i koszty” (nie po każdym zapisie):
    // osoby (słownik Osoby), elementy CES nakładek innych projektów, kontrola folderów na dysku sieciowym.
    private IReadOnlyList<LookupOption>? _persons;
    private bool _ownersLoaded;
    private (Issue Structure, Issue CamAccess)? _folderChecks;
    private ActualsFreshness? _actuals;   // z kiedy są dane ACTUALS – czytane razem z kosztami
    private ProductionData? _production;  // PZLPROD na żywo (vAHDD, vAPD) – przy otwarciu, „Odśwież” i po zmianie Legacy WBS
    private bool _isEditingObjectives;
    private bool _isReady;
    private DictionaryPanelViewModel? _selectedDictionary;
    private readonly Queue<StructureRowViewModel> _commits = new();
    private bool _committing;
    private int _reloads;
    /// <summary>ACWP po elemencie CES (ostatni import ACTUALS) – czytany przy otwarciu i „Odśwież P1S i koszty”, nie po każdym zapisie.</summary>
    private ElementActuals? _costs;   // ACWP, Actual DLH, ACWP Labor / Material – jeden odczyt ACTUALS
    private string? _currency;          // waluta projektu z kolumny waluty zestawu walutowego (dane ACTUALS projektu)
    private string? _currencyProblem;   // kilka walut w kolumnie waluty zestawu
    private (ProjectStructure Structure, IReadOnlyList<LookupOption> Persons)? _structure;   // ostatnio wczytana – eksport do Excela
    private string? _costsError;
    private IReadOnlyList<SourceDefinitionRow> _sourceDefinitions = [];
    private string _costsField = ProjectSettings.DefaultActualsAmount;   // pole kwoty ACTUALS kosztów w pamięci („Parametry projektu”)

    public ProjectDetailViewModel(ProjectService service, IFileDialogs dialogs, BusyState busy, ProjectInfo project, Action back)
    {
        _service = service;
        _dialogs = dialogs;
        Busy = busy;
        Project = project;
        // Edycja struktury także w trakcie zapisu – zapisy idą po kolei (CommitRow), niezapisane zmiany trwają po odświeżeniu.
        Structure = new StructureViewModel { Commit = CommitRow, CanEditNow = () => !IsEditingObjectives };
        Structure.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StructureViewModel.Notice)) OnPropertyChanged(nameof(StatusLine)); };
        // Edycja nakładki tym samym edytorem co w kreatorze (przeciąganie, przesuwanie, usuwanie, węzły wirtualne);
        // „Wczytaj Excel” podmienia strukturę (elementy o tym samym WBS element zachowują historię).
        Objectives = new PoEditorViewModel(dialogs, busy, service.ReadObjectives, tree => ObjectivesValidator.Validate(tree, _owners), ProjectService.Refresh);
        EditObjectives = new RelayCommand(_ => { if (StructureSettled("edycją Performance Objectives")) StartEditObjectives(); }, _ => !IsEditingObjectives && !Busy.IsBusy);
        SaveObjectives = new AsyncRelayCommand(DoSaveObjectives, () => IsEditingObjectives && Objectives.IsDirty && !Busy.IsBusy);
        CancelObjectives = new RelayCommand(_ => { IsEditingObjectives = false; Status = Objectives.IsDirty ? "Zmiany Performance Objectives odrzucone." : ""; },
            _ => IsEditingObjectives && !Busy.IsBusy);
        Back = new RelayCommand(_ => { if (StructureSettled("powrotem do listy")) back(); }, _ => !Busy.IsBusy);
        AddFromExcel = new AsyncRelayCommand(DoAddFromExcel, () => !Busy.IsBusy);
        ExportCostReport = new AsyncRelayCommand(DoExportCostReport, () => !Busy.IsBusy);
        ExportCasCostReport = new AsyncRelayCommand(DoExportCasCostReport, () => !Busy.IsBusy);
        ExportStructure = new AsyncRelayCommand(DoExportStructure, () => _structure is not null && !Busy.IsBusy);
        ImportStructure = new AsyncRelayCommand(DoImportStructure, () => _structure is not null && !Busy.IsBusy);
        ExportDictionaries = new AsyncRelayCommand(DoExportDictionaries, () => !Busy.IsBusy);
        CreateFolders = new AsyncRelayCommand(DoCreateFolders, () => !Busy.IsBusy);
        RefreshMapping = new AsyncRelayCommand(() => { Structure.CommitEdits(); return Reload(refreshMapping: true); }, () => !Busy.IsBusy);
        ImportSources = new AsyncRelayCommand(DoImportSources, () => !Busy.IsBusy && _service.Imports is not null);
        // Projekt niegotowy (ERROR gotowości) – przebieg zablokowany (docs/pipeline-fazy.md, P0 – bramka wejścia).
        StartRun = new AsyncRelayCommand(DoStartRun, () => IsReady && _service.Runs is { } r && !r.IsRunning(Code));
        if (_service.Runs is { } launcher)
        {
            launcher.Progress += OnRunProgress;
            launcher.Finished += OnRunFinished;
            if (launcher.IsRunning(Code))
                RunProgress = "Przebieg trwa…";
            _ = LoadRuns();
        }
        foreach (var item in ProjectDictionaries.ForType(project.Type))
        {
            var panel = new DictionaryPanelViewModel(item, project.Type);
            panel.Load = new AsyncRelayCommand(() => LoadDictionary(panel), () => item.Stored && !Busy.IsBusy);
            panel.Apply = new AsyncRelayCommand(() => ApplyDictionary(panel), () => panel.Preview is { HasErrors: false, HasChanges: true } && !Busy.IsBusy);
            panel.Remove = new RelayCommand(_ => { panel.SetPreview(null, ""); Status = "Wczytanie anulowane – słownik bez zmian."; }, _ => panel.HasPreview && !Busy.IsBusy);
            Dictionaries.Add(panel);
        }
        ProjectDictionaryList = Dictionaries.Where(p => !ProjectDictionaries.Configuration.Contains(p.Item.Code)).ToList();
        ConfigDictionaryList = Dictionaries.Where(p => ProjectDictionaries.Configuration.Contains(p.Item.Code)).ToList();
        AddDictionaryRow = new RelayCommand(_ => { Table.AddRow(); Status = "Dodano wiersz – uzupełnij wartości i zapisz."; },
            _ => Table.Spec is not null && !Busy.IsBusy);
        RemoveDictionaryRow = new RelayCommand(_ => DoRemoveDictionaryRow(), _ => Table.SelectedRow is not null && !Busy.IsBusy);
        SaveDictionary = new AsyncRelayCommand(() => DoSaveDictionary(confirmWarnings: false), () => Table.Spec is not null && !Busy.IsBusy);
        Table.SaveWithWarnings = new AsyncRelayCommand(() => DoSaveDictionary(confirmWarnings: true), () => Table.NeedsConfirmation && !Busy.IsBusy);
        // Dostępne zawsze – komórka w trakcie edycji nie jest jeszcze zmianą wiersza (Reload ją anuluje).
        DiscardDictionary = new AsyncRelayCommand(() => Reload(refreshMapping: false, discard: true), () => Table.Spec is not null && !Busy.IsBusy);
        _selectedDictionary = ProjectDictionaryList.FirstOrDefault();
        _ = Reload(refreshMapping: false);
    }

    public BusyState Busy { get; }

    public ProjectInfo Project { get; }

    public string Code => Project.Code;

    public string Title => $"{Project.Code} · {Project.Name}";

    /// <summary>Z kiedy są dane RABIT (ACTUALS) – data raportu w RABIT, import, ostatnie sprawdzenie (nagłówek).</summary>
    public string DataInfo => _actuals?.Summary ?? "Dane RABIT: wczytywanie…";

    public string DataInfoDetails => _actuals?.Details ?? "";

    /// <summary>Dane produkcyjne (PZLPROD na żywo): chwila odczytu albo powód braku (nagłówek).</summary>
    /// <summary>Waluta projektu w nagłówku: kod z danych ACTUALS i zestaw walutowy („Parametry projektu”).</summary>
    public string CurrencyInfo
    {
        get
        {
            var set = ProjectSettings.SetOf(_costsField);
            return _currencyProblem is not null ? $"Waluta: ⚠ kilka walut ({set.CurrencyColumn})"
                : $"Waluta: {_currency ?? "—"} ({set.CurrencyColumn} / {set.AmountColumn})";
        }
    }

    public string CurrencyDetails => _currencyProblem
        ?? $"Zestaw walutowy ACTUALS (Konfiguracja → „Parametry programu”): kwota {ProjectSettings.SetOf(_costsField).AmountColumn}, waluta z kolumny "
           + $"{ProjectSettings.SetOf(_costsField).CurrencyColumn} ostatniego importu ACTUALS programu{(_currency is null ? " – brak danych" : "")}. "
           + "W tej walucie: ACWP, raport kosztów, materiały z vAPD (USD przeliczone kursem bieżącym), BAC Material, BAC Cost, ETC Cost i EAC; "
           + "kwoty „Harmonogram i budżet” zapisują walutę programu (wpisane w innej walucie – przeliczone).";

    public string ProductionInfo => _production is null ? "Produkcja: wczytywanie…"
        : _production.Error is null ? $"Produkcja (PZLPROD) {_production.ReadAt:yyyy-MM-dd HH:mm}" : "Produkcja: brak danych";

    public string ProductionDetails => _production is null ? ""
        : (_production.Error ?? $"Operational EV i Actual Material odczytane na żywo z PZLPROD (LOG.vAHDD, LOG.vAPD) o {_production.ReadAt:yyyy-MM-dd HH:mm} – " +
            $"elementów z danymi: {_production.ByPspnr.Count}. Ponowny odczyt: „Odśwież P1S i koszty”.")
          + (_production.Note is { } note ? $"\nUwaga: {note}" : "")
          + "\nZapytanie: sql/pzlprod/produkcja.sql; parametry – słowniki „Wskaźniki DJK” i „Parametry produkcji”; wydzielenia (np. Paint) – węzły wirtualne nakładki z kodem P1S ELEMENT_P1S.NAZWA.CPLGR.";

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

    /// <summary>Wskaźniki w grupach jak w tabeli struktury: Operational EV, Materials, Financial EV.</summary>
    public ObservableCollection<KpiGroup> Kpis { get; } = [];

    /// <summary>Wskaźniki EV – liczone w przebiegach (docs/ev-obliczenia.md); do tego czasu kafelki bez wartości.</summary>
    private static readonly IReadOnlyList<KpiTile> EvKpis =
        new[] { "BCWS", "BCWP", "CPI", "SPI", "EAC" }.Select(k => new KpiTile(k, "—", "po obliczeniu EV w przebiegu", Pending: true)).ToList();

    public ObservableCollection<Issue> Readiness { get; } = [];

    /// <summary>Czy projekt jest gotowy do przebiegu (kontrole F02 bez ERROR).</summary>
    public bool IsReady { get => _isReady; private set => SetProperty(ref _isReady, value); }

    public string MappingInfo => _mapping.Describe;

    public ObservableCollection<DictionaryPanelViewModel> Dictionaries { get; } = [];

    /// <summary>Słownik projektu pokazany w tabeli (zakładka „Słowniki projektu”).</summary>
    public DictionaryPanelViewModel? SelectedDictionary
    {
        get => _selectedDictionary;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedDictionary))
                return;
            Table.CommitEdits();
            if (Table.HasPendingChanges || Busy.IsBusy)
            {
                Status = Busy.IsBusy ? "Poczekaj na zakończenie bieżącej operacji." : "Masz niezapisane zmiany słownika – zapisz albo odrzuć je przed zmianą słownika.";
                OnSelectionChanged();
                return;
            }
            _selectedDictionary = value;
            OnSelectionChanged();
            _ = Reload(refreshMapping: false);
        }
    }

    /// <summary>Definicje źródeł (Administracja) ze słownika „Źródła danych” projektu – zakładka „Konfiguracja”, tylko odczyt.</summary>
    public IReadOnlyList<SourceDefinitionRow> SourceDefinitions { get => _sourceDefinitions; private set => SetProperty(ref _sourceDefinitions, value); }

    /// <summary>Słowniki zakładki „Słowniki projektu” (bez słowników konfiguracji).</summary>
    public IReadOnlyList<DictionaryPanelViewModel> ProjectDictionaryList { get; }

    /// <summary>Słowniki zakładki „Konfiguracja”: Źródła danych, Parametry projektu.</summary>
    public IReadOnlyList<DictionaryPanelViewModel> ConfigDictionaryList { get; }

    /// <summary>Wybrany słownik, gdy należy do listy zakładki „Słowniki projektu” (inaczej brak zaznaczenia).</summary>
    public DictionaryPanelViewModel? SelectedProjectDictionary
    {
        get => _selectedDictionary is { } d && ProjectDictionaryList.Contains(d) ? d : null;
        set => SelectedDictionary = value;
    }

    /// <summary>Wybrany słownik, gdy należy do listy zakładki „Konfiguracja”.</summary>
    public DictionaryPanelViewModel? SelectedConfigDictionary
    {
        get => _selectedDictionary is { } d && ConfigDictionaryList.Contains(d) ? d : null;
        set => SelectedDictionary = value;
    }

    /// <summary>Otwarcie zakładki „Słowniki projektu” – tabela pokazuje słownik z jej listy.</summary>
    public bool DictionaryTabOpen
    {
        get => SelectedProjectDictionary is not null;
        set { if (value) SelectFrom(ProjectDictionaryList); }
    }

    /// <summary>Otwarcie zakładki „Konfiguracja” – tabela pokazuje słownik konfiguracji.</summary>
    public bool ConfigTabOpen
    {
        get => SelectedConfigDictionary is not null;
        set { if (value) SelectFrom(ConfigDictionaryList); }
    }

    /// <summary>Słownik z listy zakładki: bieżący, jeśli z niej pochodzi, inaczej pierwszy (niezapisane zmiany – bez zmiany).</summary>
    private void SelectFrom(IReadOnlyList<DictionaryPanelViewModel> list)
    {
        if (_selectedDictionary is null || !list.Contains(_selectedDictionary))
            SelectedDictionary = list.FirstOrDefault();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedDictionary));
        OnPropertyChanged(nameof(SelectedProjectDictionary));
        OnPropertyChanged(nameof(SelectedConfigDictionary));
    }

    /// <summary>Wiersze wybranego słownika projektu – edycja w komórkach (wspólna tabela słownika).</summary>
    public DictionaryTableViewModel Table { get; } = new();

    public Pill ReadinessPill { get => _readiness; private set => SetProperty(ref _readiness, value); }

    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(StatusLine));
        }
    }

    /// <summary>
    /// Komunikat w stałym miejscu pod nagłówkiem (jedna linia, pełna treść w podpowiedzi): wynik ostatniej operacji
    /// i wynik operacji na komórkach struktury (wklejenie, Delete, Ctrl+D, Ctrl+Z) – bez przesuwania tabeli.
    /// </summary>
    public string StatusLine => string.Join(" · ", new[] { _status, Structure.Notice }.Where(t => !string.IsNullOrWhiteSpace(t)));

    public string FolderPath => _service.Folders.PathOf(Code);

    public string FolderPreview => _service.Folders.Preview(Code);

    public string ItRequest => _service.Folders.ItRequest(Code);

    /// <summary>Uruchomienia przebiegów projektu (od najnowszego) – zakładka Przebiegi.</summary>
    public ObservableCollection<RunPinRow> RunPins { get; } = [];

    /// <summary>Wyniki kontroli wybranego uruchomienia (błędy i ostrzeżenia przed informacjami).</summary>
    public ObservableCollection<RunIssueRow> RunIssues { get; } = [];

    public RunPinRow? SelectedPin
    {
        get => _selectedPin;
        set
        {
            if (SetProperty(ref _selectedPin, value))
                _ = LoadIssues(value);
        }
    }

    /// <summary>Postęp przebiegu w tle (pusty – przebieg nie trwa).</summary>
    public string RunProgress { get => _runProgress; private set => SetProperty(ref _runProgress, value); }

    /// <summary>„Uruchom przebieg” – w tle; wynik w zakładce Przebiegi, na liście projektów i w powiadomieniu.</summary>
    public ICommand StartRun { get; }

    private async Task DoStartRun()
    {
        if (_service.Runs is not { } runs)
            return;
        var refused = await runs.Start(Code);
        RunProgress = refused ?? "Przebieg uruchomiony…";
        if (refused is null)
            Status = "Przebieg liczy się w tle – możesz dalej pracować; po zakończeniu pojawi się powiadomienie.";
    }

    /// <summary>Odpięcie od zdarzeń przebiegów przy zmianie strony modułu.</summary>
    public void Dispose()
    {
        if (_service.Runs is { } runs)
        {
            runs.Progress -= OnRunProgress;
            runs.Finished -= OnRunFinished;
        }
    }

    private void OnRunProgress(string project, string text)
    {
        if (string.Equals(project, Code, StringComparison.OrdinalIgnoreCase))
            RunProgress = text;
    }

    private void OnRunFinished(string project)
    {
        if (!string.Equals(project, Code, StringComparison.OrdinalIgnoreCase))
            return;
        RunProgress = "";
        CommandManager.InvalidateRequerySuggested();   // „Uruchom przebieg” znów dostępny
        _ = LoadRuns();
    }

    private async Task LoadRuns()
    {
        if (_service.Runs is not { } runs)
            return;
        await Try(async () =>
        {
            var pins = await Task.Run(() => runs.Pins(Code, 20));
            RunPins.Clear();
            foreach (var pin in pins)
                RunPins.Add(pin);
            SelectedPin = RunPins.FirstOrDefault();
        });
    }

    private async Task LoadIssues(RunPinRow? pin)
    {
        RunIssues.Clear();
        if (pin is null || _service.Runs is not { } runs)
            return;
        await Try(async () =>
        {
            var issues = await Task.Run(() => runs.Issues(pin.PinId));
            if (!ReferenceEquals(pin, _selectedPin))
                return;
            foreach (var issue in issues.OrderBy(i => i.Level == CheckLevel.Error ? 0 : i.Level == CheckLevel.Warning ? 1 : 2).ThenBy(i => i.Id))
                RunIssues.Add(issue);
        });
    }

    public ICommand Back { get; }
    public ICommand AddFromExcel { get; }
    public ICommand EditObjectives { get; }
    public ICommand SaveObjectives { get; }
    public ICommand CancelObjectives { get; }
    public ICommand ExportCostReport { get; }

    /// <summary>Raport kosztów CES z narzutami CAS w formułach (zakładka Raporty – tylko projekty CAS).</summary>
    public ICommand ExportCasCostReport { get; }

    /// <summary>Proces projektu (ścieżka wspólna albo wariant typu – ProjectService.Process).</summary>
    private ProjectProcess ProjectProcess => _projectProcess ??= _service.Process(Code, Project.Type, null);
    private ProjectProcess? _projectProcess;

    /// <summary>Raport kosztów z narzutami w formułach (raport kosztów CES) – projekty, których proces go ma (CAS).</summary>
    public bool HasBurdenReport => ProjectProcess.HasBurdenReport;
    public ICommand ExportStructure { get; }

    /// <summary>„Wczytaj strukturę z Excela” – WP, CAM, Cost Category, budżet i daty z arkusza „Struktura” uzupełnionego ręcznie.</summary>
    public ICommand ImportStructure { get; }
    public ICommand ExportDictionaries { get; }
    public ICommand AddDictionaryRow { get; }
    public ICommand RemoveDictionaryRow { get; }
    public ICommand SaveDictionary { get; }
    public ICommand DiscardDictionary { get; }
    public ICommand CreateFolders { get; }
    public ICommand RefreshMapping { get; }

    /// <summary>„Importuj źródła projektu” – pliki definicji ze słownika „Źródła danych”, potem odświeżenie kosztów.</summary>
    public ICommand ImportSources { get; }

    /// <summary>Stan projektu z bazy w jednym odczycie w tle: nakładka, struktura P1S, gotowość, słowniki, struktura.</summary>
    private sealed record Snapshot(PoTree Tree, P1sInputs Mapping, IReadOnlyDictionary<string, string> Owners, List<Issue> Readiness,
        IReadOnlyDictionary<string, (int Rows, string LastChange)> Dictionaries, ProjectStructure Structure, IReadOnlyList<LookupOption> Persons,
        IReadOnlyList<LookupOption> Categories,
        IReadOnlyList<(DictRow Row, bool Inherited)>? Table, IReadOnlyDictionary<string, IReadOnlyList<LookupOption>> Lookups,
        ElementActuals Costs, string? CostsError, string CostsField, string? Currency, string? CurrencyProblem, IReadOnlyList<LookupOption> Mpks, IReadOnlyList<LookupOption> PersonLookups, (Issue Structure, Issue CamAccess) FolderChecks,
        ActualsFreshness Actuals, ProductionData Production, IReadOnlyList<SourceDefinitionRow> SourceDefinitions);

    /// <summary>
    /// Wczytuje stan projektu w tle (struktura zachowuje rozwinięcie i zaznaczenie). Tabela wybranego słownika – tylko
    /// bez niezapisanych zmian, chyba że discard (Odrzuć zmiany). Każdy słownik projektu jest czytany raz (wiersze, liczba,
    /// ostatnia zmiana, struktura, gotowość i tabela z tego samego odczytu); osoby, nakładki
    /// innych projektów, foldery i koszty – z pamięci, ponownie przy refreshMapping („Odśwież P1S i koszty”).
    /// </summary>
    private async Task Reload(bool refreshMapping, bool discard = false, IReadOnlySet<string>? saved = null)
    {
        var version = ++_reloads;
        var codes = Dictionaries.Where(p => p.Item.Stored).Select(p => p.Item.Code).ToList();
        var selected = _selectedDictionary;
        var table = selected is { Item.Stored: true } && (discard || !Table.HasPendingChanges) ? selected.Item.Code : null;
        var cachedPersons = refreshMapping ? null : _persons;
        var cachedFolders = refreshMapping ? null : _folderChecks;
        var cachedOwners = refreshMapping || !_ownersLoaded ? null : _owners;
        var cachedActuals = refreshMapping ? null : _actuals;
        var cachedProduction = refreshMapping ? null : _production;
        CostData? cachedCosts = _costs is null ? null : new CostData(_costs, _costsError, _costsField, _currency, _currencyProblem);
        await Try(async () =>
        {
            var steps = new StepTimer();
            var snapshot = await Busy.Run(refreshMapping ? "Odświeżanie struktury P1S, kosztów i struktury programu…" : "Wczytywanie programu i struktury…", () =>
            {
                // koszty (PZLTEST) – równolegle z odczytami PZLPROD (struktura P1S, potem produkcja); z pamięci, gdy pole kwoty
                // („Parametry projektu”) się nie zmieniło
                var costsRead = Task.Run(() => ReadCosts(steps, refreshMapping ? null : cachedCosts));
                var tree = steps.Time("nakładka Performance Objectives", () => _service.Objectives(Code));
                var mapping = steps.Time("struktura P1S (PZLPROD LOG.WBS)", () => _service.Mapping(refreshMapping));
                var states = steps.Time("słowniki programu", () => codes.ToDictionary(c => c, c => _service.DictionaryState(c, Code)));
                var rows = states.ToDictionary(s => s.Key, s => s.Value.Rows);
                var wpCam = rows.GetValueOrDefault(ProjectDictionaries.WpCam) ?? [];
                var production = cachedProduction ?? steps.Time("produkcja (PZLPROD vAHDD, vAPD)", () => _service.Production(tree, mapping));
                var (costs, costsError, costsField, currency, currencyProblem) = costsRead.GetAwaiter().GetResult();
                // Stawki MPK i kursy – ETC Cost struktury (waluta projektu z pola kwoty ACTUALS); lista MPK w „WP i CAM”.
                var departmentRates = steps.Time("stawki wydziałów i kursy walut", _service.DepartmentRates);
                var rates = ProjectService.EtcRatesOf(departmentRates, _service.FxRates(), currency, currencyProblem, DateOnly.FromDateTime(DateTime.Today));
                var mpks = GlobalDictionaries.LookupOptions(GlobalDictionaries.DepartmentRates, departmentRates);
                var structure = steps.Time("budowa struktury", () =>
                    ProjectService.Structure(tree, mapping, wpCam, rows.GetValueOrDefault(ProjectDictionaries.ScheduleBudget) ?? [], costs.Amounts, production,
                        costs.Hours, costs.Labor, costs.Material, rates));
                var persons = cachedPersons ?? steps.Time("osoby", _service.PersonLookups);
                var folderChecks = cachedFolders ?? steps.Time("foldery (dysk sieciowy)", () => _service.FolderChecks(Code));
                var categories = ProjectService.CategoryLookups(rows.GetValueOrDefault(ProjectDictionaries.WbsCategories) ?? []);
                var readiness = steps.Time("gotowość", () => ProjectService.Readiness(Project, tree, mapping, rows,
                    persons.Select(p => p.Value).ToHashSet(StringComparer.OrdinalIgnoreCase), folderChecks));
                var owners = cachedOwners ?? steps.Time("nakładki innych programów", () => _service.WbsOwners(Code));
                var editable = table is null ? null : steps.Time("tabela słownika", () =>
                    _service.EditableRows(table, rows.TryGetValue(table, out var tableRows) ? tableRows : _service.Rows(table, Code)));
                var definitions = steps.Time("definicje źródeł", _service.SourceDefinitions);
                var sources = ProjectService.SourceOptions(definitions);
                var burdenGroups = steps.Time("grupy narzutowe CAS", _service.BurdenGroupLookups);
                var projectSources = (rows.GetValueOrDefault(ProjectDictionaries.Sources) ?? []).Select(r => r["Definicja źródła"]).OfType<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var actuals = cachedActuals ?? steps.Time("data danych ACTUALS", ReadActuals);
                return new Snapshot(tree, mapping, owners, readiness,
                    states.ToDictionary(s => s.Key, s => (s.Value.Rows.Count, s.Value.LastChange)), structure, ProjectService.PersonOptions(persons, wpCam),
                    ProjectService.CategoryOptions(categories, wpCam), editable,
                    ProjectService.Lookups(persons, categories, sources, burdenGroups, mpks), costs, costsError, costsField, currency, currencyProblem, mpks, persons, folderChecks, actuals, production,
                    definitions.Where(d => projectSources.Contains(d.Code)).ToList());
            });
            Logger.Information("Program {Code}: wczytanie {Steps}", Code, steps.Summary);
            if (version != _reloads)
                return;   // w międzyczasie ruszyło nowsze odświeżenie – starszy wynik pomijany
            _saved = snapshot.Tree;
            (_costs, _costsError, _costsField, _currency, _currencyProblem) = (snapshot.Costs, snapshot.CostsError, snapshot.CostsField, snapshot.Currency, snapshot.CurrencyProblem);
            OnPropertyChanged(nameof(CurrencyInfo));
            OnPropertyChanged(nameof(CurrencyDetails));
            _mapping = snapshot.Mapping;
            _owners = snapshot.Owners;
            _ownersLoaded = true;
            _persons = snapshot.PersonLookups;
            _folderChecks = snapshot.FolderChecks;
            _actuals = snapshot.Actuals;
            _production = snapshot.Production;
            SourceDefinitions = snapshot.SourceDefinitions;
            OnPropertyChanged(nameof(ProductionInfo));
            OnPropertyChanged(nameof(ProductionDetails));
            OnPropertyChanged(nameof(DataInfo));
            OnPropertyChanged(nameof(DataInfoDetails));
            Objectives.MappingInfo = _mapping.Describe;
            if (IsEditingObjectives)
                Objectives.Refresh();
            OnPropertyChanged(nameof(MappingInfo));
            Structure.Load(snapshot.Structure, snapshot.Persons, snapshot.Categories, saved, snapshot.Mpks);
            _structure = (snapshot.Structure, snapshot.Persons);
            ShowKpis(snapshot.Structure.Summary);
            foreach (var panel in Dictionaries.Where(p => p.Item.Stored))
                (panel.Rows, panel.LastChange) = snapshot.Dictionaries[panel.Item.Code];
            if (ReferenceEquals(selected, _selectedDictionary) && (snapshot.Table is not null || selected is { Item.Stored: false }))
                Table.Load(selected is { Item.Stored: true } ? ProjectDictionaries.Base(selected.Item.Code) : null, snapshot.Table ?? [], snapshot.Lookups);
            if (discard)
                Status = "Zmiany słownika odrzucone.";
            Readiness.Clear();
            foreach (var check in snapshot.Readiness)
                Readiness.Add(check);
            IsReady = ProjectReadiness.IsReady(snapshot.Readiness);
            ReadinessPill = IsReady ? new Pill("ok", "gotowy do przebiegu") : new Pill("crit", "niegotowy – przebieg zablokowany");
            CommandManager.InvalidateRequerySuggested();   // „Uruchom przebieg” według gotowości
            if (refreshMapping)
                Status = _costsError is null ? "Odświeżono strukturę P1S i koszty (ostatni import ACTUALS)." : $"Odświeżono strukturę P1S; {_costsError}";
        });
    }

    /// <summary>Daty danych ACTUALS (w tle); błąd odczytu – informacja w nagłówku, ekran działa dalej.</summary>
    private ActualsFreshness ReadActuals()
    {
        try
        {
            return _service.ActualsFreshness();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Daty danych ACTUALS programu {Code}", Code);
            return new ActualsFreshness([], ex.Message);
        }
    }

    /// <summary>ACWP i Actual DLH po elemencie CES, błąd odczytu kosztów i pole kwoty ACTUALS.</summary>
    private sealed record CostData(ElementActuals Actuals, string? Error, string Field, string? Currency = null, string? CurrencyProblem = null);

    /// <summary>
    /// ACWP po elemencie CES (w tle) z pola kwoty ACTUALS projektu i Actual DLH (Total Quantity); cached – koszty w pamięci,
    /// użyte przy tym samym polu – jedno wywołanie procedury. Brak danych lub procedury – pusta lista i powód (struktura bez
    /// kosztów); brak pola godzin w parserze – kolumna Actual DLH pusta.
    /// </summary>
    private CostData ReadCosts(StepTimer steps, CostData? cached)
    {
        var field = ProjectSettings.DefaultActualsAmount;
        try
        {
            field = steps.Time("pole kwoty ACTUALS", () => _service.ActualsAmount(Code));
            if (cached is { } c && c.Field == field)
                return c;
            var actuals = steps.Time("koszty ACWP i godziny Actual DLH (ACTUALS)", () => _service.CostsAndHoursByElement(Code, field));
            var (currency, problem) = steps.Time("waluta programu (ACTUALS)", () => _service.ProjectCurrency(Code, field));
            return new CostData(actuals, null, field, currency, problem);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Koszty programu {Code}", Code);
            return new CostData(ElementActuals.Empty, $"koszty (ACWP) niedostępne: {ex.Message}", field);
        }
    }

    private void ShowKpis(StructureSummary summary)
    {
        static string Amount(decimal value) => PzlEv.Shared.Utils.Files.PolishNumber.ToDisplay(Math.Round(value, 2));
        Kpis.Clear();
        Kpis.Add(new KpiGroup(StructureColumns.OperationalEv,
            [new KpiTile("BAC HOURS", Amount(summary.BacHours), "budżet godzin – Harmonogram i budżet")]));
        Kpis.Add(new KpiGroup(StructureColumns.Materials,
            [new KpiTile("BAC MATERIAL", Amount(summary.BacMaterial), "budżet materiałów")]));
        Kpis.Add(new KpiGroup(StructureColumns.FinancialEv,
        [
            new KpiTile("BAC", Amount(summary.Bac), "budżet kosztowy – Harmonogram i budżet"),
            _costsError is null
                ? new KpiTile("ACWP", Amount(summary.Acwp), ProjectProcess.AcwpDescription(_currency)
                    ?? $"koszt rzeczywisty narastająco – {ProjectSettings.SetOf(_costsField).AmountColumn}, {_currency ?? ProjectSettings.CurrencyOf(_costsField)}; ostatni import ACTUALS, bez wykluczeń")
                : new KpiTile("ACWP", "—", _costsError, Pending: true),
            new KpiTile("Koszt bez WP", Amount(summary.AcwpWithoutWp + summary.AcwpOutside),
                $"bez przypisania do WP: {Amount(summary.AcwpWithoutWp)} (brak WP / kilka WP – O45); elementy CES spoza nakładki: {Amount(summary.AcwpOutside)}"),
            .. EvKpis,
        ]));
    }

    /// <summary>Czas zbierania zmian przed zapisem – kilka kliknięć WP albo Enter w kolejnych wierszach idzie jednym zapisem.</summary>
    private static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Zapis zmian wiersza struktury od razu po zatwierdzeniu. Wiersze zatwierdzone w krótkim czasie (kilka WP, Enter
    /// w kolejnych wierszach, wklejenie bloku) czekają w kolejce i są zapisywane razem – jednym zapisem na słownik
    /// (ProjectService.SaveStructureEdits); po serii – jedno odświeżenie danych projektu (także po nieudanym zapisie –
    /// część zmian mogła się zapisać). Nieudany zapis – zmiany zostają w wierszu (żółty), komunikat wyżej.
    /// </summary>
    private async Task CommitRow(StructureRowViewModel row)
    {
        if (!_commits.Contains(row))
            _commits.Enqueue(row);
        if (_committing)
            return;
        _committing = true;
        var saved = new HashSet<string>();
        var messages = new List<string>();
        var failures = new List<string>();
        try
        {
            await Task.Delay(CommitDelay);
            while (_commits.Count > 0)
            {
                var edits = new List<StructureEdit>();
                var names = new Dictionary<string, string>();
                while (_commits.TryDequeue(out var next))
                {
                    if (next.Changes.Count == 0 || !ConfirmWpRemoval(next))
                        continue;
                    edits.Add(new StructureEdit(next.Id, next.Row, next.Changes.ToDictionary(c => c.Key, c => c.Value)));
                    if (next.Changes.ContainsKey(StructureEdits.P1s))
                        _production = null;   // zmiana Legacy WBS / kodu wydzielenia – dane produkcyjne czytane ponownie
                    names[next.Id] = next.Row.Name;
                }
                if (edits.Count == 0)
                    continue;
                var mapping = _mapping;
                await Try(async () =>
                {
                    var results = await Busy.Run(edits.Count > 1 ? $"Zapisywanie zmian ({edits.Count} wierszy)…" : "Zapisywanie zmiany…",
                        () => _service.SaveStructureEdits(Code, mapping, edits));
                    foreach (var result in results)
                    {
                        if (result.Saved)
                        {
                            saved.Add(result.Id);
                            messages.Add(result.Message);
                        }
                        else
                            failures.Add($"{names[result.Id]}: {result.Message}");
                    }
                });
            }
            await Reload(refreshMapping: false, saved: saved);
            Status = failures.Count > 0
                ? $"Nie zapisano ({failures.Count}): {string.Join(" · ", failures.Take(3))}{(failures.Count > 3 ? " …" : "")} Popraw komórki (wiersze żółte) albo „Odrzuć niezapisane”."
                : saved.Count > 1 ? $"Zapisano wierszy: {saved.Count}." : messages.LastOrDefault() ?? Status;
        }
        finally
        {
            _committing = false;
        }
        if (_commits.Count > 0)
            await CommitRow(_commits.Peek());
    }

    /// <summary>Odznaczenie WP z budżetem lub datami usuwa harmonogram WP – wymaga potwierdzenia; „nie” cofa znacznik.</summary>
    private bool ConfirmWpRemoval(StructureRowViewModel row)
    {
        if (!row.Changes.TryGetValue(StructureEdits.Wp, out var flag) || StructureEdits.IsChecked(flag) || row.Row.Wp is not { } wp
            || row.Row.OwnsBudget is false || (row.Row.BacHours == 0 && row.Row.BacMaterial == 0 && row.Row.Bac == 0 && row.Row.Start is null && row.Row.Finish is null))
            return true;
        if (_dialogs.Confirm("Odznaczenie WP",
                $"Odznaczenie WP {wp} („{row.Row.Name}”) usunie jego przypisanie (CAM, Cost Category) oraz harmonogram i budżet WP. Kontynuować?"))
            return true;
        row[StructureEdits.Wp] = "true";
        return row.Changes.Count > 0;
    }

    /// <summary>Przed wyjściem z tabeli struktury: edycja w toku zatwierdzona; zapis w toku albo nieudany – komunikat.</summary>
    private bool StructureSettled(string action)
    {
        Structure.CommitEdits();
        if (!_committing && _commits.Count == 0 && !Structure.HasChanges)
            return true;
        Status = _committing || _commits.Count > 0
            ? $"Trwa zapis zmian struktury – poczekaj przed {action}."
            : $"Struktura ma niezapisane zmiany (wiersze żółte) – popraw je albo „Odrzuć niezapisane” przed {action}.";
        return false;
    }

    private void StartEditObjectives()
    {
        Objectives.Load(_saved.Copy());
        IsEditingObjectives = true;
        Status = "Edycja Performance Objectives – zapis tworzy nowe wersje zmienionych węzłów (historia).";
    }

    private async Task DoSaveObjectives()
    {
        Objectives.CommitEdits();
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
            _production = null;   // nakładka zmienia zakres P1S i wydzielenia – dane produkcyjne czytane ponownie
            await Reload(refreshMapping: false);
            Status = $"Zapisano Performance Objectives: +{result.Added} ~{result.Updated} −{result.Removed}{(issues.Count > 0 ? $" (ostrzeżenia: {issues.Count})" : "")}.";
        });
    }

    /// <summary>Raport kosztów projektu z ostatniego importu ACTUALS do Excela (procedura REP_ProjectCosts).</summary>
    private async Task DoExportCostReport()
    {
        var path = _dialogs.SaveExcel("Raport kosztów programu", $"{Code}_koszty.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            var rows = await Busy.Run("Raport kosztów – ostatni import ACTUALS programu…", () => _service.ExportCostReport(path, Code));
            Status = $"Zapisano {path} – {rows} wierszy (Project definition × Cost Element).";
        });
    }

    /// <summary>Raport kosztów CES do Excela: raport kosztów z narzutami CAS liczonymi formułami (ProjectService.ExportCasCostReport).</summary>
    private async Task DoExportCasCostReport()
    {
        var path = _dialogs.SaveExcel("Raport kosztów CES", $"{Code}_koszty_CES.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            var (rows, notes) = await Busy.Run("Raport kosztów CES – ostatni import ACTUALS i narzuty CAS…", () => _service.ExportCasCostReport(path, Code));
            Status = $"Zapisano {path} – {rows} wierszy (Project definition × Cost Element)"
                     + (notes > 0 ? $"; uwagi: {notes} (arkusz „Uwagi” – brak grupy narzutowej albo wskaźnika)." : ".");
        });
    }

    /// <summary>Struktura projektu (stan zapisany w bazie, wszystkie kolumny) do Excela; niezapisane zmiany – najpierw zapis.</summary>
    private async Task DoExportStructure()
    {
        if (!StructureSettled("pobraniem struktury do Excela") || _structure is not { } current)
            return;
        var path = _dialogs.SaveExcel("Pobierz strukturę programu", $"{Code}_struktura.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            var rows = await Busy.Run("Zapisywanie struktury programu do Excela…", () => _service.ExportStructure(path, Code, current.Structure, current.Persons, Structure.Mpks.ToList()));
            Status = $"Zapisano {path} – {rows} wierszy struktury.";
        });
    }

    /// <summary>
    /// Wczytanie arkusza „Struktura” uzupełnionego w Excelu (StructureImport): podsumowanie zmian i problemów pliku do
    /// potwierdzenia, potem zapis jak edycja w tabeli struktury (SaveStructureEdits) i odświeżenie projektu.
    /// </summary>
    private async Task DoImportStructure()
    {
        if (!StructureSettled("wczytaniem struktury z Excela") || _structure is not { } current)
            return;
        var path = _dialogs.OpenExcel($"Wczytaj strukturę programu {Code} z Excela (arkusz „Struktura” pobrany z aplikacji)");
        if (path is null)
            return;
        await Try(async () =>
        {
            var file = await Busy.Run("Odczyt pliku struktury…", () => _service.ReadStructureFile(path, Code, current.Structure));
            var problems = file.Issues.OrderBy(i => i.Level == CheckLevel.Error ? 0 : 1)
                .Select(i => $"• {(i.Level == CheckLevel.Error ? "BŁĄD" : "UWAGA")} {i.Element}: {i.Message}").ToList();
            var shown = string.Join("\n", problems.Take(10)) + (problems.Count > 10 ? $"\n… (+{problems.Count - 10})" : "");
            if (file.Edits.Count == 0)
            {
                Status = problems.Count == 0 ? $"{Path.GetFileName(path)}: brak zmian względem programu." : $"{Path.GetFileName(path)}: brak zmian do zapisu – {problems[0]}";
                return;
            }
            var budget = file.Edits.Count(e => e.Changes.Keys.Any(StructureEdits.ScheduleColumns.Contains));
            var message = $"Plik {Path.GetFileName(path)} ({file.FileRows} wierszy) – zmiany w {file.Edits.Count} wierszach struktury:\n"
                          + $"• nowe WP: {file.Count(StructureEdits.Wp)}\n• CAM: {file.Count(StructureEdits.Cam)}\n• Cost Category: {file.Count(StructureEdits.CostCategory)}\n"
                          + $"• budżet i daty WP: {budget}\n\nPuste komórki – bez zmiany."
                          + (problems.Count > 0 ? $"\n\nPominięte ({problems.Count}):\n{shown}" : "")
                          + "\n\nZapisać zmiany?";
            if (!_dialogs.Confirm("Wczytaj strukturę z Excela", message))
            {
                Status = "Wczytanie struktury anulowane – bez zmian.";
                return;
            }
            var mapping = _mapping;
            var results = await Busy.Run($"Zapisywanie zmian z pliku ({file.Edits.Count} wierszy)…", () => _service.SaveStructureEdits(Code, mapping, file.Edits));
            var failures = results.Where(r => !r.Saved).Select(r => $"{r.Id}: {r.Message}").ToList();
            _production = null;
            await Reload(refreshMapping: false);
            Status = failures.Count == 0
                ? $"Wczytano strukturę z {Path.GetFileName(path)}: zapisano {results.Count} wierszy{(problems.Count > 0 ? $", pominięto {problems.Count}" : "")}."
                : $"Zapisano {results.Count - failures.Count} z {results.Count} wierszy; nie zapisano: {string.Join(" · ", failures.Take(3))}{(failures.Count > 3 ? " …" : "")}";
        });
    }

    /// <summary>Dołożenie elementów z kolejnego eksportu SAP (np. nowe Project definition) – istniejąca nakładka bez zmian.</summary>
    private async Task DoAddFromExcel()
    {
        var path = _dialogs.OpenExcel($"Dołóż elementy do programu {Code} – eksport struktury WBS z SAP");
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
            {
                _production = null;
                await Reload(refreshMapping: false);
            }
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
        Table.CommitEdits();
        if (Table.HasPendingChanges)
        {
            Status = "Masz niezapisane zmiany słownika – zapisz albo odrzuć je przed wczytaniem z Excela.";
            return;
        }
        var path = _dialogs.OpenExcel($"Wczytaj słownik „{panel.Name}” programu {Code}");
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

    private void DoRemoveDictionaryRow()
    {
        var (removed, _) = Table.RemoveSelected();
        Status = removed == 0 ? ""
            : _selectedDictionary?.Item.Code == PzlEv.Shared.Utils.Dictionaries.GlobalDictionaries.CostCategory
                ? "Zmiany programu usunięte z tabeli – po zapisie obowiązują pozycje ze słownika globalnego."
                : "Wiersze usunięte z tabeli – zapisz, aby zamknąć ich obowiązywanie (historia zostaje).";
    }

    /// <summary>Zapis tabeli wybranego słownika z regułami projektu; po zapisie – odświeżenie struktury i liczników.</summary>
    private async Task DoSaveDictionary(bool confirmWarnings)
    {
        var panel = _selectedDictionary!;
        var context = Context(panel.Item.Code);
        Table.CommitEdits();
        var (working, removed) = Table.State();
        await Try(async () =>
        {
            var outcome = await Busy.Run($"Zapisywanie słownika „{panel.Name}”…",
                () => _service.SaveDictionary(panel.Item.Code, context(), working, removed, Code, confirmWarnings));
            switch (outcome.Status)
            {
                case SaveStatus.Saved:
                case SaveStatus.NoChanges:
                    await Reload(refreshMapping: false, discard: true);
                    Table.SetIssues(outcome.Issues);
                    break;
                case SaveStatus.NeedsConfirmation:
                    Table.SetIssues(outcome.Issues, needsConfirmation: true);
                    break;
                default:
                    Table.SetIssues(outcome.Issues);
                    break;
            }
            Status = $"{panel.Name}: {outcome.Message}";
        });
    }

    private async Task DoExportDictionaries()
    {
        var path = _dialogs.SaveExcel("Pobierz słowniki programu", $"{Code}_slowniki_programu.xlsx");
        if (path is null)
            return;
        var (tree, mapping) = (_saved, _mapping);
        await Try(async () =>
        {
            await Busy.Run("Zapisywanie słowników programu do Excela…", () => _service.ExportDictionaries(path, Code, Project.Type, tree, mapping));
            Status = $"Zapisano {path} – popraw w Excelu i wczytaj ponownie (przed zapisem zobaczysz różnice).";
        });
    }

    private async Task DoCreateFolders()
    {
        await Try(async () =>
        {
            var created = await Busy.Run("Tworzenie folderów programu…", () => _service.Folders.Create(Code));
            _folderChecks = null;   // ponowna kontrola folderów
            await Reload(refreshMapping: false);
            Status = created.Count == 0 ? "Wszystkie foldery programu istnieją." : $"Utworzono foldery: {string.Join(", ", created)}.";
        });
    }

    /// <summary>
    /// Import plików definicji źródeł projektu (słownik „Źródła danych”): logowanie do SharePoint, import wspólny
    /// z innymi projektami (plik zapisuje się raz), potem ponowny odczyt struktury P1S i kosztów.
    /// </summary>
    private async Task DoImportSources()
    {
        Structure.CommitEdits();
        var import = _service.Imports!;
        IReadOnlySet<string> sources = new HashSet<string>();
        await Try(async () => sources = await Busy.Run("Odczyt źródeł programu…", () => _service.ProjectSources(Code)));
        if (sources.Count == 0)
        {
            Status = "Program nie ma źródeł danych – dodaj definicje źródeł w słowniku programu „Źródła danych”.";
            return;
        }
        if (!await import.SignIn())
        {
            Status = "Import nie rozpoczęty – logowanie do SharePoint anulowane.";
            return;
        }
        SourceImportResult? result = null;
        await Try(async () => result = await Busy.Run($"Import źródeł programu ({string.Join(", ", sources.Order())})…",
            () => import.Import(sources, null, CancellationToken.None)));
        if (result is null)
            return;
        await Reload(refreshMapping: true);
        var errors = result.Issues.Where(i => i.Level == CheckLevel.Error).Select(i => i.Message).Take(3).ToList();
        Status = $"Import źródeł programu #{result.BatchId}: {result.Summary}" + (errors.Count > 0 ? $" – {string.Join("; ", errors)}" : "");
    }

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (FileInUse.Is(ex))
        {
            Logger.Warning("{Context}: plik otwarty w innym programie – {Message}", "Program " + Code, ex.Message);
            Status = FileInUse.Describe(ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Program {Code}", Code);
            Status = $"Nie udało się: {ex.Message}";
        }
    }
}
