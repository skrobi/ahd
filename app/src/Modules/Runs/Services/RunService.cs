using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Process;
using Serilog;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Wynik uruchomienia przebiegu.</summary>
public sealed record RunOutcome(string Project, string? Code, string Status, int Errors, int Warnings, string Summary)
{
    public bool Failed => Status is RunStatuses.Failed || Code is null;
}

/// <summary>
/// Przebieg projektu (docs/pipeline-fazy.md, P0–P2 – kontrole danych): tydzień z kalendarza okresów → przebieg
/// i uruchomienie „w toku” → import źródeł projektu (ISourceImport) → przypięcie danych ACTUALS projektu (run.Balance)
/// → kontrole wobec poprzedniego przebiegu (RunChecks) → wynik w bazie. Blokady: przebieg projektu (jeden naraz)
/// i trwające uruchomienie (wolna po awarii – uruchomienie „przerwane”).
/// </summary>
public sealed class RunService(IRunStore store, AppServices services, Func<ISourceImport?>? imports = null, Func<IProjectProcessSource?>? processes = null)
{
    public const string Area = "Przebiegi";

    /// <summary>Dni po początku okresu, w których poprzedni okres jest jeszcze otwarty (rozliczenie na przełomie miesiąca).</summary>
    public const int SettlementDays = 7;

    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    public static string ProjectLock(string project) => $"run-{project}";

    public static string PinLock(long pinId) => $"run-pin-{pinId}";

    /// <summary>Kto teraz liczy przebieg projektu (dowolny komputer); null – nikt.</summary>
    public LockHolder? Running(string project) => services.Locks.Holder(ProjectLock(project));

    public IReadOnlyDictionary<string, RunPinRow> Latest() => store.Latest();

    /// <summary>
    /// Pierwszy otwarty okres: bieżący, a w pierwszych SettlementDays dniach okresu – poprzedni (rozliczenie
    /// poprzedniego okresu jeszcze trwa). Okres z kalendarza (RRRR-MM) = okres księgowy SAP.
    /// </summary>
    public static ((int Year, int Period) ClosedBefore, (int Year, int Period) Today) Periods(string period, DateTime today)
    {
        var current = (int.Parse(period[..4]), int.Parse(period[5..7]));
        var start = new DateTime(current.Item1, current.Item2, 1);
        var previous = current.Item2 == 1 ? (current.Item1 - 1, 12) : (current.Item1, current.Item2 - 1);
        return (today.Date < start.AddDays(SettlementDays) ? previous : current, current);
    }

    /// <summary>
    /// Wiersze kosztu (bez rozliczenia) przekształcone procesem projektu w walucie projektu (np. CAS – narzuty i kurs CAS,
    /// wskaźniki jak w raporcie kosztów CES; ostatni okres – najpóźniejszy okres kosztów); błąd – kosztów nie da się policzyć.
    /// </summary>
    private static (IReadOnlyList<RunBalanceRow> Rows, string? Error) Transformed(ProjectProcess process, IReadOnlyList<RunBalanceRow> rows)
    {
        var costs = rows.Where(r => !r.IsSettlement).ToList();
        var last = costs.Count == 0 ? (0, 0) : costs.Max(r => (r.FiscalYear, Math.Clamp(r.Period, 1, 12)));
        var result = process.Costs(costs.Select(r => new CostRow(r.WbsElement, r.CostElement.Length == 0 ? null : r.CostElement,
            r.FiscalYear, r.Period, r.Amount)).ToList(), last);
        return result.Error is not null ? ([], result.Error)
            : (costs.Select((r, i) => r with { Amount = result.Costs[i], Currency = process.Currency ?? r.Currency }).ToList(), null);
    }

