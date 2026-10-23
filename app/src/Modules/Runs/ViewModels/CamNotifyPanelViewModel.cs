using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>Wysłane powiadomienie w panelu etapu: kiedy, kto, CAM, adres, wynik, WP.</summary>
public sealed record CamNotificationRow(string When, string Cam, string Email, string Result, string Tone, string Wps, string Message);

/// <summary>
/// Etap przebiegu „Powiadomienie CAM” (docs/pipeline-fazy.md, rozdz. 4.1): powiadomienia programu w cyklu (kto, kiedy, wynik) i „Wyślij ponownie”
/// – e-mail do wszystkich CAM-ów z zaległymi estymacjami, także powiadomionych wcześniej w cyklu (ICamReview.Notify, resend).
/// </summary>
public sealed class CamNotifyPanelViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly ICamReview _review;
    private readonly string _project;
    private readonly string _cycle;
    private readonly long _pinId;
    private string _message = "";

    public CamNotifyPanelViewModel(ICamReview review, string project, string cycle, long pinId, bool canResend)
    {
        (_review, _project, _cycle, _pinId) = (review, project, cycle, pinId);
        CanResend = canResend;
        Resend = new AsyncRelayCommand(DoResend, () => CanResend && !Busy.IsBusy);
        _ = Load();
    }

    public BusyState Busy { get; } = new();

    public ICommand Resend { get; }

    /// <summary>Ponowna wysyłka – w ostatnim uruchomieniu.</summary>
    public bool CanResend { get; }

    public string Title => $"Powiadomienia CAM – cykl {_cycle}";

    public ObservableCollection<CamNotificationRow> Rows { get; } = [];

    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    private async Task Load(string? message = null)
    {
        try
        {
            var notifications = await Busy.Run("Powiadomienia CAM…", () => _review.Notifications(_project, _cycle));
            Rows.Clear();
            foreach (var n in notifications)
                Rows.Add(new CamNotificationRow($"{n.SentAt.LocalDateTime:yyyy-MM-dd HH:mm} · {n.SentBy}", n.Name, n.Email ?? "—", n.Result,
                    n.Result == CamNotificationResults.Sent ? "ok" : "warn", n.Wps, n.Message ?? ""));
            Message = message ?? (notifications.Count == 0 ? "Brak powiadomień w tym cyklu." : "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Powiadomienia CAM programu {Project}", _project);
            Message = $"Błąd: {ex.Message}";
        }
    }

    private async Task DoResend()
    {
        try
        {
            var result = await Busy.Run("Wysyłanie powiadomień do CAM-ów…", () => _review.Notify(_project, _review.CycleOf(_cycle), _pinId, resend: true));
            await Load(result.Notifications.Count == 0 ? "Brak CAM-ów z zaległymi estymacjami – nic nie wysłano."
                : $"Wysłano ponownie: {result.Notifications.Count(n => n.Result == CamNotificationResults.Sent)} z {result.Notifications.Count}.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Ponowne powiadomienia CAM programu {Project}", _project);
            Message = $"Błąd: {ex.Message}";
        }
    }
}
