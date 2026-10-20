using PzlEv.Modules.Runs.Data;
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
public sealed class RunService(IRunStore store, AppServices services, Func<ISourceImport?>? imports = null, Func<IProjectProcessSource?>? processes = null)
{
    public const string Area = "Przebiegi";

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
                issues = ExecuteStep(store.Pin(pin.PinId)!, step, progress, cancellation);
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

    /// <summary>Akcja etapu: import źródeł, przypięcie stanu, kontrole (elementy kontrolne etapu), zatwierdzenie (bez wykonania).</summary>
    private List<RunIssue> ExecuteStep(RunPinRow pin, RunStepDefinition step, IProgress<string>? progress, CancellationToken cancellation) =>
        step.Action switch
        {
            RunActions.Import => ImportStep(pin, progress, cancellation),
            RunActions.Pin => PinStep(pin),
            RunActions.Checks => ChecksStep(pin, step),
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
        return issues;
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
    // wobec poprzedniego przebiegu z tym samym polem kwoty.
    private List<RunIssue> ChecksStep(RunPinRow pin, RunStepDefinition step)
    {
        if (pin.Rows is null)
            return [new RunIssue("Przebieg", CheckLevel.Error, null, $"Etap „{step.Name}” przed przypięciem stanu – w definicji przebiegu brakuje etapu z akcją „{RunActions.Pin}” przed kontrolami")];
        var issues = new List<RunIssue>();
        var (rules, missing) = RunWorkflowEngine.Rules(step, store.Rules());
        if (rules.Count > 0)
        {
            var process = Process(pin.Project);
            var source = store.Balance(pin.PinId);
            if (SaveEv(pin, process, source) is { } error)
                issues.Add(new RunIssue(process.CostsCheck, CheckLevel.Warning, null, $"{process.CostsCheck} niepoliczone – {error}; tabela „ev” bez narzutów"));
            var previous = Comparable(store, pin);
            if (previous is not null && !store.HasEv(previous.PinId))
                SaveEv(previous, process, store.Balance(previous.PinId));
            var context = Context(pin, previous, source, services.Clock.Now.Date);
            foreach (var rule in rules)
                issues.AddRange(Issues(rule, store.RunRule(rule.Sql, context)));
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

    private ProjectProcess Process(string project) => processes?.Invoke()?.For(project) ?? ProjectProcess.Default;
}
