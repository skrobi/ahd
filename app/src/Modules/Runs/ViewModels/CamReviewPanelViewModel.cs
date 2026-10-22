using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>Wiersz estymacji WP w panelu etapu: status (Tone – kolor), wymagana, wartości, uzasadnienie CAM, kto i kiedy wysłał i ocenił.</summary>
public sealed record CamReviewRow(CamReviewItem Item, string Tone, string Required, string EtcHours, string Material, string Finish, string Submitted, string Reviewed)
{
    public string Status => Item.Status;

    public string Wp => Item.Wp;

    public string Name => Item.Name;

    public string Cam => Item.Cam;

    public string Note => Item.Status == CamStatuses.Returned ? $"uwaga: {Item.ReviewNote}" : Item.Justification ?? "";
}

/// <summary>
/// Etap przebiegu „Estymacje CAM” (docs/pipeline-fazy.md, rozdz. 4.1): estymacje programu w cyklu okresu tygodnia przebiegu – liczniki
/// wymaganych WP wobec zatwierdzonych i lista WP; finansista zatwierdza albo zwraca wysłane estymacje (uwaga) i otwiera ponownie zatwierdzone
/// (komentarz) – kontrakt ICamReview (moduł CAM). Akceptacja etapu – „Zaakceptuj estymacje i dalej” panelu przebiegu.
/// </summary>
public sealed class CamReviewPanelViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly ICamReview _review;
    private readonly string _project;
    private readonly string _period;
    private readonly bool _editable;
    private CamReview? _state;
    private CamReviewRow? _selected;
    private string _note = "";
    private string _message = "";
    private bool _onlyRequired = true;

    /// <param name="editable">Decyzje tylko w ostatnim uruchomieniu, gdy etap czeka na akceptację.</param>
    public CamReviewPanelViewModel(ICamReview review, string project, string period, bool editable)
    {
        (_review, _project, _period, _editable) = (review, project, period, editable);
        Approve = new AsyncRelayCommand(() => Decide(r => _review.Approve(r.Item), "Zatwierdzanie – zapis w „Harmonogram i budżet”…"),
            () => _editable && !Busy.IsBusy && _selected?.Status == CamStatuses.Sent);
        Return = new AsyncRelayCommand(() => Decide(r => _review.Return(r.Item, _note), "Zwrot do CAM-a…"),
            () => _editable && !Busy.IsBusy && _selected?.Status == CamStatuses.Sent);
        Reopen = new AsyncRelayCommand(() => Decide(r => _review.Reopen(r.Item, _note), "Ponowne otwarcie estymacji…"),
            () => _editable && !Busy.IsBusy && _selected?.Status == CamStatuses.Approved);
        Refresh = new AsyncRelayCommand(() => Load(), () => !Busy.IsBusy);
        _ = Load();
    }

    public BusyState Busy { get; } = new();

    public ICommand Approve { get; }

    public ICommand Return { get; }

    public ICommand Reopen { get; }

    public ICommand Refresh { get; }

    public bool Editable => _editable;

    public bool ReadOnly => !_editable;

    public ObservableCollection<CamReviewRow> Rows { get; } = [];

    public CamReviewRow? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    /// <summary>Uwaga przy zwrocie albo powód ponownego otwarcia (min. 10 znaków).</summary>
    public string Note { get => _note; set => SetProperty(ref _note, value); }

    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public bool OnlyRequired
    {
        get => _onlyRequired;
        set { if (SetProperty(ref _onlyRequired, value)) Show(); }
    }

    public string Summary => _state?.Summary ?? "";

    /// <summary>Liczniki wymaganych WP: wymagane, zatwierdzone, wysłane (czekają na decyzję), u CAM-a (szkic, zwrócone), brak.</summary>
    public string RequiredText => $"{_state?.Required ?? 0}";

    public string ApprovedText => $"{_state?.Approved ?? 0}";

    public string SentText => $"{_state?.Sent ?? 0}";

    public string AtCamText => $"{_state?.AtCam ?? 0}";

    public string MissingText => $"{_state?.Missing ?? 0}";

    public bool Complete => _state is { } s && s.Approved == s.Required;

    private async Task Load(string? message = null)
    {
        try
        {
            _state = await Busy.Run("Estymacje CAM programu…", () => _review.Program(_project, _review.CycleOf(_period)));
            Show();
            Message = message ?? (_state.Overdue(DateOnly.FromDateTime(DateTime.Today)) && _state.Approved < _state.Required
                ? $"Termin cyklu {_state.Cycle.Deadline:yyyy-MM-dd} minął – brakujące estymacje można zaakceptować z komentarzem (ETC tych WP jak dotąd)." : "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Estymacje CAM programu {Project}", _project);
            Message = $"Błąd: {ex.Message}";
        }
        RaiseCounters();
    }

    private void RaiseCounters()
    {
        foreach (var name in new[] { nameof(Summary), nameof(RequiredText), nameof(ApprovedText), nameof(SentText), nameof(AtCamText), nameof(MissingText), nameof(Complete) })
            OnPropertyChanged(name);
    }

    private void Show()
    {
        static string N(decimal v) => PolishNumber.ToDisplay(Math.Round(v, 0), 0);
        Rows.Clear();
        foreach (var i in (_state?.Items ?? []).Where(i => !_onlyRequired || i.Required)
                     .OrderBy(i => i.Status switch { CamStatuses.Sent => 0, CamStatuses.Returned => 1, CamStatuses.ToDo => 2, CamStatuses.Draft => 3, _ => 4 })
                     .ThenBy(i => i.Wp, StringComparer.OrdinalIgnoreCase))
        {
            var tone = i.Status switch
            {
                CamStatuses.Approved => "ok",
                CamStatuses.Sent => "info",
                CamStatuses.Returned => "crit",
                _ => "warn",
            };
            var todo = i.Status == CamStatuses.ToDo;
            Rows.Add(new CamReviewRow(i, tone, i.Required ? "tak" : "nie", todo ? "—" : N(i.EtcHours) + " h", todo ? "—" : $"{N(i.Material)} {i.Currency}",
                i.ForecastFinish?.ToString("yyyy-MM-dd") ?? "—",
                i.SubmittedAt is { } s ? $"{s.LocalDateTime:yyyy-MM-dd HH:mm} · {i.SubmittedBy}" : "",
                i.ReviewedAt is { } r && i.Status is CamStatuses.Approved or CamStatuses.Returned ? $"{r.LocalDateTime:yyyy-MM-dd HH:mm} · {i.ReviewedBy}" : ""));
        }
    }

    private async Task Decide(Func<CamReviewRow, CamDecision> decision, string label)
    {
        if (_selected is not { } row)
            return;
        try
        {
            var outcome = await Busy.Run(label, () => decision(row));
            if (!outcome.Ok)
            {
                Message = outcome.Message;
                return;
            }
            Note = "";
            if (outcome.Item is { } changed && _state is { } state)
            {
                // Pozycja po decyzji w miejsce poprzedniej – bez ponownego odczytu programu (struktura z PZLPROD).
                _state = state with { Items = [.. state.Items.Select(i => i.Project == changed.Project && i.Wp == changed.Wp ? changed : i)] };
                Show();
                Message = outcome.Message;
                RaiseCounters();
            }
            else
                await Load(outcome.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Decyzja o estymacji WP {Wp}", row.Wp);
            Message = $"Błąd: {ex.Message}";
        }
    }
}
