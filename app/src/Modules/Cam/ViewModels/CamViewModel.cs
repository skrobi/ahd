using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Cam.Models;
using PzlEv.Modules.Cam.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Cam.ViewModels;

/// <summary>Wiersz listy zadań cyklu: status (Tone – kolor pigułki), WP, czego brakuje, stan, ETC, akcja.</summary>
public sealed record CamWorkRow(CamWorkItem Item, string Status, string Tone, string Name, string Code, string Missing, string State, string EtcHours,
    string EtcMaterial, string Action, bool ToDo);

/// <summary>Wiersz postępu estymacji (program albo CAM): wymagane WP, zatwierdzone, wysłane, u CAM-a, brak, % zatwierdzonych; Tone – kolor %.</summary>
public sealed record CamProgressRow(string Name, int Required, int Approved, int Sent, int AtCam, int Missing, string Percent, string Tone);

/// <summary>
/// Ekran modułu CAM (docs/funkcjonalnosc.md; tasks/F11.3–F11.5): Estymacje – lista zadań cyklu i formularz WP (wprowadzanie dla
/// Finansów); Postęp estymacji – monitoring finansisty: wymagane WP wobec zatwierdzonych po programach i CAM-ach (decyzje – etap przebiegu
/// „Estymacje CAM”); Wyniki EV – przegląd tylko do odczytu. Test – bez uprawnień (wybór CAM-a z listy).
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
    private string _status = "";
    private int _selectedTab;

    public CamViewModel(CamService service)
    {
        _service = service;
        Refresh = new AsyncRelayCommand(() => LoadCam(), () => !Busy.IsBusy && _selectedCam is not null);
        Open = new RelayCommand(p => _ = OpenRow(p as CamWorkRow));
        Back = new RelayCommand(_ => { Editor = null; _ = LoadCam(); });
        RefreshProgress = new AsyncRelayCommand(LoadProgress, () => !Busy.IsBusy);
        _ = Load();
    }

    public BusyState Busy { get; } = new();

    public ICommand Refresh { get; }

    public ICommand Open { get; }

    public ICommand Back { get; }

    public ICommand RefreshProgress { get; }

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
                _ = LoadProgress();
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

    /// <summary>Postęp estymacji cyklu po programach i po CAM-ach (wymagane WP – z CAM-em, niezakończone).</summary>
    public IReadOnlyList<CamProgressRow> ProgressByProgram { get; private set; } = [];

    public IReadOnlyList<CamProgressRow> ProgressByCam { get; private set; } = [];

    public string ProgressSummary { get; private set; } = "";

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
            // W międzyczasie wybrano innego CAM-a – wynik tego odczytu nieaktualny (pokazuje go odczyt nowego CAM-a).
            if (!ReferenceEquals(_selectedCam, cam))
                return;
            _wps = wps;
            _cycle = cycle;
            OnPropertyChanged(nameof(CycleText));
            var items = await Busy.Run("Estymacje cyklu…", () => _service.Worklist(wps, cycle));
            if (!ReferenceEquals(_selectedCam, cam))
                return;
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

    /// <summary>Postęp estymacji cyklu we wszystkich programach (struktura każdego programu z CAM-ami – CamService.Progress).</summary>
    private async Task LoadProgress()
    {
        await Try(async () =>
        {
            var cycle = _cycle ?? await Busy.Run("Cykl…", _service.Cycle);
            var review = await Busy.Run("Postęp estymacji – struktury programów…", () => _service.Progress(cycle));
            static CamProgressRow Row(string name, IReadOnlyList<CamReviewItem> items)
            {
                var review = new CamReview(new CamCycle("", null), items);
                var share = review.Required == 0 ? (decimal?)null : review.Approved * 100m / review.Required;
                return new CamProgressRow(name, review.Required, review.Approved, review.Sent, review.AtCam, review.Missing,
                    share is { } p ? $"{Math.Round(p, 0)}%" : "—", share is null ? "" : share == 100 ? "ok" : share >= 50 ? "warn" : "crit");
            }
            var required = review.Items.Where(i => i.Required).ToList();
            ProgressByProgram = [.. required.GroupBy(i => i.Project, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key).Select(g => Row(g.Key, g.ToList())),
                Row("Razem", required)];
            ProgressByCam = [.. required.GroupBy(i => i.Cam, StringComparer.OrdinalIgnoreCase)
                .Select(g => Row(_cams.FirstOrDefault(c => string.Equals(c.Code, g.Key, StringComparison.OrdinalIgnoreCase))?.Label ?? g.Key, g.ToList()))
                .OrderBy(r => r.Name, StringComparer.CurrentCulture)];
            ProgressSummary = review.Summary.Replace($"Estymacje CAM {cycle.Period}", $"Wszystkie programy, cykl {cycle.Period}") +
                              (review.Overdue(DateOnly.FromDateTime(DateTime.Today)) ? " · termin minął" : "") +
                              ". Decyzje (zatwierdź, zwróć, otwórz ponownie) – etap „Estymacje CAM” w przebiegu programu.";
            OnPropertyChanged(nameof(ProgressByProgram));
            OnPropertyChanged(nameof(ProgressByCam));
            OnPropertyChanged(nameof(ProgressSummary));
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