    public RunOutcome Execute(string project, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        using var lease = services.Locks.TryAcquire(ProjectLock(project), out var holder);
        if (lease is null)
            return new RunOutcome(project, null, RunStatuses.Failed, 1, 0, $"Przebieg programu {project} trwa: {holder?.Text ?? "?"}");
        store.AbandonRunning(id => services.Locks.Holder(PinLock(id)) is not null, services.Clock.Now);
        var week = store.Week(services.Clock.Now.Date);
        if (week is null)
            return new RunOutcome(project, null, RunStatuses.Failed, 1, 0, $"Dzień {services.Clock.Now:yyyy-MM-dd} nie ma tygodnia w kalendarzu okresów (Słowniki → Kalendarz okresów)");

        var pin = store.StartPin(project, week, services.Clock.Now, services.User.Account, Environment.MachineName);
        using var alive = services.Locks.TryAcquire(PinLock(pin.PinId), out _);
        if (alive is null)
        {
            // Bez blokady „żyje” inny przebieg uznałby to przypięcie za przerwane.
            var refused = $"Przebieg nie rozpoczęty – nie udało się założyć blokady przypięcia w folderze {services.Config.RabitFolder}";
            store.Finish(pin.PinId, services.Clock.Now, RunStatuses.Failed, null, 0, 0, 0, [("Przebieg", CheckLevel.Error, null, refused, null)], refused);
            return new RunOutcome(project, pin.Code, RunStatuses.Failed, 1, 0, refused);
        }
        services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo} rozpoczęty", pin.Code);
        var issues = new List<RunIssue>();
        long? batchId = null;
        try
        {
            // 1. Import źródeł projektu (słownik „Źródła danych”) – pliki zapisują się raz, wspólnie dla projektów.
            var sources = store.ProjectSources(project);
            if (sources.Count == 0)
                issues.Add(new RunIssue("Import", CheckLevel.Warning, null, "Program nie ma źródeł danych (słownik „Źródła danych”) – przebieg na danych ostatniego importu"));
            else if (imports?.Invoke() is { } import)
            {
                progress?.Report($"Import źródeł programu ({string.Join(", ", sources.Order())})…");
                var imported = import.Import(sources, progress, cancellation);
                batchId = imported.BatchId == 0 ? null : imported.BatchId;
                issues.Add(new RunIssue("Import", imported.Errors > 0 ? CheckLevel.Error : CheckLevel.Pass, null, $"Import #{imported.BatchId}: {imported.Summary}"));
                issues.AddRange(imported.Issues.Where(i => i.Level == CheckLevel.Error).Take(RunChecks.Top)
                    .Select(i => new RunIssue("Import", CheckLevel.Error, i.Element, i.Message)));
            }
            cancellation.ThrowIfCancellationRequested();

            // 2. Przypięcie stanu: dane ACTUALS projektu w chwili przebiegu.
            progress?.Report("Przypięcie danych programu…");
            // Proces projektu: pole kwoty i ewentualne przekształcenie kosztów (np. CAS – PLN, narzuty, kurs CAS).
            var process = processes?.Invoke()?.For(project) ?? ProjectProcess.Default;
            var amountField = process.AmountField(store.ActualsAmount(project));
            var current = store.PinBalance(project, pin.PinId, amountField);
            var previousPin = store.Previous(project, pin.RunId);
            var previousField = previousPin?.AmountField ?? ProjectSettings.DefaultActualsAmount;
            var previous = previousPin is null || previousField != amountField ? null : store.Balance(previousPin.PinId);
            if (previousPin is not null && previous is null)
                issues.Add(new RunIssue(RunChecks.Volume, CheckLevel.Warning, null,
                    $"Pole kwoty ACTUALS zmienione ({previousField} → {amountField}, „Parametry programu”) – bez porównania z przebiegiem {previousPin.Code} #{previousPin.PinNo}"));
            else if (previousPin is not null)
                issues.Add(new RunIssue(RunChecks.Volume, CheckLevel.Pass, null, $"Porównanie z przebiegiem {previousPin.Code} #{previousPin.PinNo} ({previousPin.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm})"));

            // 3. Kontrole danych; koszty przekształcone procesem projektu (np. CAS – po narzutach), netowanie do 0 – na danych
            //    źródłowych.
            progress?.Report("Kontrole danych…");
            var source = current;
            if (process.TransformsCosts)
            {
                var (transformed, error) = Transformed(process, current);
                if (error is null)
                {
                    current = transformed;
                    previous = previous is null ? null : Transformed(process, previous) is { Error: null } before ? before.Rows : null;
                    issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Pass, null,
                        $"{process.CostsCheck} w {process.Currency}: koszt brutto {Math.Round(RunChecks.GrossCost(current), 2):#,0.00}"));
                }
                else
                    issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Error, null, $"{process.CostsCheck} niepoliczone – {error}; kontrole na danych źródłowych"));
            }
            var (closedBefore, today) = Periods(week.Period, services.Clock.Now.Date);
            // Reguły kontroli dla typu programu i wariantu przebiegu – słownik „Kontrole przebiegu” (brak wiersza – ustawienia domyślne).
            var settings = RunChecks.Settings(store.CheckSettings(), store.ProjectType(project) ?? "", week.IsClosing);
            issues.AddRange(RunChecks.Check(current, previous, store.CostElements(project), closedBefore, today,
                current != source ? process.Currency : amountField == ProjectSettings.DefaultActualsAmount ? "PLN" : null, source, settings,
                store.Dictionaries(project, today.Year)));

            var errors = issues.Count(i => i.Level == CheckLevel.Error);
            var warnings = issues.Count(i => i.Level == CheckLevel.Warning);
            var status = errors > 0 ? RunStatuses.Errors : warnings > 0 ? RunStatuses.Warnings : RunStatuses.Ok;
            var rows = source.Sum(r => (long)r.Rows);
            var balance = source.Where(r => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(closedBefore) < 0).Sum(r => r.Amount);
            var summary = $"{status}: błędy {errors}, ostrzeżenia {warnings}; wierszy {rows:#,0}, saldo okresów zamkniętych {Math.Round(balance, 2):#,0.00}";
            store.Finish(pin.PinId, services.Clock.Now, status, batchId, rows, RunChecks.GrossCost(current), balance,
                issues.Select(i => (i.Check, i.Level, i.Element, i.Message, i.Amount)).ToList(), summary);
            services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo}: {summary}", pin.Code);
            return new RunOutcome(project, pin.Code, status, errors, warnings, summary);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Przebieg {Code} #{Pin} przerwany", pin.Code, pin.PinNo);
            var reason = cancellation.IsCancellationRequested ? "przerwany przez użytkownika" : $"{ex.GetType().Name}: {ex.Message}";
            issues.Add(new RunIssue("Przebieg", CheckLevel.Error, null, $"Przebieg przerwany: {reason}"));
            store.Finish(pin.PinId, services.Clock.Now, RunStatuses.Failed, batchId, 0, 0, 0,
                issues.Select(i => (i.Check, i.Level, i.Element, i.Message, i.Amount)).ToList(), $"Przerwany: {reason}");
            services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo} przerwany: {reason}", pin.Code);
            return new RunOutcome(project, pin.Code, RunStatuses.Failed, 1, 0, $"Przerwany: {reason}");
        }
    }
}
