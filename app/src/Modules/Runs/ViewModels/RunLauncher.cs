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
/// po zakończeniu powiadomienie Windows.
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

    public async Task<string?> Start(string project)
    {
        if (!_running.TryAdd(project, true))
            return "Przebieg projektu już trwa.";
        try
        {
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
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Błąd przed startem (baza, dysk sieciowy, SharePoint) – projekt nie zostaje „w toku” do restartu aplikacji.
            _running.TryRemove(project, out _);
            Serilog.Log.Error(ex, "Przebieg projektu {Project} nie rozpoczęty", project);
            return $"Przebieg nie rozpoczęty – {ex.Message}";
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
}
