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

    /// <summary>Potwierdzenie etapu wymagającego akcji (WARNING przy bramce, zatwierdzenie ręczne) z komentarzem – dziennik; potem dalsze etapy.</summary>
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
            store.SetStep(pinId, stepNo, RunStepStatuses.Done, step.StartedAt, step.FinishedAt ?? at, services.User.Account, step.Errors, step.Warnings,
                $"{at.ToLocalTime():yyyy-MM-dd HH:mm} {services.User.Account}: {comment.Trim()}");
            services.Journal.Add(Area, $"Przebieg {pin.Code} #{pin.PinNo}: etap {stepNo} „{step.Name}” potwierdzony – {comment.Trim()}", pin.Code);
            return Advance(pin, progress, cancellation);
        });

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

    private RunOutcome WithPin(long pinId, Func<RunPinRow, RunOutcome> action)
    {
        var pin = store.Pin(pinId) ?? throw new InvalidOperationException($"Nie ma uruchomienia przebiegu {pinId}.");
        return Locked(pin.Project, () => action(store.Pin(pinId)!));
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
            progress?.Report($"Etap {step.No}/{definition.Steps.Count}: {step.Name}…");
            List<RunIssue> issues;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                issues = ExecuteStep(store.Pin(pin.PinId)!, step, definition, progress, cancellation);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.Error(ex, "Przebieg {Code} #{Pin}: etap {Step} przerwany", pin.Code, pin.PinNo, step.No);
                var reason = cancellation.IsCancellationRequested ? "przerwany przez użytkownika" : $"{ex.GetType().Name}: {ex.Message}";
                issues = [new RunIssue("Przebieg", CheckLevel.Error, null, $"Etap „{step.Name}” przerwany: {reason}")];
            }
            var errors = issues.Count(i => i.Level == CheckLevel.Error);
            var warnings = issues.Count(i => i.Level == CheckLevel.Warning);
            var status = RunWorkflowEngine.Gate(step, errors, warnings);
            store.ReplaceStepIssues(pin.PinId, step.No, issues.Select(i => (i.Check, i.Level, i.Element, i.Message, i.Amount)).ToList());
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

    /// <summary>Akcja etapu: import źródeł, przypięcie stanu, kontrole (elementy kontrolne etapu), zatwierdzenie (bez wykonania).</summary>
    private List<RunIssue> ExecuteStep(RunPinRow pin, RunStepDefinition step, RunDefinition definition, IProgress<string>? progress, CancellationToken cancellation) =>
        step.Action switch
        {
            RunActions.Import => ImportStep(pin, progress, cancellation),
            RunActions.Pin => PinStep(pin),
            RunActions.Checks => ChecksStep(pin, step, definition),
            _ => [],
        };

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
            .. imported.Issues.Where(i => i.Level == CheckLevel.Error).Take(RunChecks.Top).Select(i => new RunIssue("Import", CheckLevel.Error, i.Element, i.Message)),
        ];
    }

    // Przypięcie stanu: dane ACTUALS programu w chwili przebiegu (pole kwoty procesu programu), porównanie z poprzednim przebiegiem,
    // koszty procesu programu (np. CAS – po narzutach).
    private List<RunIssue> PinStep(RunPinRow pin)
    {
        var issues = new List<RunIssue>();
        var process = Process(pin.Project);
        var amountField = process.AmountField(store.ActualsAmount(pin.Project));
        var source = store.PinBalance(pin.Project, pin.PinId, amountField);
        var previousPin = store.Previous(pin.Project, pin.RunId);
        var previousField = previousPin?.AmountField ?? ProjectSettings.DefaultActualsAmount;
        if (previousPin is not null && previousField != amountField)
            issues.Add(new RunIssue(RunChecks.Volume, CheckLevel.Warning, null,
                $"Pole kwoty ACTUALS zmienione ({previousField} → {amountField}, „Parametry programu”) – bez porównania z przebiegiem {previousPin.Code} #{previousPin.PinNo}"));
        else if (previousPin is not null)
            issues.Add(new RunIssue(RunChecks.Volume, CheckLevel.Pass, null, $"Porównanie z przebiegiem {previousPin.Code} #{previousPin.PinNo} ({previousPin.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm})"));
        var current = source;
        if (process.TransformsCosts)
        {
            var (transformed, error) = Transformed(process, source);
            if (error is null)
            {
                current = transformed;
                issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Pass, null,
                    $"{process.CostsCheck} w {process.Currency}: koszt brutto {Math.Round(RunChecks.GrossCost(current), 2):#,0.00}"));
            }
            else
                issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Error, null, $"{process.CostsCheck} niepoliczone – {error}; kontrole na danych źródłowych"));
        }
        var (closedBefore, _) = Periods(store.RunWeek(pin.RunId).Period, services.Clock.Now.Date);
        var balance = source.Where(r => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(closedBefore) < 0).Sum(r => r.Amount);
        store.SetPinBalance(pin.PinId, source.Sum(r => (long)r.Rows), RunChecks.GrossCost(current), balance);
        issues.Add(new RunIssue(RunChecks.Volume, CheckLevel.Pass, null,
            $"Przypięto {source.Sum(r => (long)r.Rows):#,0} wierszy ACTUALS ({amountField}); saldo okresów zamkniętych {Math.Round(balance, 2):#,0.00}"));
        return issues;
    }

    // Kontrole etapu: reguły katalogu z elementów etapu (ustawienia – element, słownik „Kontrole przebiegu”, katalog) i reguły
    // uzgodnień etapu na danych przypiętych w uruchomieniu (koszty procesu programu; netowanie do 0 – na danych źródłowych).
    private List<RunIssue> ChecksStep(RunPinRow pin, RunStepDefinition step, RunDefinition definition)
    {
        var source = store.Balance(pin.PinId);
        if (pin.Rows is null)
            return [new RunIssue("Przebieg", CheckLevel.Error, null, $"Etap „{step.Name}” przed przypięciem stanu – w definicji przebiegu brakuje etapu z akcją „{RunActions.Pin}” przed kontrolami")];
        var process = Process(pin.Project);
        var amountField = pin.AmountField ?? ProjectSettings.DefaultActualsAmount;
        var previousPin = store.Previous(pin.Project, pin.RunId);
        var previous = previousPin is null || (previousPin.AmountField ?? ProjectSettings.DefaultActualsAmount) != amountField ? null : store.Balance(previousPin.PinId);
        var current = source;
        if (process.TransformsCosts && Transformed(process, source) is { Error: null } transformed)
        {
            current = transformed.Rows;
            previous = previous is null ? null : Transformed(process, previous) is { Error: null } before ? before.Rows : null;
        }
        var week = store.RunWeek(pin.RunId);
        var (closedBefore, today) = Periods(week.Period, services.Clock.Now.Date);
        var projectType = store.ProjectType(pin.Project) ?? "";
        var settings = RunWorkflowEngine.Settings(step, RunChecks.Settings(store.CheckSettings(), projectType, week.IsClosing), definition.BuiltIn);
        var dictionaries = store.Dictionaries(pin.Project, today.Year);
        var issues = RunChecks.Check(current, previous, store.CostElements(pin.Project), closedBefore, today,
            current != source ? process.Currency : amountField == ProjectSettings.DefaultActualsAmount ? "PLN" : null, source, settings, dictionaries).ToList();
        var (rules, missing) = RunWorkflowEngine.Reconciliations(step, store.Reconciliations(), projectType, week.IsClosing);
        if (rules.Count > 0)
            issues.AddRange(RunReconciliations.CheckRules(rules, RunReconciliations.Measures(current, source, previous, closedBefore, today, dictionaries)));
        issues.AddRange(missing.Select(name => new RunIssue(RunReconciliations.CheckName, CheckLevel.Warning, name,
            $"Reguła uzgodnień „{name}” z definicji przebiegu nie istnieje w słowniku „Reguły uzgodnień” – niesprawdzona")));
        return issues;
    }

    private ProjectProcess Process(string project) => processes?.Invoke()?.For(project) ?? ProjectProcess.Default;
}
