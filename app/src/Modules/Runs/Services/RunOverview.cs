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
/// Wiersz porównania dwóch uruchomień: wyniki kontroli o tej samej kontroli i elemencie – poziom (najgorszy), liczba i kwota (suma)
/// „było” i „jest”; Status – nowy, rozwiązany, zmieniony, bez zmian; Issue – wynik „jest” (albo „było”) do „Pokaż zmiany”.
/// </summary>
public sealed record RunCompareRow(string CheckName, string? Element, string Status, CheckLevel? LevelBefore, CheckLevel? LevelAfter,
    decimal? AmountBefore, decimal? AmountAfter, string Message, RunIssueView Issue)
{
    public const string New = "nowy";
    public const string Resolved = "rozwiązany";
    public const string Changed = "zmieniony";
    public const string Same = "bez zmian";

    public decimal? Difference => AmountBefore is null && AmountAfter is null ? null : (AmountAfter ?? 0) - (AmountBefore ?? 0);

    public CheckLevel? Level => LevelAfter ?? LevelBefore;
}

/// <summary>
/// Przebieg programu (zakładka Przebieg programu – docs/funkcjonalnosc.md, F06): macierz wyników kontroli kolejnych uruchomień,
/// porównanie dwóch uruchomień, zakres okna „Zmiany ACWP” wyniku; reguły uzgodnień i ustawienia kontroli (ekran Przebiegi).
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
    /// Porównanie wyników kontroli dwóch uruchomień (before – „było”, after – „jest”) po kontroli i elemencie: tylko w „jest” – nowy,
    /// tylko w „było” – rozwiązany, inny najgorszy poziom, liczba wyników albo kwota (różnica ≥ 0,01) – zmieniony, inaczej bez zmian.
    /// Kolejność: od najgorszego poziomu, nowe i zmienione przed rozwiązanymi i bez zmian.
    /// </summary>
    public static IReadOnlyList<RunCompareRow> Compare(IReadOnlyList<RunIssueRow> before, IReadOnlyList<RunIssueRow> after, string project)
    {
        static (string, string) Key(RunIssueRow i) => (i.CheckName, i.Element ?? "");
        var was = before.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        var now = after.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        static decimal? Sum(List<RunIssueRow>? rows) => rows is null || rows.All(r => r.Amount is null) ? null : rows.Sum(r => r.Amount ?? 0);
        static RunIssueRow Worst(List<RunIssueRow> rows) => rows.OrderByDescending(r => r.Level).ThenBy(r => r.Id).First();
        var rows = was.Keys.Union(now.Keys).Select(key =>
        {
            var b = was.GetValueOrDefault(key);
            var a = now.GetValueOrDefault(key);
            var status = b is null ? RunCompareRow.New : a is null ? RunCompareRow.Resolved
                : Worst(a).Level != Worst(b).Level || a.Count != b.Count || Math.Abs((Sum(a) ?? 0) - (Sum(b) ?? 0)) >= 0.01m ? RunCompareRow.Changed
                : RunCompareRow.Same;
            var shown = Worst(a ?? b!);
            return new RunCompareRow(key.Item1, key.Item2.Length == 0 ? null : key.Item2, status, b is null ? null : Worst(b).Level,
                a is null ? null : Worst(a).Level, Sum(b), Sum(a), (a ?? b)!.Count > 1 ? $"{shown.Message} (+{(a ?? b)!.Count - 1})" : shown.Message,
                new RunIssueView(shown, ChangesFor(shown, project)));
        });
        static int Order(string status) => status switch { RunCompareRow.New => 0, RunCompareRow.Changed => 1, RunCompareRow.Resolved => 2, _ => 3 };
        return rows.OrderByDescending(r => r.Level).ThenBy(r => Order(r.Status)).ThenBy(r => r.CheckName, StringComparer.CurrentCulture)
            .ThenBy(r => r.Element, StringComparer.CurrentCulture).ToList();
    }

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
