using System.Globalization;
using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Reguła uzgodnień w definicji przebiegu (słownik „Reguły uzgodnień”, wiersze dla typu programu).</summary>
public sealed record RunReconciliationView(string Name, string RunKind, string Condition, string Level, bool IsActive)
{
    public string State => IsActive ? "aktywna" : "wyłączona";
}

/// <summary>Wynik kontroli na ekranie Przebiegi z zakresem okna „Zmiany ACWP” (null – akcja „Pokaż zmiany” niedostępna).</summary>
public sealed record RunIssueView(RunIssueRow Issue, AcwpChangesRequest? Changes)
{
    public bool CanShowChanges => Changes is not null;
}

/// <summary>Kolumna macierzy kontroli: uruchomienie przebiegu (kod tygodnia i numer, start, stan).</summary>
public sealed record RunMatrixColumn(long PinId, string Label, string Detail);

/// <summary>Komórka macierzy: wynik kontroli w uruchomieniu – najgorszy poziom i liczby wyników; Level null – kontrola bez wyników.</summary>
public sealed record RunMatrixCell(long PinId, string CheckName, CheckLevel? Level, int Errors, int Warnings, int Passes)
{
    public string Text => Errors > 0 ? $"{Errors} ERROR" : Warnings > 0 ? $"{Warnings} WARNING" : Passes > 0 ? "OK" : "–";

    public bool HasIssues => Errors + Warnings + Passes > 0;
}

/// <summary>Wiersz macierzy: kontrola i jej wyniki w kolejnych uruchomieniach (od najstarszego).</summary>
public sealed record RunMatrixRow(string CheckName, IReadOnlyList<RunMatrixCell> Cells);

/// <summary>Macierz kontrola × uruchomienia przebiegu programu.</summary>
public sealed record RunMatrix(IReadOnlyList<RunMatrixColumn> Columns, IReadOnlyList<RunMatrixRow> Rows);

/// <summary>
/// Wiersz definicji przebiegu programu (zakładka „Definicja przebiegu”): etap (numer, nazwa, akcja, bramka) i element kontrolny
/// z ustawieniem (aktywny, poziom, próg albo warunek reguły uzgodnień) i źródłem ustawienia.
/// </summary>
public sealed record RunWorkflowRow(int StepNo, string Step, string Action, string Gate, string Element, string Check, string Setting, string Source,
    string Description);

/// <summary>Program na przeglądzie przebiegów: ostatnie uruchomienie, etap, na którym stoi, liczby wyników.</summary>
public sealed record RunProgramRow(string Code, string Name, string ProjectType, string? Run, string? Status, string? Stop, int Errors, int Warnings,
    DateTimeOffset? StartedAt, string? StartedBy);

