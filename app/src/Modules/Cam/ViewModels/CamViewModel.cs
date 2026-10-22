using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Cam.Models;
using PzlEv.Modules.Cam.Services;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Cam.ViewModels;

/// <summary>Wiersz listy zadań cyklu: status (Tone – kolor pigułki), WP, czego brakuje, stan, ETC, akcja.</summary>
public sealed record CamWorkRow(CamWorkItem Item, string Status, string Tone, string Name, string Code, string Missing, string State, string EtcHours,
    string EtcMaterial, string Action, bool ToDo);

/// <summary>Estymacja czekająca na analityka (zakładka Zatwierdzanie).</summary>
public sealed record CamPendingRow(CamEstimate Estimate, string Program, string Wp, string Cam, string EtcHours, string EtcMaterial, string Finish,
    string Justification, string Submitted);

/// <summary>
/// Ekran modułu CAM (docs/funkcjonalnosc.md; tasks/F11.3–F11.5): Estymacje – lista zadań cyklu i formularz WP (wprowadzanie dla
/// Finansów); Zatwierdzanie – wysłane estymacje wszystkich CAM-ów; Wyniki EV – przegląd tylko do odczytu. Test – bez uprawnień
/// (wybór CAM-a z listy).
/// </summary>
public sealed class CamViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "cam");

    private readonly CamService _service;
    private IReadOnlyList<CamPerson> _cams = [];
    private CamPerson? _selectedCam;
    private IReadOnlyList<CamWorkPackage> _wps = [];
    private CamCycle? _cycle;
    private bool _onlyToDo = true;
    private EstimateEditorViewModel? _editor;
    private CamPendingRow? _selectedPending;
    private string _reviewNote = "";
    private string _status = "";
    private int _selectedTab;

    public CamViewModel(CamService service)
    {
        _service = service;
        Refresh = new AsyncRelayCommand(() => LoadCam(), () => !Busy.IsBusy && _selectedCam is not null);
        Open = new RelayCommand(p => _ = OpenRow(p as CamWorkRow));
        Back = new RelayCommand(_ => { Editor = null; _ = LoadCam(); });
        Approve = new AsyncRelayCommand(() => Review(approve: true), () => _selectedPending is not null && !Busy.IsBusy);
        Return = new AsyncRelayCommand(() => Review(approve: false), () => _selectedPending is not null && !Busy.IsBusy);
        _ = Load();
    }

    public BusyState Busy { get; } = new();

    public ICommand Refresh { get; }

    public ICommand Open { get; }

    public ICommand Back { get; }

    public ICommand Approve { get; }

    public ICommand Return { get; }

    public IReadOnlyList<CamPerson> Cams { get => _cams; private set => SetProperty(ref _cams, value); }

    public CamPerson? SelectedCam
    {
        get => _selectedCam;
        set
        {
            if (SetProperty(ref _selectedCam, value) && value is not null)
            {
                Editor = null;
                _ = LoadCam();
            }
        }
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value) && value == 1)
                _ = LoadPending();
        }
    }

    public string CycleText => _cycle is null ? ""
        : $"Cykl: zamknięcie okresu {_cycle.Period}" + (_cycle.Deadline is { } d ? $" · termin {d:yyyy-MM-dd}" : " · termin – brak tygodnia zamykającego w kalendarzu");

    public string ProgressText { get; private set; } = "";

    public ObservableCollection<CamWorkRow> Rows { get; } = [];

    private List<CamWorkRow> _allRows = [];

    public bool OnlyToDo
    {
        get => _onlyToDo;
        set { if (SetProperty(ref _onlyToDo, value)) ShowRows(); }
    }

    public EstimateEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(IsListing));
            }
        }
    }

    public bool IsEditing => _editor is not null;

    public bool IsListing => _editor is null;

    public ObservableCollection<CamPendingRow> Pending { get; } = [];

    public CamPendingRow? SelectedPending { get => _selectedPending; set => SetProperty(ref _selectedPending, value); }

    public string ReviewNote { get => _reviewNote; set => SetProperty(ref _reviewNote, value); }

    public CamResultsViewModel Results { get; } = new();

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private async Task Load()
    {
        if (!_service.Available)
        {
            Status = "Moduł Projekty niedostępny – brak danych WP.";
            return;
        }
        await Try(async () =>
        {
            var (cams, cycle) = await Busy.Run("Wczytywanie CAM-ów…", () => (_service.Cams(), _service.Cycle()));
            _cycle = cycle;
            OnPropertyChanged(nameof(CycleText));
            Cams = cams;
            if (cams.Count == 0)
                Status = "Brak CAM-ów – przypisz CAM do WP w strukturze programu („WP i CAM”).";
            SelectedCam = cams.FirstOrDefault();
        });
    }

    private async Task LoadCam()
    {
        if (_selectedCam is not { } cam)
            return;
        await Try(async () =>
        {
            var (wps, cycle) = await Busy.Run($"Wczytywanie WP – {cam.Label} (struktury programów)…", () => (_service.WorkPackages(cam.Code), _service.Cycle()));
            _wps = wps;
            _cycle = cycle;
            OnPropertyChanged(nameof(CycleText));
            var items = await Busy.Run("Estymacje cyklu…", () => _service.Worklist(wps, cycle));
            _allRows = items.Select(ToRow).ToList();
            var ready = items.Count(i => i.Estimate.Status is CamStatuses.Sent or CamStatuses.Approved);
            ProgressText = $"{ready} z {items.Count} WP gotowe (wysłane albo zatwierdzone) · " +
                           string.Join(" · ", items.GroupBy(i => i.Estimate.Status).OrderBy(g => CamStatuses.Order(g.Key)).Select(g => $"{g.Key.ToLowerInvariant()}: {g.Count()}"));
            OnPropertyChanged(nameof(ProgressText));
            ShowRows();
            Results.Load(wps);
            if (wps.Count == 0)
                Status = $"{cam.Label} nie ma WP w programach.";
            else if (wps.Select(w => w.DataNote).OfType<string>().FirstOrDefault() is { } note)
                Status = $"Dane niepełne: {note}";
            else
                Status = "";
        });
    }

    private void ShowRows()
    {
        Rows.Clear();
        foreach (var row in _allRows.Where(r => !_onlyToDo || r.ToDo))
            Rows.Add(row);
    }

    private static CamWorkRow ToRow(CamWorkItem item)
    {
        var e = item.Estimate;
        var tone = e.Status switch
        {
            CamStatuses.Returned => "crit",
            CamStatuses.ToDo => "warn",
            CamStatuses.Draft => "stale",
            CamStatuses.Sent => "info",
            _ => "ok",
        };
        var state = e.Status switch
        {
            CamStatuses.Returned => $"Zwrócona {e.ReviewedAt?.LocalDateTime:yyyy-MM-dd} ({e.ReviewedBy}): {e.ReviewNote}",
            CamStatuses.ToDo => "Brak estymacji w tym cyklu – propozycja systemu do potwierdzenia albo zmiany.",
            CamStatuses.Draft => $"Szkic zapisany {e.UpdatedAt?.LocalDateTime:yyyy-MM-dd HH:mm} – niewysłany.",
            CamStatuses.Sent => $"Wysłana {e.SubmittedAt?.LocalDateTime:yyyy-MM-dd} – czeka na analityka.",
            _ => $"Zatwierdzona {e.ReviewedAt?.LocalDateTime:yyyy-MM-dd} ({e.ReviewedBy}) – w EV.",
        };
        var missing = string.Join(" · ", item.Checklist.Select(c => c.Done ? $"{c.Item} ✓" : c.Item));
        var hasMaterial = item.Checklist.Any(c => c.Item == "Materiały");
        return new CamWorkRow(item, e.Status, tone, item.Wp.Name, $"{item.Wp.Wp} · {item.Wp.Project} · {(item.Wp.Production ? "produkcyjny (AHD)" : "bez harmonogramu")}",
            hasMaterial ? missing : missing + " · materiały – brak w WP", state,
            PolishNumber.ToDisplay(Math.Round(e.Total(CamLineKinds.Hours), 0), 0) + " h",
            hasMaterial ? $"{PolishNumber.ToDisplay(Math.Round(e.Total(CamLineKinds.Material), 0), 0)} {item.Wp.Currency}" : "—",
            e.Status switch { CamStatuses.Returned => "Popraw", CamStatuses.ToDo => "Uzupełnij", CamStatuses.Draft => "Kontynuuj", _ => "Podgląd" },
            CamStatuses.Editable(e.Status));
    }

    private async Task OpenRow(CamWorkRow? row)
    {
        if (row is null)
            return;
        await Try(async () =>
        {
            var (history, last) = await Busy.Run("Historia estymacji…", () => (_service.History(row.Item.Estimate), _service.LastApproved(row.Item.Estimate)));
            Editor = new EstimateEditorViewModel(row.Item.Wp, row.Item.Estimate, history, last, Save);
        });
    }

    private async Task Save(EstimateEditorViewModel editor, bool submit)
    {
        await Try(async () =>
        {
            var outcome = await Busy.Run(submit ? "Wysyłanie estymacji…" : "Zapis szkicu…", () => _service.Save(editor.Wp, editor.Current(), submit));
            editor.Message = outcome.Message;
            if (outcome.Ok && outcome.Estimate is { } saved)
                editor.Saved(saved);
        });
    }

    private async Task LoadPending()
    {
        await Try(async () =>
        {
            var cycle = _cycle ?? await Busy.Run("Cykl…", _service.Cycle);
            var pending = await Busy.Run("Estymacje do zatwierdzenia…", () => _service.Pending(cycle));
            Pending.Clear();
            foreach (var e in pending)
                Pending.Add(new CamPendingRow(e, e.Project, e.Wp, e.Cam, PolishNumber.ToDisplay(Math.Round(e.Total(CamLineKinds.Hours), 0), 0) + " h",
                    $"{PolishNumber.ToDisplay(Math.Round(e.Total(CamLineKinds.Material), 0), 0)} {e.Currency}", e.ForecastFinish?.ToString("yyyy-MM-dd") ?? "—",
                    e.Justification ?? "", $"{e.SubmittedAt?.LocalDateTime:yyyy-MM-dd HH:mm} · {e.SubmittedBy}"));
            Status = pending.Count == 0 ? "Brak estymacji do zatwierdzenia w tym cyklu." : "";
        });
    }

    private async Task Review(bool approve)
    {
        if (_selectedPending is not { } row)
            return;
        await Try(async () =>
        {
            var outcome = await Busy.Run(approve ? "Zatwierdzanie – zapis w „Harmonogram i budżet”…" : "Zwrot do CAM-a…",
                () => approve ? _service.Approve(row.Estimate) : _service.Return(row.Estimate, _reviewNote));
            Status = outcome.Message;
            if (outcome.Ok)
            {
                ReviewNote = "";
                await LoadPending();
            }
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
            Logger.Error(ex, "Moduł CAM");
            Status = $"Błąd: {ex.Message}";
        }
    }
}
