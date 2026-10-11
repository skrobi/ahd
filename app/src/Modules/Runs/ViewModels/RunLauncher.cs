using System.Collections.Concurrent;
using System.Windows;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Notifications;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>
/// Kontrakt IRunLauncher: przebieg projektu w tle aplikacji (osobny wątek), postęp i zakończenie na wątku UI,
/// po zakończeniu powiadomienie Windows i e-mail przez Outlook (adres ze słownika Osoby albo konto Outlooka).
/// </summary>
public sealed class RunLauncher(RunService service, IRunStore store, AppServices services, Func<ISourceImport?> imports) : IRunLauncher
{
    private readonly ConcurrentDictionary<string, bool> _running = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, string>? Progress;

    public event Action<string>? Finished;

    public bool IsRunning(string project) => _running.ContainsKey(project);

    public IReadOnlyDictionary<string, RunPinRow> Latest() => store.Latest();

    public IReadOnlyList<RunPinRow> Pins(string project, int count) => store.Pins(project, count);

    public IReadOnlyList<RunIssueRow> Issues(long pinId) => store.Issues(pinId);

    public async Task<string?> Start(string project)
    {
        if (!_running.TryAdd(project, true))
            return "Przebieg projektu już trwa.";
        if (service.Running(project) is { } holder)
        {
            _running.TryRemove(project, out _);
            return $"Przebieg projektu trwa: {holder.Text}.";
        }
        if (imports() is { } import && store.ProjectSources(project).Count > 0 && !await import.SignIn())
        {
            _running.TryRemove(project, out _);
            return "Przebieg nie rozpoczęty – logowanie do SharePoint anulowane.";
        }
        var dispatcher = Application.Current?.Dispatcher;
        var progress = new Progress<string>(text => Progress?.Invoke(project, text));
        _ = Task.Run(() =>
        {
            RunOutcome outcome;
            try
            {
                outcome = service.Execute(project, progress);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                outcome = new RunOutcome(project, null, RunStatuses.Failed, 1, 0, ex.Message);
            }
            _running.TryRemove(project, out _);
            Notify(outcome);
            dispatcher?.BeginInvoke(() =>
            {
                WindowsNotification.Show($"PZL-EV – przebieg {outcome.Code ?? project}", outcome.Summary,
                    warning: outcome.Warnings > 0, error: outcome.Errors > 0);
                Finished?.Invoke(project);
            });
        });
        Progress?.Invoke(project, "Przebieg uruchomiony…");
        return null;
    }

    /// <summary>E-mail o zakończeniu (w tle – Outlook potrafi chwilę odpowiadać).</summary>
    private void Notify(RunOutcome outcome)
    {
        var usrid = services.User.Account.Split('\\').Last();
        var issues = outcome.Code is null ? [] : store.Pins(outcome.Project, 1).FirstOrDefault() is { } pin ? store.Issues(pin.PinId) : [];
        var body = $"Przebieg {outcome.Code ?? outcome.Project}: {outcome.Summary}\r\n\r\n" +
                   string.Join("\r\n", issues.Where(i => i.Level != Shared.Models.Pipeline.CheckLevel.Pass)
                       .Select(i => $"[{(i.Level == Shared.Models.Pipeline.CheckLevel.Error ? "BŁĄD" : "UWAGA")}] {i.CheckName}: {i.Message}").Take(30)) +
                   "\r\n\r\nSzczegóły: PZL-EV → Projekty → " + outcome.Project + " → Przebiegi.";
        OutlookMail.Send(store.Email(usrid), $"PZL-EV: przebieg {outcome.Code ?? outcome.Project} – {outcome.Status}", body);
    }
}
