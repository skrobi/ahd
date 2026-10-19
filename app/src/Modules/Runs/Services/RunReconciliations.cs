using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Runs.Services;

/// <summary>
/// Reguły uzgodnień przebiegu (docs/pipeline-fazy.md, rozdz. 4.1): wartości miar programu (RunMeasures) z danych przebiegu
/// i słowników oraz sprawdzenie reguł słownika „Reguły uzgodnień” dla typu programu i wariantu przebiegu – kontrola
/// „Reguły uzgodnień”, element – nazwa reguły.
/// </summary>
public static class RunReconciliations
{
    public const string CheckName = "Reguły uzgodnień";

    /// <summary>
    /// Miary programu: current – koszty przebiegu (CAS – po narzutach), source – dane źródłowe ACTUALS (null – jak current),
    /// previous – poprzedni przebieg (null – miary poprzedniego bez wartości), dictionaries – słowniki (null – miary słowników bez wartości).
    /// </summary>
    public static IReadOnlyDictionary<string, decimal?> Measures(IReadOnlyList<RunBalanceRow> current, IReadOnlyList<RunBalanceRow>? source,
        IReadOnlyList<RunBalanceRow>? previous, (int Year, int Period) closedBefore, (int Year, int Period) today, RunDictionaryData? dictionaries)
    {
        var data = source ?? current;
        bool Closed(RunBalanceRow r) => (r.FiscalYear, Math.Min(r.Period, 12)).CompareTo(closedBefore) < 0;
        var wps = dictionaries?.Wps.GroupBy(w => w.Wp, StringComparer.OrdinalIgnoreCase).ToList();
        var kinds = dictionaries?.CostKinds;
        var totals = RunCostKinds.Totals(current, kinds);
        var measures = new Dictionary<string, decimal?>
        {
            [RunMeasures.GrossCost.Name] = RunChecks.GrossCost(current),
            [RunMeasures.Settlement.Name] = data.Where(r => r.IsSettlement).Sum(r => r.Amount),
            [RunMeasures.ClosedBalance.Name] = data.Where(Closed).Sum(r => r.Amount),
            [RunMeasures.CurrentPeriodCost.Name] = RunChecks.GrossCost(current.Where(r => (r.FiscalYear, Math.Min(r.Period, 12)) == today)),
            [RunMeasures.PreviousGrossCost.Name] = previous is null ? null : RunChecks.GrossCost(previous),
            [RunMeasures.Rows.Name] = data.Sum(r => (long)r.Rows),
            [RunMeasures.PreviousRows.Name] = previous?.Sum(r => (long)r.Rows),
            [RunMeasures.NoWbsCost.Name] = RunChecks.GrossCost(current.Where(r => r.WbsElement.Length == 0)),
            [RunMeasures.Bac.Name] = dictionaries?.Budget?.Bac,
            [RunMeasures.BacHours.Name] = dictionaries?.Budget?.BacHours,
            [RunMeasures.BacMaterial.Name] = dictionaries?.Budget?.BacMaterial,
            [RunMeasures.WpCount.Name] = wps?.Count,
            [RunMeasures.WpWithoutBudgetCount.Name] = wps?.Count(g => !g.Any(w => w.HasBudget)),
        };
        foreach (var kind in RunMeasures.Kinds)
        {
            measures[RunMeasures.KindAcwp(kind)] = totals[kind];
            measures[RunMeasures.KindIncrement(kind)] = RunCostKinds.InPeriod(current, kinds, kind, today);
            measures[RunMeasures.KindAverageIncrement(kind)] = RunCostKinds.AverageBefore(current, kinds, kind, today, RunCheckRules.IncrementPeriods);
        }
        return measures;
    }

    /// <summary>Reguły słownika dla typu programu („Wszystkie” albo typ) i wariantu przebiegu („Każdy” albo wariant), w kolejności nazw.</summary>
    public static IReadOnlyList<RunReconciliationRow> Applicable(IEnumerable<RunReconciliationRow> rows, string projectType, bool? isClosing)
    {
        static bool Is(string? value, string expected) => string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        return rows.Where(r => Is(r.ProjectType, projectType) || Is(r.ProjectType, RunCheckRules.AllTypes))
            .Where(r => isClosing is null || Is(r.RunKind, RunCheckRules.AnyRun) || Is(r.RunKind, isClosing.Value ? RunCheckRules.ClosingRun : RunCheckRules.MidPeriodRun))
            .OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>
    /// Wyniki aktywnych reguł: warunek spełniony – INFO z wartościami stron, niespełniony – poziom reguły; formuła niepoprawna albo
    /// miara bez wartości / dzielenie przez 0 – WARNING (reguła niesprawdzona).
    /// </summary>
    public static IReadOnlyList<RunIssue> Check(IEnumerable<RunReconciliationRow> rows, string projectType, bool isClosing,
        IReadOnlyDictionary<string, decimal?> measures) =>
        CheckRules(Applicable(rows, projectType, isClosing), measures);

    /// <summary>Wyniki aktywnych reguł z listy (reguły etapu przebiegu – RunWorkflowEngine.Reconciliations).</summary>
    public static IReadOnlyList<RunIssue> CheckRules(IEnumerable<RunReconciliationRow> rules, IReadOnlyDictionary<string, decimal?> measures)
    {
        var issues = new List<RunIssue>();
        foreach (var rule in rules.Where(r => r.IsActive))
        {
            var (left, leftError) = Side(rule.LeftFormula, measures);
            var (right, rightError) = Side(rule.RightFormula, measures);
            if (leftError is not null || rightError is not null)
            {
                issues.Add(new RunIssue(CheckName, CheckLevel.Warning, rule.Name,
                    $"{rule.Name}: reguła niesprawdzona – {leftError ?? rightError} ({Condition(rule)})"));
                continue;
            }
            var tolerance = rule.Tolerance ?? 0m;
            var holds = RunMeasures.Holds(rule.Comparison, left!.Value, right!.Value, tolerance);
            var level = holds ? CheckLevel.Pass : RunCheckRules.LevelOf(rule.Level) ?? CheckLevel.Error;
            issues.Add(new RunIssue(CheckName, level, rule.Name,
                $"{rule.Name}: {(holds ? "spełniona" : "niespełniona")} – {rule.LeftFormula.Trim()} = {Amount(left.Value)}, {rule.RightFormula.Trim()} = {Amount(right.Value)}, " +
                $"warunek {rule.Comparison.Trim()}{(tolerance != 0 ? $" (tolerancja {Amount(tolerance)})" : "")}", left.Value - right.Value));
        }
        return issues;
    }

    public static string Condition(RunReconciliationRow rule) =>
        $"{rule.LeftFormula.Trim()} {rule.Comparison.Trim()} {rule.RightFormula.Trim()}" + (rule.Tolerance is { } t and not 0 ? $" ± {Amount(t)}" : "");

    private static (decimal? Value, string? Error) Side(string text, IReadOnlyDictionary<string, decimal?> measures)
    {
        var (formula, error) = RunFormula.Parse(text);
        return formula is null ? (null, $"formuła „{text}”: {error}") : formula.Evaluate(name => measures.GetValueOrDefault(name));
    }

    private static string Amount(decimal value) => PolishNumber.ToDisplay(Math.Round(value, 2));
}
