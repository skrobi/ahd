using System.Collections.Concurrent;
using System.Windows;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Notifications;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>
/// Kontrakt IRunLauncher: przebieg programu w tle aplikacji (osobny wątek) – nowe uruchomienie, dalsze etapy, potwierdzenie
/// i ponowne wykonanie etapu; postęp i zakończenie na wątku UI, po zakończeniu powiadomienie Windows.
/// </summary>
public sealed class RunLauncher(RunService service, IRunStore store, Func<ISourceImport?> imports) : IRunLauncher
{
    private readonly ConcurrentDictionary<string, bool> _running = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, string>? Progress;

    public event Action<string>? Finished;

    public bool IsRunning(string project) => _running.ContainsKey(project);

    public IReadOnlyDictionary<string, RunPinRow> Latest() => store.Latest();

    public IReadOnlyList<RunPinRow> Pins(string project, int count) => store.Pins(project, count);

    public IReadOnlyList<RunIssueRow> Issues(long pinId) => store.Issues(pinId);

    public Task<string?> Start(string project) =>
        Launch(project, (progress, cancellation) => service.Execute(project, progress, cancellation), "Przebieg uruchomiony…");

    /// <summary>Dalsze etapy uruchomienia do najbliższej bramki.</summary>
    public Task<string?> Continue(string project, long pinId) =>
        Launch(project, (progress, cancellation) => service.Continue(pinId, progress, cancellation), "Przebieg – dalsze etapy…");

    /// <summary>Potwierdzenie etapu z komentarzem, potem dalsze etapy.</summary>
    public Task<string?> Confirm(string project, long pinId, int stepNo, string comment) =>
        Launch(project, (progress, cancellation) => service.Confirm(pinId, stepNo, comment, progress, cancellation), $"Etap {stepNo} potwierdzony…");

    /// <summary>Dziennik przebiegu (kod przebiegu) – panel uruchomienia.</summary>
    public IReadOnlyList<JournalEntry> Log(string code) => service.RunLog(code);

    /// <summary>„Otwórz zamknięcie” zamrożonego uruchomienia z komentarzem.</summary>
    public Task<string?> Reopen(string project, long pinId, string comment) =>
        Launch(project, (_, _) => service.Reopen(pinId, comment), "Otwieranie zamknięcia okresu…");

    /// <summary>Ponowne wykonanie etapu, potem dalsze etapy.</summary>
    public Task<string?> Rerun(string project, long pinId, int stepNo) =>
        Launch(project, (progress, cancellation) => service.Rerun(pinId, stepNo, progress, cancellation), $"Etap {stepNo} – ponowne wykonanie…");

    private async Task<string?> Launch(string project, Func<IProgress<string>, CancellationToken, RunOutcome> work, string started)
    {
        if (!_running.TryAdd(project, true))
            return "Przebieg programu już trwa.";
        try
        {
            if (service.Running(project) is { } holder)
            {
                _running.TryRemove(project, out _);
                return $"Przebieg programu trwa: {holder.Text}.";
            }
            if (imports() is { } import && store.ProjectSources(project).Count > 0 && !await import.SignIn())
            {
                _running.TryRemove(project, out _);
                return "Przebieg nie rozpoczęty – logowanie do SharePoint anulowane.";
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Błąd przed startem (baza, dysk sieciowy, SharePoint) – program nie zostaje „w toku” do restartu aplikacji.
            _running.TryRemove(project, out _);
            Serilog.Log.Error(ex, "Przebieg programu {Project} nie rozpoczęty", project);
            return $"Przebieg nie rozpoczęty – {ex.Message}";
        }
        var dispatcher = Application.Current?.Dispatcher;
        var progress = new Progress<string>(text => Progress?.Invoke(project, text));
        _ = Task.Run(() =>
        {
            RunOutcome outcome;
            try
            {
                outcome = work(progress, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                outcome = new RunOutcome(project, null, RunStatuses.Failed, 1, 0, ex.Message);
            }
            _running.TryRemove(project, out _);
            dispatcher?.BeginInvoke(() =>
            {
                WindowsNotification.Show($"PZL-EV – przebieg {outcome.Code ?? project}", outcome.Summary,
                    warning: outcome.Warnings > 0 || outcome.Status == RunStatuses.NeedsAction, error: outcome.Errors > 0);
                Finished?.Invoke(project);
            });
        });
        Progress?.Invoke(project, started);
        return null;
    }
}
