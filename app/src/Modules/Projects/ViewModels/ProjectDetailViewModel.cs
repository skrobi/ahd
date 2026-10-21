using System.Windows.Input;
using PzlEv.Shared.Utils.Files;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Ekran Projekt (docs/funkcjonalnosc.md, rozdz. 2) w zakładkach: Wskaźniki (status danych, sumy projektu, miejsce na wskaźniki
/// EV), Struktura (nakładka Performance Objectives z rozwinięciem P1S, WP, CAM, budżetem
/// i datami – edycja w komórkach zapisywana od razu, dokładanie elementów z kolejnego eksportu SAP), Słowniki projektu
/// (wybrany słownik w tabeli z edycją w komórkach jak na ekranie Słowniki; pobierz / wczytaj z Excela z podglądem
/// różnic), Konfiguracja, Raporty, Przebieg, Foldery. Ten plik – stan, konstruktor, odczyt programu (Reload) i komunikaty;
/// zakładki w plikach częściowych: .Indicators (Wskaźniki, koszty ACTUALS), .Structure (Struktura i Performance Objectives),
/// .Dictionaries (Słowniki programu, Konfiguracja, import źródeł), .Reports (Raporty), .Folders (Foldery).
/// </summary>
public sealed partial class ProjectDetailViewModel : ObservableObject, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly ProjectService _service;
    private readonly IFileDialogs _dialogs;
    private string _status = "";
    private readonly IRunPanel? _runPanel;
    private P1sInputs _mapping = P1sInputs.None;
    private PoTree _saved = new();
    private IReadOnlyDictionary<string, string> _owners = new Dictionary<string, string>();
    // Dane zmieniające się rzadko – czytane przy otwarciu projektu i „Odśwież P1S i koszty” (nie po każdym zapisie):
    // osoby (słownik Osoby), elementy CES nakładek innych projektów, kontrola folderów na dysku sieciowym.
    private IReadOnlyList<LookupOption>? _persons;
    private bool _ownersLoaded;
    private ActualsFreshness? _actuals;   // z kiedy są dane ACTUALS – czytane razem z kosztami
    private ProductionData? _production;  // PZLPROD na żywo (vAHDD, vAPD) – przy otwarciu, „Odśwież” i po zmianie Legacy WBS
    private bool _isEditingObjectives;
    private DictionaryPanelViewModel? _selectedDictionary;
    private readonly Queue<StructureRowViewModel> _commits = new();
    private bool _committing;
    private int _reloads;
    /// <summary>ACWP po elemencie CES (ostatni import ACTUALS) – czytany przy otwarciu i „Odśwież P1S i koszty”, nie po każdym zapisie.</summary>
    private ElementActuals? _costs;   // ACWP, Actual DLH, ACWP Labor / Material – jeden odczyt ACTUALS
    private string? _currency;          // waluta projektu z kolumny waluty zestawu walutowego (dane ACTUALS projektu)
    private string? _currencyProblem;   // kilka walut w kolumnie waluty zestawu
    private int _loading;   // trwające odczyty programu (Reload) – zmiana słownika dozwolona, starszy odczyt pomijany
    private ApprovedActuals? _approved;  // przebieg z zatwierdzonymi danymi ACTUALS – stan danych kosztów struktury (null – ostatni import)
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
        Structure = new StructureViewModel { Commit = CommitRow, CanEditNow = () => !IsEditingObjectives, OpenAcwpChanges = ShowAcwpChanges };
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
        _runPanel = _service.RunPanels?.Create(Code);
        if (_service.Runs is { } runs)
            runs.Finished += OnRunFinished;
        foreach (var item in ProjectDictionaries.Items)
        {
            var panel = new DictionaryPanelViewModel(item);
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

    public string Subtitle => $"{ProjectTypes.Label(Project.Type)} · utworzony {Project.RecordedAt:yyyy-MM-dd HH:mm} przez {Project.RecordedBy}";

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

    /// <summary>Przebieg programu – zakładka Przebieg (panel modułu Przebiegi: etapy, potwierdzenia, ponowne wykonanie); null – moduł niedostępny.</summary>
    public object? RunPanel => _runPanel?.View;

    /// <summary>Zwolnienie panelu przebiegu i odpięcie od zdarzeń przebiegów przy zmianie strony modułu.</summary>
    public void Dispose()
    {
        _runPanel?.Dispose();
        if (_service.Runs is { } runs)
            runs.Finished -= OnRunFinished;
    }

    // Przebieg programu zakończony (np. zatwierdzone dane ACTUALS) – struktura na danych zatwierdzonego przebiegu (ReadCosts).
    private void OnRunFinished(string project)
    {
        if (string.Equals(project, Code, StringComparison.OrdinalIgnoreCase))
            _ = Reload(refreshMapping: false);
    }

    public ICommand Back { get; }
    public ICommand RefreshMapping { get; }

    /// <summary>Stan projektu z bazy w jednym odczycie w tle: nakładka, struktura P1S, słowniki, struktura.</summary>
    private sealed record Snapshot(PoTree Tree, P1sInputs Mapping, IReadOnlyDictionary<string, string> Owners,
        IReadOnlyDictionary<string, (int Rows, string LastChange)> Dictionaries, ProjectStructure Structure, IReadOnlyList<LookupOption> Persons,
        IReadOnlyList<LookupOption> Categories,
        IReadOnlyList<(DictRow Row, bool Inherited)>? Table, IReadOnlyDictionary<string, IReadOnlyList<LookupOption>> Lookups,
        ElementActuals Costs, string? CostsError, string CostsField, string? Currency, string? CurrencyProblem, ApprovedActuals? Approved, IReadOnlyList<LookupOption> Mpks, IReadOnlyList<LookupOption> PersonLookups,
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
        var cachedOwners = refreshMapping || !_ownersLoaded ? null : _owners;
        var cachedActuals = refreshMapping ? null : _actuals;
        var cachedProduction = refreshMapping ? null : _production;
        CostData? cachedCosts = _costs is null ? null : new CostData(_costs, _costsError, _costsField, _currency, _currencyProblem, _approved);
        _loading++;
        try
        {
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
                    var (costs, costsError, costsField, currency, currencyProblem, approved) = costsRead.GetAwaiter().GetResult();
                    // Stawki MPK i kursy – ETC Cost struktury (waluta projektu z pola kwoty ACTUALS); lista MPK w „WP i CAM”.
                    var departmentRates = steps.Time("stawki wydziałów i kursy walut", _service.DepartmentRates);
                    var rates = ProjectService.EtcRatesOf(departmentRates, _service.FxRates(), currency, currencyProblem, DateOnly.FromDateTime(DateTime.Today));
                    var mpks = GlobalDictionaries.LookupOptions(GlobalDictionaries.DepartmentRates, departmentRates);
                    var structure = steps.Time("budowa struktury", () =>
                        ProjectService.Structure(tree, mapping, wpCam, rows.GetValueOrDefault(ProjectDictionaries.ScheduleBudget) ?? [], costs.Amounts, production,
                            costs.Hours, costs.Labor, costs.Material, rates));
                    var persons = cachedPersons ?? steps.Time("osoby", _service.PersonLookups);
                    var categories = ProjectService.CategoryLookups(rows.GetValueOrDefault(ProjectDictionaries.WbsCategories) ?? []);
                    var owners = cachedOwners ?? steps.Time("nakładki innych programów", () => _service.WbsOwners(Code));
                    var editable = table is null ? null : steps.Time("tabela słownika", () =>
                        _service.EditableRows(table, rows.TryGetValue(table, out var tableRows) ? tableRows : _service.Rows(table, Code)));
                    var definitions = steps.Time("definicje źródeł", _service.SourceDefinitions);
                    var sources = ProjectService.SourceOptions(definitions);
                    var burdenGroups = steps.Time("grupy narzutowe CAS", _service.BurdenGroupLookups);
                    var runTemplates = steps.Time("szablony przebiegów", _service.RunTemplates);
                    var projectSources = (rows.GetValueOrDefault(ProjectDictionaries.Sources) ?? []).Select(r => r["Definicja źródła"]).OfType<string>()
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var actuals = cachedActuals ?? steps.Time("data danych ACTUALS", ReadActuals);
                    return new Snapshot(tree, mapping, owners,
                        states.ToDictionary(s => s.Key, s => (s.Value.Rows.Count, s.Value.LastChange)), structure, ProjectService.PersonOptions(persons, wpCam),
                        ProjectService.CategoryOptions(categories, wpCam), editable,
                        ProjectService.Lookups(persons, categories, sources, burdenGroups, mpks, runTemplates), costs, costsError, costsField, currency, currencyProblem, approved, mpks, persons, actuals, production,
                        definitions.Where(d => projectSources.Contains(d.Code)).ToList());
                });
                Logger.Information("Program {Code}: wczytanie {Steps}", Code, steps.Summary);
                if (version != _reloads)
                    return;   // w międzyczasie ruszyło nowsze odświeżenie – starszy wynik pomijany
                _saved = snapshot.Tree;
                (_costs, _costsError, _costsField, _currency, _currencyProblem, _approved) =
                    (snapshot.Costs, snapshot.CostsError, snapshot.CostsField, snapshot.Currency, snapshot.CurrencyProblem, snapshot.Approved);
                OnPropertyChanged(nameof(StructureDataInfo));
                OnPropertyChanged(nameof(StructureDataDetails));
                OnPropertyChanged(nameof(StructureDataUnapproved));
                OnPropertyChanged(nameof(CurrencyInfo));
                OnPropertyChanged(nameof(CurrencyDetails));
                _mapping = snapshot.Mapping;
                _owners = snapshot.Owners;
                _ownersLoaded = true;
                _persons = snapshot.PersonLookups;
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
                if (refreshMapping)
                    Status = _costsError is null ? "Odświeżono strukturę P1S i koszty (ostatni import ACTUALS)." : $"Odświeżono strukturę P1S; {_costsError}";
            });
        }
        finally
        {
            _loading--;
        }
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