/// <summary>
/// Ekran Przebiegi (docs/funkcjonalnosc.md, F06): macierz wyników kontroli kolejnych uruchomień programu, definicja przebiegu
/// programu (etapy i elementy kontrolne z ustawieniami), przegląd programów, reguły uzgodnień, zakres okna „Zmiany ACWP” wyniku.
/// </summary>
public static class RunOverview
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Kontrola etapów przebiegu spoza katalogu reguł (import, przerwanie).</summary>
    private const string Import = "Import";
    private const string Run = "Przebieg";

    /// <summary>
    /// Macierz z uruchomień (dowolna kolejność – kolumny od najstarszego) i ich wyników. Wiersze: import, kontrole procesu
    /// programu (np. koszty po narzutach CAS), kontrole katalogu w jego kolejności, przerwanie przebiegu.
    /// </summary>
    public static RunMatrix Matrix(IReadOnlyList<RunPinRow> pins, IReadOnlyDictionary<long, IReadOnlyList<RunIssueRow>> issues)
    {
        var ordered = pins.OrderBy(p => p.StartedAt).ToList();
        var columns = ordered.Select(p => new RunMatrixColumn(p.PinId, $"{Week(p.Code)} #{p.PinNo}",
            $"{p.Code} #{p.PinNo}, {p.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}, {p.StartedBy} – {p.Status}")).ToList();
        var catalog = RunCheckRules.All.Select(r => r.Check).Distinct().ToList();
        int Rank(string check) => check == Import ? 0 : check == Run ? 1000 : check == RunReconciliations.CheckName ? 900
            : catalog.IndexOf(check) is var i and >= 0 ? 10 + i : 5;
        var checks = catalog.Concat(issues.Values.SelectMany(l => l).Select(i => i.CheckName)).Distinct()
            .OrderBy(Rank).ThenBy(c => c, StringComparer.CurrentCulture).ToList();
        var rows = checks.Select(check => new RunMatrixRow(check, ordered.Select(pin =>
        {
            var found = issues.GetValueOrDefault(pin.PinId)?.Where(i => i.CheckName == check).ToList() ?? [];
            CheckLevel? level = found.Count == 0 ? null : found.Max(i => i.Level);
            return new RunMatrixCell(pin.PinId, check, level, found.Count(i => i.Level == CheckLevel.Error),
                found.Count(i => i.Level == CheckLevel.Warning), found.Count(i => i.Level == CheckLevel.Pass));
        }).ToList())).ToList();
        return new RunMatrix(columns, rows);
    }

    /// <summary>
    /// Definicja przebiegu jako wiersze: etap bez elementów – jeden wiersz (akcja); element katalogu – ustawienie etapu
    /// (RunWorkflowEngine.Settings: element, słownik „Kontrole przebiegu”, katalog); „Uzgodnienie: *” – reguły uzgodnień typu
    /// programu i wariantu; reguła uzgodnień – warunek, poziom i tolerancja.
    /// </summary>
    public static IReadOnlyList<RunWorkflowRow> Workflow(RunDefinition definition, IReadOnlyDictionary<string, RunCheckSetting> defaults,
        IReadOnlyList<RunReconciliationRow> library, string projectType, bool isClosing)
    {
        var rows = new List<RunWorkflowRow>();
        foreach (var step in definition.Steps)
        {
            var gate = step.NeedsApproval ? "zatwierdzenie ręczne" : step.Action == RunActions.Checks ? $"ERROR – stop; WARNING – {step.WarningGate.ToLowerInvariant()}" : "ERROR – stop";
            RunWorkflowRow Row(string element, string check, string setting, string source, string? description) =>
                new(step.No, step.Name, step.Action, gate, element, check, setting, source, description ?? "");
            if (step.Elements.Count == 0)
            {
                rows.Add(Row("–", "", "", "", step.Description));
                continue;
            }
            var settings = RunWorkflowEngine.Settings(step, defaults, definition.BuiltIn);
            foreach (var element in step.Elements)
            {
                if (RunCheckRules.ByName(element.Element) is { } rule)
                    rows.Add(Row(rule.Name, rule.Check, Show(settings[rule.Code]),
                        element.Level is not null || element.Threshold is not null || !element.Active ? "definicja" : "domyślne", rule.Description));
                else if (RunDefinitions.IsReconciliation(element.Element))
                {
                    var (rules, missing) = RunWorkflowEngine.Reconciliations(step with { Elements = [element] }, library, projectType, isClosing);
                    rows.AddRange(rules.Select(r => Row(RunDefinitions.ReconciliationPrefix + r.Name, RunReconciliations.CheckName,
                        $"{RunReconciliations.Condition(r)} – {r.Level}", element.Element.Trim() == RunDefinitions.AllReconciliations ? "reguły uzgodnień" : "definicja", null)));
                    rows.AddRange(missing.Select(m => Row(RunDefinitions.ReconciliationPrefix + m, RunReconciliations.CheckName, "brak reguły w słowniku", "definicja", null)));
                    if (!element.Active)
                        rows.Add(Row(element.Element, RunReconciliations.CheckName, "wyłączona", "definicja", null));
                    else if (rules.Count == 0 && missing.Count == 0)
                        rows.Add(Row(element.Element, RunReconciliations.CheckName, "brak aktywnych reguł uzgodnień dla typu programu", "reguły uzgodnień", null));
                }
                else
                    rows.Add(Row(element.Element, "", "nieznany element", "definicja", null));
            }
        }
        return rows;
    }

    /// <summary>Przegląd przebiegów: programy z ostatnim uruchomieniem i etapem, na którym stoi.</summary>
    public static IReadOnlyList<RunProgramRow> Programs(IReadOnlyList<RunProjectRow> projects, IReadOnlyDictionary<string, RunPinRow> latest,
        IReadOnlyDictionary<long, IReadOnlyList<RunStepRow>> steps) =>
        projects.Select(p =>
        {
            if (!latest.TryGetValue(p.Code, out var pin))
                return new RunProgramRow(p.Code, p.Name, p.ProjectType, null, null, null, 0, 0, null, null);
            var stop = steps.GetValueOrDefault(pin.PinId)?.OrderBy(s => s.StepNo).FirstOrDefault(s => s.Status != RunStepStatuses.Done);
            return new RunProgramRow(p.Code, p.Name, p.ProjectType, $"{pin.Code} #{pin.PinNo}", pin.Status,
                stop is null ? steps.ContainsKey(pin.PinId) ? "wszystkie etapy zakończone" : null : $"{stop.StepNo}. {stop.Name} – {stop.Status.ToLowerInvariant()}",
                pin.Errors, pin.Warnings, pin.StartedAt, pin.StartedBy);
        }).ToList();

    /// <summary>Kontrole kosztów, dla których okno „Zmiany ACWP” pokazuje przyczynę.</summary>
    private static readonly HashSet<string> CostChecks =
    [
        RunCheckRules.Volume, RunCheckRules.Deviations, RunCheckRules.NewCostElements, RunCheckRules.NetZero, RunCheckRules.ClosedPeriods,
        RunCheckRules.Quality, RunCheckRules.CostKinds, RunReconciliations.CheckName,
    ];

    /// <summary>
    /// Zakres okna „Zmiany ACWP” dla wyniku kontroli uruchomienia: element – rodzaj kosztu (Rodzaje kosztu), cost element (Nowe cost
    /// elementy, odchylenia po cost elemencie) albo element WBS (pozostałe kontrole kosztów); bez elementu – cały program. Para
    /// uruchomień jak w przebiegu (uruchomienie wyniku i poprzednie). Kontrole spoza kosztów (import, słowniki) – null.
    /// </summary>
    public static AcwpChangesRequest? ChangesFor(RunIssueRow issue, string project)
    {
        if (!CostChecks.Contains(issue.CheckName))
            return null;
        var element = issue.Element;
        if (string.IsNullOrEmpty(element) || issue.CheckName == RunReconciliations.CheckName)
            return new(project, PinId: issue.PinId, Scope: issue.CheckName);
        if (RunMeasures.Kinds.Contains(element))
            return new(project, Kind: element, PinId: issue.PinId, Scope: $"{issue.CheckName}: {element}");
        if (issue.CheckName == RunCheckRules.NewCostElements || issue.CheckName == RunCheckRules.Deviations && issue.Message.StartsWith("cost element"))
            return new(project, CostElement: element, PinId: issue.PinId, Scope: $"cost element {element}");
        return new(project, WbsElements: [element], PinId: issue.PinId, Scope: $"element WBS {element}");
    }

    /// <summary>Reguły uzgodnień dla typu programu (oba warianty przebiegu).</summary>
    public static IReadOnlyList<RunReconciliationView> Reconciliations(IEnumerable<RunReconciliationRow> rows, string projectType) =>
        RunReconciliations.Applicable(rows, projectType, isClosing: null)
            .Select(r => new RunReconciliationView(r.Name, r.RunKind, RunReconciliations.Condition(r), r.Level, r.IsActive)).ToList();

    /// <summary>Opis ustawienia: wyłączona albo poziom i próg.</summary>
    public static string Show(RunCheckSetting setting) =>
        !setting.Active ? "wyłączona"
            : (setting.Level switch { CheckLevel.Error => "ERROR", CheckLevel.Warning => "WARNING", _ => "INFO" })
              + (setting.Threshold is { } t ? $", próg {t.ToString("#,0.00", Pl)}" : "");

    // „R-PAF-2026-10-T41” → „2026-10 T41”.
    private static string Week(string code)
    {
        var parts = code.Split('-');
        return parts.Length >= 5 ? $"{parts[^3]}-{parts[^2]} {parts[^1]}" : code;
    }
}
