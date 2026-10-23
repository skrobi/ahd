using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Process;
using Serilog;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Wynik kontroli przebiegu: kontrola (wynik reguły – nazwa reguły), poziom, element, opis, kwota, wiersz wyniku reguły (JSON).</summary>
public sealed record RunIssue(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount = null, string? Details = null);

/// <summary>Wynik uruchomienia przebiegu.</summary>
public sealed record RunOutcome(string Project, string? Code, string Status, int Errors, int Warnings, string Summary)
{
    public bool Failed => Status is RunStatuses.Failed || Code is null;
}

/// <summary>
/// Przebieg projektu (docs/pipeline-fazy.md, P0–P2 – kontrole danych): tydzień z kalendarza okresów → przebieg
/// i uruchomienie „w toku” → import źródeł projektu (ISourceImport) → przypięcie danych ACTUALS projektu (run.Balance, run.BalanceEv)
/// → reguły walidacji w SQL wobec poprzedniego przebiegu (RunRules) → wynik w bazie. Blokady: przebieg projektu (jeden naraz)
/// i trwające uruchomienie (wolna po awarii – uruchomienie „przerwane”).
/// </summary>
public sealed class RunService(IRunStore store, AppServices services, Func<ISourceImport?>? imports = null, Func<IProjectProcessSource?>? processes = null,
    Func<ICamReview?>? cams = null, Func<ICostAssignment?>? costs = null)
{
    public const string Area = "Przebiegi";

    /// <summary>Kontrola wyników etapu Zatwierdzenie ETC – stan estymacji CAM (wiersz macierzy).</summary>
    public const string CamCheck = "Estymacje CAM";

    /// <summary>Kontrola wyników etapu Zatwierdzenie ETC – powiadomienia CAM (wiersz macierzy).</summary>
    public const string CamNotifyCheck = "Powiadomienie CAM";

    /// <summary>Wynik etapu pominiętego warunkiem „Przebieg zamykający” w tygodniu bez zamknięcia okresu.</summary>
    public const string Skipped = "Warunek etapu";

    /// <summary>Kontrola wyników etapu Łączenie źródeł – przypisanie kosztu do WP (wiersz macierzy).</summary>
    public const string Coverage = "Łączenie źródeł";

    /// <summary>Kontrola wyników etapu Przypięcie stanu (wiersz macierzy).</summary>
    public const string Pinned = "Przypięcie stanu";

    /// <summary>Najwyżej tyle błędów importu w wynikach etapu.</summary>
    private const int Top = 10;

    /// <summary>Dni po początku okresu, w których poprzedni okres jest jeszcze otwarty (rozliczenie na przełomie miesiąca).</summary>
    public const int SettlementDays = 7;

    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    public static string ProjectLock(string project) => $"run-{project}";

    public static string PinLock(long pinId) => $"run-pin-{pinId}";

    /// <summary>Kto teraz liczy przebieg projektu (dowolny komputer); null – nikt.</summary>
    public LockHolder? Running(string project) => services.Locks.Holder(ProjectLock(project));

    public IReadOnlyDictionary<string, RunPinRow> Latest() => store.Latest();

    /// <summary>Dziennik przebiegu (wszystkie uruchomienia tygodnia): wpisy obszaru Przebiegi z kodem przebiegu, od najnowszego.</summary>
    public IReadOnlyList<JournalEntry> RunLog(string code, int count = 200) => services.Journal.ForScope(Area, code, count);

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
    internal static (IReadOnlyList<RunBalanceRow> Rows, string? Error) Transformed(ProjectProcess process, IReadOnlyList<RunBalanceRow> rows)
    {
        var costs = rows.Where(r => !r.IsSettlement).ToList();
        var last = costs.Count == 0 ? (0, 0) : costs.Max(r => (r.FiscalYear, Math.Clamp(r.Period, 1, 12)));
        var result = process.Costs(costs.Select(r => new CostRow(r.WbsElement, r.CostElement.Length == 0 ? null : r.CostElement,
            r.FiscalYear, r.Period, r.Amount)).ToList(), last);
        return result.Error is not null ? ([], result.Error)
            : (costs.Select((r, i) => r with { Amount = result.Costs[i], Currency = process.Currency ?? r.Currency }).ToList(), null);
    }

    /// <summary>
    /// Nowe uruchomienie przebiegu programu: tydzień z kalendarza okresów, przebieg wybrany w programie albo domyślny dla typu
    /// (RunDefinitions.For; bez szablonów – definicja wbudowana) zapisany w uruchomieniu, etapy – potem wykonanie etapów do najbliższej bramki.
    /// </summary>
    public RunOutcome Execute(string project, IProgress<string>? progress = null, CancellationToken cancellation = default) =>
        Locked(project, () =>
        {
            var week = store.Week(services.Clock.Now.Date);
            if (week is null)
                return new RunOutcome(project, null, RunStatuses.Failed, 1, 0, $"Dzień {services.Clock.Now:yyyy-MM-dd} nie ma tygodnia w kalendarzu okresów (Słowniki → Kalendarz okresów)");
            if (store.FrozenIn(project, week.Year, week.Week) is { } frozen)
                return new RunOutcome(project, null, RunStatuses.Failed, 1, 0, $"Nowe uruchomienie odrzucone – {frozen.FrozenText}");
            var (headers, steps, elements) = store.DefinitionRows();
            var assigned = store.AssignedDefinition(project);
            var definition = RunDefinitions.For(headers, steps, elements, assigned, store.ProjectType(project) ?? "");
            var pin = store.StartPin(project, week, services.Clock.Now, services.User.Account, Environment.MachineName);
            store.StartSteps(pin.PinId, RunWorkflowEngine.Serialize(definition), definition.Steps);
            var note = assigned is not null && !string.Equals(assigned, definition.Code, StringComparison.OrdinalIgnoreCase)
                ? $"; wybrany przebieg „{assigned}” nieaktywny albo bez etapów – użyto „{definition.Code}”" : "";
            services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo} ({(week.IsClosing ? "zamykający okres" : "w środku okresu")}) rozpoczęty – "
                + $"przebieg „{definition.Code}” ({definition.Steps.Count} etapów){note}", pin.Code);
            return Advance(pin, progress, cancellation);
        });

    /// <summary>Dalsze etapy uruchomienia do najbliższej bramki (np. po poprawie danych innego etapu w innej aplikacji).</summary>
    public RunOutcome Continue(long pinId, IProgress<string>? progress = null, CancellationToken cancellation = default) =>
        WithPin(pinId, pin => Advance(pin, progress, cancellation));

    /// <summary>
    /// Potwierdzenie etapu wymagającego akcji (WARNING przy bramce, zatwierdzenie ręczne) albo akceptacja błędów etapu zatrzymanego (ERROR)
    /// z komentarzem – komentarz w etapie, dziennik; potem dalsze etapy. Błędy zaakceptowane zostają w wynikach etapu.
    /// </summary>
    public RunOutcome Confirm(long pinId, int stepNo, string comment, IProgress<string>? progress = null, CancellationToken cancellation = default) =>
        WithPin(pinId, pin =>
        {
            var steps = store.Steps(pinId);
            if (!RunWorkflowEngine.CanConfirm(steps, stepNo))
                return Outcome(pin, steps, $"Etap {stepNo} nie wymaga potwierdzenia.");
            if (string.IsNullOrWhiteSpace(comment))
                return Outcome(pin, steps, "Potwierdzenie wymaga komentarza.");
            var step = steps.Single(s => s.StepNo == stepNo);
            var at = services.Clock.Now;
            var what = step.Status == RunStepStatuses.Failed ? $"błędy zaakceptowane ({step.Errors})"
                : step.Action == RunActions.ApproveActuals ? "dane ACTUALS zatwierdzone (struktura programu)" : "potwierdzony";
            if (step.Action == RunActions.ApproveEtc && step.Status != RunStepStatuses.Failed)
            {
                // Wynik etapu – stan estymacji w chwili akceptacji (decyzje finansisty w panelu etapu zmieniły go od wykonania).
                List<RunIssue> issues;
                CamReview? review;
                try
                {
                    (issues, review) = CamState(pin, DefinitionOf(pin));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Logger.Error(ex, "Przebieg {Code} #{Pin}: odczyt estymacji CAM przy akceptacji", pin.Code, pin.PinNo);
                    return Outcome(pin, steps, $"Nie zaakceptowano – błąd odczytu estymacji CAM: {ex.Message}");
                }
                // Wyniki powiadomień z wykonania etapu zostają (kto i kiedy dostał wezwanie).
                issues.InsertRange(0, store.Issues(pinId).Where(i => i.StepNo == stepNo && i.CheckName == CamNotifyCheck)
                    .Select(i => new RunIssue(i.CheckName, i.Level, i.Element, i.Message, i.Amount, i.Details)));
                store.ReplaceStepIssues(pinId, stepNo, issues.Select(i => (i.Check, i.Level, i.Element, i.Message, i.Amount, i.Details)).ToList());
                step = step with { Errors = 0, Warnings = issues.Count(i => i.Level == CheckLevel.Warning) };
                what = review is null ? "zaakceptowany bez estymacji CAM (moduł CAM niedostępny)"
                    : $"estymacje CAM zaakceptowane – zatwierdzone {review.Approved} z {review.Required} wymaganych" +
                      (review.Approved < review.Required ? $", bez zatwierdzenia {review.Required - review.Approved}" : "");
            }
            if (step.Action == RunActions.ClosePeriod && step.Status != RunStepStatuses.Failed)
            {
                store.SetFrozen(pin.RunId, pinId, at, services.User.Account);
                what = $"okres {pin.Period} zamknięty – uruchomienie zamrożone";
            }
            store.SetStep(pinId, stepNo, RunStepStatuses.Done, step.StartedAt, step.FinishedAt ?? at, services.User.Account, step.Errors, step.Warnings,
                $"{at.ToLocalTime():yyyy-MM-dd HH:mm} {services.User.Account} – {what}: {comment.Trim()}");
            services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo}: etap {stepNo} „{step.Name}” {what} – {comment.Trim()}", pin.Code);
            return Advance(pin, progress, cancellation);
        });

    /// <summary>
    /// „Otwórz zamknięcie” zamrożonego uruchomienia z komentarzem: przebieg tygodnia znów do zmian, etap Zamknięcie okresu – „Wymaga akcji”
    /// (ponowne zatwierdzenie zamraża znowu); komentarz w etapie i dzienniku.
    /// </summary>
    public RunOutcome Reopen(long pinId, string comment) =>
        WithPin(pinId, pin =>
        {
            var steps = store.Steps(pinId);
            if (!pin.IsFrozenPin)
                return Outcome(pin, steps, "Uruchomienie nie jest zamrożone.");
            if (string.IsNullOrWhiteSpace(comment))
                return Outcome(pin, steps, "Otwarcie zamknięcia wymaga komentarza.");
            var at = services.Clock.Now;
            store.SetFrozen(pin.RunId, null, null, null);
            if (steps.LastOrDefault(s => s.Action == RunActions.ClosePeriod) is { } close)
                store.SetStep(pinId, close.StepNo, RunStepStatuses.NeedsAction, close.StartedAt, close.FinishedAt, services.User.Account, close.Errors, close.Warnings,
                    $"{(close.Comment is null ? "" : close.Comment + "\n")}{at.ToLocalTime():yyyy-MM-dd HH:mm} {services.User.Account} – zamknięcie otwarte ponownie: {comment.Trim()}");
            steps = store.Steps(pinId);
            var summary = RunWorkflowEngine.Summary(steps);
            store.SetPinState(pinId, RunWorkflowEngine.PinStatus(steps), null, summary);
            services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo}: zamknięcie okresu {pin.Period} otwarte ponownie – {comment.Trim()}", pin.Code);
            return Outcome(store.Pin(pinId)!, steps, $"Zamknięcie okresu {pin.Period} otwarte – {summary}");
        }, frozenAllowed: true);

    /// <summary>Ponowne wykonanie etapu (po poprawie danych albo słowników): późniejsze etapy – Nieaktualne, potem wykonanie do bramki.</summary>
    public RunOutcome Rerun(long pinId, int stepNo, IProgress<string>? progress = null, CancellationToken cancellation = default) =>
        WithPin(pinId, pin =>
        {
            var steps = store.Steps(pinId);
            if (!RunWorkflowEngine.CanRerun(steps, stepNo))
                return Outcome(pin, steps, $"Etapu {stepNo} nie można teraz wykonać ponownie – wcześniejsze etapy muszą być zakończone.");
            foreach (var (no, status) in RunWorkflowEngine.Rerun(steps, stepNo))
            {
                var step = steps.Single(s => s.StepNo == no);
                store.SetStep(pinId, no, status, step.StartedAt, step.FinishedAt, step.ActedBy, step.Errors, step.Warnings);
            }
            services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo}: etap {stepNo} wykonywany ponownie", pin.Code);
            return Advance(pin, progress, cancellation);
        });

    /// <summary>Operacja na uruchomieniu pod blokadą przebiegu; przebieg zamrożony (zamknięcie okresu) – odrzucona, poza frozenAllowed.</summary>
    private RunOutcome WithPin(long pinId, Func<RunPinRow, RunOutcome> action, bool frozenAllowed = false)
    {
        var pin = store.Pin(pinId) ?? throw new InvalidOperationException($"Nie ma uruchomienia przebiegu {pinId}.");
        return Locked(pin.Project, () => store.Pin(pinId)! is var current && current.IsFrozen && !frozenAllowed
            ? Outcome(current, store.Steps(pinId), $"Nie wykonano – {current.FrozenText}")
            : action(current));
    }

    /// <summary>Operacja pod blokadą przebiegu programu (jeden naraz, także między komputerami); uruchomienia przerwane – „przerwany”.</summary>
    private RunOutcome Locked(string project, Func<RunOutcome> action)
    {
        using var lease = services.Locks.TryAcquire(ProjectLock(project), out var holder);
        if (lease is null)
            return new RunOutcome(project, null, RunStatuses.Failed, 1, 0, $"Przebieg programu {project} trwa: {holder?.Text ?? "?"}");
        store.AbandonRunning(id => services.Locks.Holder(PinLock(id)) is not null, services.Clock.Now);
        return action();
    }

    /// <summary>Wykonuje kolejne etapy, dopóki etap kończy się „Zakończony”; zatrzymuje się na bramce (Błąd, Wymaga akcji).</summary>
    private RunOutcome Advance(RunPinRow pin, IProgress<string>? progress, CancellationToken cancellation)
    {
        var definition = RunWorkflowEngine.Deserialize(store.DefinitionOf(pin.PinId)) ?? RunDefinitions.BuiltIn;
        using var alive = services.Locks.TryAcquire(PinLock(pin.PinId), out _);
        if (alive is null)
        {
            // Bez blokady „żyje” inny przebieg uznałby uruchomienie za przerwane.
            var refused = $"Przebieg nie wykonany – nie udało się założyć blokady uruchomienia w folderze {services.Config.RabitFolder}";
            store.SetPinState(pin.PinId, RunStatuses.Failed, services.Clock.Now, refused);
            return new RunOutcome(pin.Project, pin.Code, RunStatuses.Failed, 1, 0, refused);
        }
        store.SetPinState(pin.PinId, RunStatuses.Running, null, $"{RunStatuses.Running}");
        while (RunWorkflowEngine.Next(store.Steps(pin.PinId)) is { } next)
        {
            var step = definition.Steps.FirstOrDefault(s => s.No == next.StepNo)
                       ?? new RunStepDefinition(next.StepNo, next.Name, next.Action, RunGates.Confirm, false, []);
            var started = services.Clock.Now;
            store.SetStep(pin.PinId, step.No, RunStepStatuses.Running, started, null, services.User.Account, 0, 0);
            var label = $"Etap {step.No}/{definition.Steps.Count}: {step.Name}";
            progress?.Report($"{label}…");
            List<RunIssue> issues;
            // Warunek „Przebieg zamykający” w tygodniu bez zamknięcia okresu – etap zakończony bez akcji (także bez czekania na zatwierdzenie).
            var skipped = !step.RunsIn(store.RunWeek(pin.RunId).IsClosing);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                issues = skipped
                    ? [new RunIssue(Skipped, CheckLevel.Pass, null, $"Etap pominięty – przebieg w środku okresu (warunek: {RunConditions.Closing.ToLowerInvariant()})")]
                    : ExecuteStep(store.Pin(pin.PinId)!, step, label, progress, cancellation, definition);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.Error(ex, "Przebieg {Code} #{Pin}: etap {Step} przerwany", pin.Code, pin.PinNo, step.No);
                var reason = cancellation.IsCancellationRequested ? "przerwany przez użytkownika" : $"{ex.GetType().Name}: {ex.Message}";
                issues = [new RunIssue("Przebieg", CheckLevel.Error, null, $"Etap „{step.Name}” przerwany: {reason}")];
            }
            var errors = issues.Count(i => i.Level == CheckLevel.Error);
            var warnings = issues.Count(i => i.Level == CheckLevel.Warning);
            var status = skipped && errors == 0 ? RunStepStatuses.Done : RunWorkflowEngine.Gate(step, errors, warnings);
            store.ReplaceStepIssues(pin.PinId, step.No, issues.Select(i => (i.Check, i.Level, i.Element, i.Message, i.Amount, i.Details)).ToList());
            store.SetStep(pin.PinId, step.No, status, started, services.Clock.Now, services.User.Account, errors, warnings);
            services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo}: etap {step.No} „{step.Name}” – {status.ToLowerInvariant()} (błędy {errors}, ostrzeżenia {warnings})", pin.Code);
            if (status != RunStepStatuses.Done)
                break;
            // Kolejny etap – do wykonania (Oczekuje / Nieaktualny wykona pętla).
        }
        var steps = store.Steps(pin.PinId);
        var final = RunWorkflowEngine.PinStatus(steps);
        var summary = RunWorkflowEngine.Summary(steps);
        store.SetPinState(pin.PinId, final, final is RunStatuses.Ok or RunStatuses.Warnings ? services.Clock.Now : null, summary);
        return Outcome(store.Pin(pin.PinId)!, steps, summary);
    }

    private static RunOutcome Outcome(RunPinRow pin, IReadOnlyList<RunStepRow> steps, string summary) =>
        new(pin.Project, pin.Code, RunWorkflowEngine.PinStatus(steps), steps.Sum(s => s.Errors), steps.Sum(s => s.Warnings), summary);

    /// <summary>
    /// Akcja etapu: import źródeł, przypięcie stanu, kontrole (elementy kontrolne etapu), zatwierdzenie i zatwierdzenie ACTUALS (bez wykonania –
    /// czekają na analityka); label – postęp etapu.
    /// </summary>
    private List<RunIssue> ExecuteStep(RunPinRow pin, RunStepDefinition step, string label, IProgress<string>? progress, CancellationToken cancellation,
        RunDefinition definition) =>
        step.Action switch
        {
            RunActions.Import => ImportStep(pin, progress, cancellation),
            RunActions.Pin => PinStep(pin),
            RunActions.Checks => ChecksStep(pin, step, label, progress, cancellation),
            RunActions.ApproveEtc => [.. step.Notify ? CamNotifyStep(pin, definition) : [], .. CamState(pin, definition).Issues],
            RunActions.Reconcile => ReconcileStep(pin, step),
            _ => [],
        };

    /// <summary>
    /// Cykl estymacji CAM programu w tygodniu uruchomienia: program tygodniowy (klocek Zatwierdzenie ETC z warunkiem „Każdy przebieg” –
    /// RunDefinitions.CamWeekly) – tydzień RRRR-Tnn, inaczej okres tygodnia RRRR-MM.
    /// </summary>
    public CamCycle CamCycleOf(RunPinRow pin, RunDefinition definition, ICamReview source)
    {
        var week = store.RunWeek(pin.RunId);
        return source.CycleOf(RunDefinitions.CamWeekly(definition) ? CamCycles.Week(week.Year, week.Week) : week.Period);
    }

    /// <summary>Definicja zapisana w uruchomieniu (bez niej – wbudowana).</summary>
    private RunDefinition DefinitionOf(RunPinRow pin) => RunWorkflowEngine.Deserialize(store.DefinitionOf(pin.PinId)) ?? RunDefinitions.BuiltIn;

    /// <summary>
    /// Powiadomienie CAM (początek etapu Zatwierdzenie ETC z Powiadomieniem): e-mail do CAM-ów z zaległymi estymacjami programu w cyklu
    /// (ICamReview.Notify) – wynik na CAM-a (wysłano – Pass, brak adresu / błąd wysyłki – WARNING); CAM powiadomiony w cyklu – pominięty.
    /// </summary>
    private List<RunIssue> CamNotifyStep(RunPinRow pin, RunDefinition definition)
    {
        if (cams?.Invoke() is not { } source)
            return [new RunIssue(CamNotifyCheck, CheckLevel.Warning, null, "Moduł CAM niedostępny w tej aplikacji – powiadomienia nie wysłane")];
        var cycle = CamCycleOf(pin, definition, source);
        var result = source.Notify(pin.Project, cycle, pin.PinId, resend: false);
        var issues = new List<RunIssue>
        {
            new(CamNotifyCheck, CheckLevel.Pass, null,
                $"Powiadomienia CAM – cykl {cycle.Period}: wysłane {result.Notifications.Count(n => n.Result == CamNotificationResults.Sent)}" +
                (result.AlreadyNotified.Count > 0 ? $" · powiadomieni wcześniej w cyklu: {result.AlreadyNotified.Count}" : "") +
                (result.Notifications.Count == 0 && result.AlreadyNotified.Count == 0 ? " · brak zaległych estymacji" : "")),
        };
        issues.AddRange(result.Notifications.Select(n => new RunIssue(CamNotifyCheck, n.Result == CamNotificationResults.Sent ? CheckLevel.Pass : CheckLevel.Warning,
            n.Cam, $"{n.Name}{(n.Email is null ? "" : $" <{n.Email}>")}: {n.Result}{(n.Message is null ? "" : $" – {n.Message}")} · WP: {n.Wps}")));
        return issues;
    }

    /// <summary>
    /// Estymacje CAM programu w cyklu przebiegu (ICamReview) – etap Zatwierdzenie ETC: podsumowanie wymaganych WP wobec zatwierdzonych i WARNING
    /// dla każdego wymaganego WP bez zatwierdzonej estymacji (po terminie cyklu – w opisie); decyzje – panel etapu, akceptacja – Confirm.
    /// </summary>
    private (List<RunIssue> Issues, CamReview? Review) CamState(RunPinRow pin, RunDefinition definition)
    {
        if (cams?.Invoke() is not { } source)
            return ([new RunIssue(CamCheck, CheckLevel.Warning, null, "Moduł CAM niedostępny w tej aplikacji – estymacje CAM bez przeglądu")], null);
        var review = source.Program(pin.Project, CamCycleOf(pin, definition, source));
        var overdue = review.Overdue(DateOnly.FromDateTime(services.Clock.Now.LocalDateTime));
        var issues = new List<RunIssue>
        {
            new(CamCheck, review.Approved == review.Required ? CheckLevel.Pass : CheckLevel.Warning, null, review.Summary),
        };
        issues.AddRange(review.Items.Where(i => i.Required && i.Status != CamStatuses.Approved).Select(i => new RunIssue(CamCheck, CheckLevel.Warning, i.Wp,
            $"{i.Name} – CAM {i.Cam}: {i.Status.ToLowerInvariant()}" + (overdue && i.Status is CamStatuses.ToDo or CamStatuses.Draft ? " (po terminie cyklu)" : ""))));
        return (issues, review);
    }

    // Import źródeł programu (słownik „Źródła danych”) – pliki zapisują się raz, wspólnie dla programów.
    private List<RunIssue> ImportStep(RunPinRow pin, IProgress<string>? progress, CancellationToken cancellation)
    {
        var sources = store.ProjectSources(pin.Project);
        if (sources.Count == 0)
            return [new RunIssue("Import", CheckLevel.Warning, null, "Program nie ma źródeł danych (słownik „Źródła danych”) – przebieg na danych ostatniego importu")];
        if (imports?.Invoke() is not { } import)
            return [new RunIssue("Import", CheckLevel.Warning, null, "Import źródeł niedostępny w tej aplikacji – przebieg na danych ostatniego importu")];
        progress?.Report($"Import źródeł programu ({string.Join(", ", sources.Order())})…");
        var imported = import.Import(sources, progress, cancellation);
        store.SetPinImport(pin.PinId, imported.BatchId == 0 ? null : imported.BatchId);
        return
        [
            new RunIssue("Import", imported.Errors > 0 ? CheckLevel.Error : CheckLevel.Pass, null, $"Import #{imported.BatchId}: {imported.Summary}"),
            .. imported.Issues.Where(i => i.Level == CheckLevel.Error).Take(Top).Select(i => new RunIssue("Import", CheckLevel.Error, i.Element, i.Message)),
        ];
    }

    // Przypięcie stanu: dane ACTUALS programu w chwili przebiegu (pole kwoty procesu programu), porównanie z poprzednim przebiegiem,
    // koszty procesu programu (np. CAS – po narzutach), dane jak w strukturze (tabela „ev” reguł walidacji).
    private List<RunIssue> PinStep(RunPinRow pin)
    {
        var issues = new List<RunIssue>();
        var process = Process(pin.Project);
        var amountField = process.AmountField(store.ActualsAmount(pin.Project));
        var source = store.PinBalance(pin.Project, pin.PinId, amountField);
        var previousPin = store.Previous(pin.Project, pin.RunId);
        var previousField = previousPin?.AmountField ?? ProjectSettings.DefaultActualsAmount;
        if (previousPin is not null && previousField != amountField)
            issues.Add(new RunIssue(Pinned, CheckLevel.Warning, null,
                $"Pole kwoty ACTUALS zmienione ({previousField} → {amountField}, „Parametry programu”) – bez porównania z przebiegiem {previousPin.Code} #{previousPin.PinNo}"));
        else if (previousPin is not null)
            issues.Add(new RunIssue(Pinned, CheckLevel.Pass, null, $"Porównanie z przebiegiem {previousPin.Code} #{previousPin.PinNo} ({previousPin.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm})"));
        var current = source;
        if (process.TransformsCosts)
        {
            var (transformed, error) = Transformed(process, source);
            if (error is null)
            {
                current = transformed;
                issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Pass, null,
                    $"{process.CostsCheck} w {process.Currency}: koszt brutto {Math.Round(GrossCost(current), 2):#,0.00}"));
            }
            else
                issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Error, null, $"{process.CostsCheck} niepoliczone – {error}; dane „ev” reguł bez narzutów"));
        }
        var (closedBefore, _) = Periods(store.RunWeek(pin.RunId).Period, services.Clock.Now.Date);
        var balance = source.Where(r => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(closedBefore) < 0).Sum(r => r.Amount);
        store.SetPinBalance(pin.PinId, source.Sum(r => (long)r.Rows), GrossCost(current), balance);
        issues.Add(new RunIssue(Pinned, CheckLevel.Pass, null,
            $"Przypięto {source.Sum(r => (long)r.Rows):#,0} wierszy ACTUALS ({amountField}); saldo okresów zamkniętych {Math.Round(balance, 2):#,0.00}"));
        SaveEv(pin, process, source);
        issues.AddRange(PinFiles(pin));
        return issues;
    }

    /// <summary>
    /// Informacja przypięcia (bez wiązania danych z przebiegiem): z jakich plików ACTUALS są dane programu – raport RABIT, import (w tym
    /// przebiegu albo wcześniejszy, np. pełny import RABIT) i czy są nowe względem poprzedniego uruchomienia programu (każdego tygodnia).
    /// </summary>
    private IEnumerable<RunIssue> PinFiles(RunPinRow pin)
    {
        static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var files = store.PinFiles(pin.Project, services.Clock.Now);
        var previous = store.Pins(pin.Project, 20).FirstOrDefault(p => p.PinId != pin.PinId && p.Rows is not null && p.StartedAt <= pin.StartedAt);
        var previousAt = previous is null ? (DateTimeOffset?)null
            : store.Steps(previous.PinId).FirstOrDefault(s => s.Action == RunActions.Pin)?.FinishedAt ?? previous.StartedAt;
        bool IsNew(RunPinFile f) => previousAt is null || f.ImportedAt > previousAt;
        var label = previous is null ? "" : $"{previous.Code} #{previous.PinNo}";
        var news = files.Where(IsNew).ToList();
        var summary = files.Count == 0 ? "Dane ACTUALS: brak plików z danymi programu"
            : $"Dane ACTUALS: {files.Count} plik(ów), raport RABIT {Time(files.Min(f => f.ReportAt))}"
              + (Time(files.Min(f => f.ReportAt)) == Time(files.Max(f => f.ReportAt)) ? "" : $" – {Time(files.Max(f => f.ReportAt))}")
              + (previous is null ? " – pierwszy przebieg programu"
                  : news.Count == 0 ? $" – bez zmian względem przebiegu {label} (te same dane)"
                  : $" – nowe względem przebiegu {label}: {string.Join(", ", news.Select(f => f.FileName))}");
        yield return new RunIssue(Pinned, CheckLevel.Pass, null, summary);
        RunRuleColumn[] columns = [new("Plik", false), new("RaportRABIT", false), new("Import", false), new("ImportNr", true), new("Pochodzenie", false), new("Nowe", false)];
        foreach (var f in files)
            yield return new RunIssue(Pinned, CheckLevel.Pass, null, $"{f.FileName}: raport RABIT {Time(f.ReportAt)}", null, RunRules.Details(columns,
            [
                f.FileName, Time(f.ReportAt), Time(f.ImportedAt), f.BatchId,
                f.BatchId is { } batch && batch == pin.ImportBatchId ? "import w tym przebiegu" : "wcześniejszy import (program albo pełny import RABIT)",
                previous is null ? "pierwszy przebieg" : IsNew(f) ? "tak" : $"nie – jak w {label}",
            ]));
    }

    /// <summary>Koszt brutto: wiersze bez rozliczenia wychodzącego (jak ACWP struktury).</summary>
    public static decimal GrossCost(IEnumerable<RunBalanceRow> rows) => rows.Where(r => !r.IsSettlement).Sum(r => r.Amount);

    /// <summary>
    /// Dane jak w strukturze programu (tabela „ev” reguł walidacji): wiersze kosztu bez rozliczeniowych, po Wykluczeniach (bez partnera –
    /// partnera nie ma w danych przypiętych), przekształcone procesem programu (CAS – po narzutach); rodzaj kosztu z Cost Category
    /// efektywnego. Error – kosztów procesu nie da się policzyć (dane bez przekształcenia).
    /// </summary>
    internal static (IReadOnlyList<RunEvRow> Rows, string? Error) Ev(IReadOnlyList<RunBalanceRow> source, IReadOnlyList<RunExclusionRow> exclusions,
        ProjectProcess process, IReadOnlyDictionary<string, string>? kinds)
    {
        IReadOnlyList<RunBalanceRow> rows = source.Where(r => !r.IsSettlement && !exclusions.Any(x => x.Covers(r))).ToList();
        string? error = null;
        if (process.TransformsCosts)
        {
            var transformed = Transformed(process, rows);
            if (transformed.Error is null)
                rows = transformed.Rows;
            else
                error = transformed.Error;
        }
        return (rows.GroupBy(r => (r.WbsElement, r.CostElement, r.FiscalYear, r.Period, Kind: RunCostKinds.Of(r, kinds)))
            .Select(g => new RunEvRow(g.Key.WbsElement, g.Key.CostElement, g.Key.FiscalYear, g.Key.Period, g.Key.Kind, g.Sum(r => r.Amount))).ToList(), error);
    }

    private string? SaveEv(RunPinRow pin, ProjectProcess process, IReadOnlyList<RunBalanceRow> source)
    {
        var (rows, error) = Ev(source, store.Exclusions(pin.Project), process, store.Dictionaries(pin.Project).CostKinds);
        store.SaveEv(pin.PinId, rows);
        return error;
    }

    // Kontrole etapu (docs/pipeline-fazy.md, rozdz. 4.1): reguły walidacji w SQL zaznaczone w etapie – na danych uruchomienia
    // (biezace – cała pula ACTUALS; ev – jak w strukturze, odświeżane przed regułami: słowniki i Wykluczenia mogły się zmienić)
    // wobec poprzedniego przebiegu z tym samym polem kwoty. Postęp – reguła po regule.
    private List<RunIssue> ChecksStep(RunPinRow pin, RunStepDefinition step, string label, IProgress<string>? progress, CancellationToken cancellation)
    {
        if (pin.Rows is null)
            return [new RunIssue("Przebieg", CheckLevel.Error, null, $"Etap „{step.Name}” przed przypięciem stanu – w definicji przebiegu brakuje etapu z akcją „{RunActions.Pin}” przed kontrolami")];
        var issues = new List<RunIssue>();
        var (rules, missing) = RunWorkflowEngine.Rules(step, store.Rules());
        if (rules.Count > 0)
        {
            var process = Process(pin.Project);
            progress?.Report($"{label} – przygotowanie danych reguł…");
            var source = store.Balance(pin.PinId);
            if (SaveEv(pin, process, source) is { } error)
                issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Warning, null, $"{process.CostsCheck} niepoliczone – {error}; tabela „ev” bez narzutów"));
            var previous = Comparable(store, pin);
            if (previous is not null && !store.HasEv(previous.PinId))
                SaveEv(previous, process, store.Balance(previous.PinId));
            var context = Context(pin, previous, source, services.Clock.Now.Date);
            foreach (var (rule, i) in rules.Select((r, i) => (r, i + 1)))
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report($"{label} – reguła {i}/{rules.Count}: {rule.Name}…");
                issues.AddRange(Issues(rule, store.RunRule(rule.Sql, context)));
            }
            var exclusions = store.Exclusions(pin.Project);
            if (exclusions.Any(x => x.PartnerObject is not null))
                issues.Add(new RunIssue(RunRules.EvArea, CheckLevel.Pass, null,
                    $"Wykluczenia z Partner object ({exclusions.Count(x => x.PartnerObject is not null)}) nie są stosowane w tabeli „ev” – dane przypięte nie mają partnera"));
        }
        issues.AddRange(missing.Select(name => new RunIssue(name, CheckLevel.Warning, name,
            $"Reguła „{name}” z szablonu przebiegu nie istnieje w regułach walidacji (ekran Przebiegi) – niesprawdzona")));
        return issues;
    }

    /// <summary>Poprzednie uruchomienie do porównania: ostatnie uruchomienie wcześniejszego tygodnia z tym samym polem kwoty; null – brak.</summary>
    internal static RunPinRow? Comparable(IRunStore store, RunPinRow pin)
    {
        var previous = store.Previous(pin.Project, pin.RunId);
        return previous is not null && (previous.AmountField ?? ProjectSettings.DefaultActualsAmount) == (pin.AmountField ?? ProjectSettings.DefaultActualsAmount)
            ? previous : null;
    }

    /// <summary>
    /// Dane reguł dla uruchomienia: okresy z tygodnia przebiegu i dnia (wykonanie – dziś, test – dzień uruchomienia), waluta kosztów
    /// (PLN dla pola kwoty w walucie obiektu, inaczej przeważająca w danych), typ programu.
    /// </summary>
    internal static RunRuleContext Context(IRunStore store, RunPinRow pin, RunPinRow? previous, IReadOnlyList<RunBalanceRow> source, DateTime day)
    {
        var (closedBefore, today) = Periods(store.RunWeek(pin.RunId).Period, day);
        var currency = (pin.AmountField ?? ProjectSettings.DefaultActualsAmount) == ProjectSettings.DefaultActualsAmount ? "PLN"
            : source.Where(r => r.Currency.Length > 0).GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Sum(r => (long)r.Rows)).Select(g => g.Key).FirstOrDefault();
        return new RunRuleContext(pin.PinId, previous?.PinId, pin.Project, closedBefore, today, currency, store.ProjectType(pin.Project) ?? "");
    }

    private RunRuleContext Context(RunPinRow pin, RunPinRow? previous, IReadOnlyList<RunBalanceRow> source, DateTime day) => Context(store, pin, previous, source, day);

    /// <summary>
    /// Wyniki reguły: każdy wiersz zapytania – wynik z poziomem reguły (element – RunRules.ElementOf, opis – RunRules.Describe, wiersz –
    /// Details); brak wierszy – INFO „OK”; wynik obcięty – WARNING; błąd zapytania – ERROR „reguła niepoliczona”.
    /// </summary>
    public static IReadOnlyList<RunIssue> Issues(RunRule rule, RunRuleResult result)
    {
        if (result.Error is not null)
            return [new RunIssue(rule.Name, CheckLevel.Error, null, $"Reguła niepoliczona: {result.Error} – popraw ją na ekranie Przebiegi")];
        if (result.Rows.Count == 0)
            return [new RunIssue(rule.Name, CheckLevel.Pass, null, "OK – brak nieprawidłowości")];
        var issues = result.Rows.Select(row => new RunIssue(rule.Name, rule.Level, RunRules.ElementOf(result.Columns, row)?.Value,
            RunRules.Describe(rule.ResultTemplate, result.Columns, row), null, RunRules.Details(result.Columns, row))).ToList();
        if (result.Truncated)
            issues.Add(new RunIssue(rule.Name, CheckLevel.Warning, null, $"Wynik obcięty do {RunRules.MaxRows} wierszy – zawęź zapytanie (TOP, WHERE)"));
        return issues;
    }

    /// <summary>
    /// Łączenie źródeł (docs/pipeline-fazy.md, P3): dane „ev” uruchomienia odświeżone (słowniki i Wykluczenia mogły się zmienić), koszt
    /// po elemencie WBS przypisany do WP, CAM i Cost Category jak w strukturze programu (ICostAssignment) i zapisany w uruchomieniu;
    /// suma kontrolna – koszt po połączeniu = koszt „ev” (ERROR); koszt bez przypisania – element po elemencie: WARNING w środku okresu,
    /// ERROR w przebiegu zamykającym (O33).
    /// </summary>
    private List<RunIssue> ReconcileStep(RunPinRow pin, RunStepDefinition step)
    {
        if (pin.Rows is null)
            return [new RunIssue("Przebieg", CheckLevel.Error, null, $"Etap „{step.Name}” przed przypięciem stanu – w definicji przebiegu brakuje etapu z akcją „{RunActions.Pin}”")];
        if (costs?.Invoke() is not { } assignment)
            return [new RunIssue(Coverage, CheckLevel.Error, null, "Łączenie źródeł niedostępne – moduł Projekty (struktura programu) nie jest załadowany")];
        var issues = new List<RunIssue>();
        var process = Process(pin.Project);
        var (rows, error) = Ev(store.Balance(pin.PinId), store.Exclusions(pin.Project), process, store.Dictionaries(pin.Project).CostKinds);
        store.SaveEv(pin.PinId, rows);
        if (error is not null)
            issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Error, null, $"{process.CostsCheck} niepoliczone – {error}; koszt bez narzutów"));
        var byElement = rows.GroupBy(r => r.WbsElement, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount), StringComparer.OrdinalIgnoreCase);
        var result = assignment.Assign(pin.Project, byElement);
        if (result.Note is { } note)
            issues.Add(new RunIssue(Coverage, CheckLevel.Warning, null, $"Przypisanie bez pełnych danych – {note}"));
        var targets = result.Targets.GroupBy(t => t.WbsElement, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // Element zapisany jak w danych uruchomienia (przypisanie podaje kod z nakładki – inna wielkość liter).
        var assigned = byElement.Select(e => (Target: targets.GetValueOrDefault(e.Key) is { } t ? t with { WbsElement = e.Key } : new CostTarget(e.Key, null, null, null, CostGaps.Outside),
                Amount: e.Value))
            .OrderBy(x => x.Target.WbsElement, StringComparer.OrdinalIgnoreCase).ToList();
        store.SaveCostAssignment(pin.PinId, assigned);
        var total = rows.Sum(r => r.Amount);
        var toWp = assigned.Where(x => x.Target.Wp is not null).Sum(x => x.Amount);
        var without = assigned.Where(x => x.Target.Wp is null && x.Amount != 0).ToList();
        if (Math.Abs(toWp + without.Sum(x => x.Amount) - total) > 0.01m)
            issues.Add(new RunIssue(Coverage, CheckLevel.Error, null,
                $"Suma kontrolna: koszt po połączeniu {Math.Round(toWp + without.Sum(x => x.Amount), 2):#,0.00} ≠ koszt danych uruchomienia {Math.Round(total, 2):#,0.00}"));
        var closing = store.RunWeek(pin.RunId).IsClosing;
        issues.Add(new RunIssue(Coverage, CheckLevel.Pass, null,
            $"Koszt {Math.Round(total, 2):#,0.00}{(process.Currency is { } currency ? $" {currency}" : "")}: przypisany do " +
            $"{assigned.Where(x => x.Target.Wp is not null).Select(x => x.Target.Wp!).Distinct(StringComparer.OrdinalIgnoreCase).Count()} WP – " +
            $"{Math.Round(toWp, 2):#,0.00}; bez przypisania {Math.Round(without.Sum(x => x.Amount), 2):#,0.00} ({without.Count} elementów" +
            (without.Count > 0 ? closing ? " – przebieg zamykający: przypisz albo zaakceptuj błędy z komentarzem)" : " – Kontynuuj bez tych elementów albo przypisz i wykonaj ponownie)" : ")")));
        issues.AddRange(without.OrderByDescending(x => Math.Abs(x.Amount)).Select(x => new RunIssue(Coverage, closing ? CheckLevel.Error : CheckLevel.Warning,
            x.Target.WbsElement.Length == 0 ? null : x.Target.WbsElement, $"Koszt {Math.Round(x.Amount, 2):#,0.00} bez przypisania do WP – {x.Target.Gap}", x.Amount)));
        return issues;
    }

    private ProjectProcess Process(string project) => processes?.Invoke()?.For(project) ?? ProjectProcess.Default;
}
