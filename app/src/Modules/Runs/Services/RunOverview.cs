using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Runs.Services;

/// <summary>Wynik kontroli na ekranie Przebiegi z zakresem okna „Zmiany ACWP” (null – akcja „Pokaż zmiany” niedostępna).</summary>
public sealed record RunIssueView(RunIssueRow Issue, AcwpChangesRequest? Changes)
{
    public bool CanShowChanges => Changes is not null;

    /// <summary>Poziom wyniku: ERROR, WARNING, INFO (wiersz reguły INFO albo informacja), OK (reguła bez nieprawidłowości).</summary>
    public string LevelText => RunOverview.LevelText(Issue);
}

/// <summary>Kolumna macierzy kontroli: uruchomienie przebiegu (kod tygodnia i numer, start, stan).</summary>
public sealed record RunMatrixColumn(long PinId, string Label, string Detail);

/// <summary>
/// Komórka macierzy: wynik kontroli w uruchomieniu – najgorszy poziom i liczby wyników (Infos – wiersze reguł z poziomem INFO);
/// Level null – kontrola bez wyników.
/// </summary>
public sealed record RunMatrixCell(long PinId, string CheckName, CheckLevel? Level, int Errors, int Warnings, int Passes, int Infos = 0)
{
    public string Text => Errors > 0 ? $"{Errors} ERROR" : Warnings > 0 ? $"{Warnings} WARNING" : Infos > 0 ? $"{Infos} INFO" : Passes > 0 ? "OK" : "–";

    public bool HasIssues => Errors + Warnings + Passes > 0;
}

/// <summary>Wiersz macierzy: kontrola (reguła walidacji albo kontrola etapu) i jej wyniki w kolejnych uruchomieniach (od najstarszego).</summary>
public sealed record RunMatrixRow(string CheckName, IReadOnlyList<RunMatrixCell> Cells);

/// <summary>Macierz kontrola × uruchomienia przebiegu programu.</summary>
public sealed record RunMatrix(IReadOnlyList<RunMatrixColumn> Columns, IReadOnlyList<RunMatrixRow> Rows);

/// <summary>
/// Wiersz porównania dwóch uruchomień: wyniki tej samej kontroli (reguły) o tych samych wartościach kolumn tekstowych wyniku (Element –
/// te wartości) – poziom (najgorszy) „było” i „jest”, różnice w kolumnach liczbowych (Changes); Status – nowy, rozwiązany, zmieniony,
/// bez zmian; Issue – wynik „jest” (albo „było”) do „Pokaż zmiany”.
/// </summary>
public sealed record RunCompareRow(string CheckName, string? Element, string Status, CheckLevel? LevelBefore, CheckLevel? LevelAfter, string Changes,
    string Message, RunIssueView Issue)
{
    public const string New = "nowy";
    public const string Resolved = "rozwiązany";
    public const string Changed = "zmieniony";
    public const string Same = "bez zmian";

    public CheckLevel? Level => LevelAfter ?? LevelBefore;
}

/// <summary>
/// Przebieg programu (zakładka Przebieg programu – docs/funkcjonalnosc.md, F06): macierz wyników reguł kolejnych uruchomień,
/// porównanie dwóch uruchomień, zakres okna „Zmiany ACWP” wyniku.
/// </summary>
public static class RunOverview
{
    /// <summary>Kontrole etapów przebiegu spoza reguł walidacji (import, przerwanie).</summary>
    private const string Import = "Import";
    private const string Run = "Przebieg";

    /// <summary>
    /// Macierz z uruchomień (dowolna kolejność – kolumny od najstarszego) i ich wyników. Wiersze: import, przypięcie stanu, inne
    /// kontrole etapów (np. koszty po narzutach CAS), reguły walidacji w kolejności rules (reguły spoza listy – po nazwie), przerwanie
    /// przebiegu.
    /// </summary>
    public static RunMatrix Matrix(IReadOnlyList<RunPinRow> pins, IReadOnlyDictionary<long, IReadOnlyList<RunIssueRow>> issues, IReadOnlyList<string>? rules = null)
    {
        var ordered = pins.OrderBy(p => p.StartedAt).ToList();
        var columns = ordered.Select(p => new RunMatrixColumn(p.PinId, $"{Week(p.Code)} #{p.PinNo}",
            $"{p.Code} #{p.PinNo}, {p.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}, {p.StartedBy} – {p.Status}")).ToList();
        var order = (rules ?? []).ToList();
        int Rank(string check) => check == Import ? 0 : check == RunService.Pinned ? 1 : check == Run ? 1000
            : order.IndexOf(check) is var i and >= 0 ? 10 + i : 500;
        var checks = issues.Values.SelectMany(l => l).Select(i => i.CheckName).Distinct()
            .OrderBy(Rank).ThenBy(c => c, StringComparer.CurrentCulture).ToList();
        var rows = checks.Select(check => new RunMatrixRow(check, ordered.Select(pin =>
        {
            var found = issues.GetValueOrDefault(pin.PinId)?.Where(i => i.CheckName == check).ToList() ?? [];
            CheckLevel? level = found.Count == 0 ? null : found.Max(i => i.Level);
            return new RunMatrixCell(pin.PinId, check, level, found.Count(i => i.Level == CheckLevel.Error),
                found.Count(i => i.Level == CheckLevel.Warning), found.Count(i => i.Level == CheckLevel.Pass),
                found.Count(i => i.Level == CheckLevel.Pass && i.Details is not null));
        }).ToList())).ToList();
        return new RunMatrix(columns, rows);
    }

    /// <summary>Poziom wyniku do wyświetlenia: wiersz reguły INFO – INFO, inny wynik PASS – OK.</summary>
    public static string LevelText(RunIssueRow issue) => issue.Level switch
    {
        CheckLevel.Error => "ERROR",
        CheckLevel.Warning => "WARNING",
        _ => issue.Details is null ? "OK" : "INFO",
    };

    /// <summary>
    /// Porównanie wyników dwóch uruchomień (before – „było”, after – „jest”): wiersze wyników tej samej kontroli dopasowane po wartościach
    /// kolumn tekstowych wyniku reguły (Details; wynik bez wiersza – po elemencie albo opisie); tylko w „jest” – nowy, tylko w „było” –
    /// rozwiązany, inny poziom, liczba wyników albo wartość kolumny liczbowej (różnica ≥ 0,01) – zmieniony, inaczej bez zmian. Pomijane:
    /// informacje bez wiersza wyniku (OK reguły, notatki etapów). Kolejność: od najgorszego poziomu, nowe i zmienione przed rozwiązanymi.
    /// </summary>
    public static IReadOnlyList<RunCompareRow> Compare(IReadOnlyList<RunIssueRow> before, IReadOnlyList<RunIssueRow> after, string project)
    {
        static bool Compared(RunIssueRow i) => i.Level != CheckLevel.Pass || i.Details is not null;
        static (string Check, string Key) Key(RunIssueRow i)
        {
            var texts = RunRules.ReadDetails(i.Details).Where(d => d.Number is null).Select(d => d.Text ?? "").ToList();
            return (i.CheckName, i.Details is null ? i.Element ?? i.Message : string.Join(" · ", texts));
        }
        var was = before.Where(Compared).GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        var now = after.Where(Compared).GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        static RunIssueRow Worst(List<RunIssueRow> rows) => rows.OrderByDescending(r => r.Level).ThenBy(r => r.Id).First();
        static IEnumerable<(string Column, decimal? Number)> Values(RunIssueRow r) =>
            r.Details is not null ? RunRules.ReadDetails(r.Details).Select(d => (d.Column, d.Number)) : r.Amount is { } a ? [("Kwota", a)] : [];
        static Dictionary<string, decimal> Numbers(List<RunIssueRow>? rows) => (rows ?? []).SelectMany(Values)
            .Where(d => d.Number is not null).GroupBy(d => d.Column, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Number!.Value), StringComparer.OrdinalIgnoreCase);
        var rows = was.Keys.Union(now.Keys).Select(key =>
        {
            var b = was.GetValueOrDefault(key);
            var a = now.GetValueOrDefault(key);
            var (numbersBefore, numbersAfter) = (Numbers(b), Numbers(a));
            var differences = numbersBefore.Keys.Union(numbersAfter.Keys, StringComparer.OrdinalIgnoreCase)
                .Select(c => (Column: c, Before: numbersBefore.GetValueOrDefault(c), After: numbersAfter.GetValueOrDefault(c)))
                .Where(d => Math.Abs(d.After - d.Before) >= 0.01m).ToList();
            var status = b is null ? RunCompareRow.New : a is null ? RunCompareRow.Resolved
                : Worst(a).Level != Worst(b).Level || a.Count != b.Count || differences.Count > 0 ? RunCompareRow.Changed
                : RunCompareRow.Same;
            var changes = b is null || a is null ? ""
                : string.Join("; ", differences.Select(d => $"{d.Column}: {RunRules.Format(d.Before)} → {RunRules.Format(d.After)} ({(d.After - d.Before >= 0 ? "+" : "")}{RunRules.Format(d.After - d.Before)})"));
            var shown = Worst(a ?? b!);
            return new RunCompareRow(key.Check, key.Key.Length == 0 ? null : key.Key, status, b is null ? null : Worst(b).Level, a is null ? null : Worst(a).Level,
                changes, (a ?? b)!.Count > 1 ? $"{shown.Message} (+{(a ?? b)!.Count - 1})" : shown.Message, new RunIssueView(shown, ChangesFor(shown, project)));
        });
        static int Order(string status) => status switch { RunCompareRow.New => 0, RunCompareRow.Changed => 1, RunCompareRow.Resolved => 2, _ => 3 };
        return rows.OrderByDescending(r => r.Level).ThenBy(r => Order(r.Status)).ThenBy(r => r.CheckName, StringComparer.CurrentCulture)
            .ThenBy(r => r.Element, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>
    /// Zakres okna „Zmiany ACWP” dla wyniku: wiersz reguły z kolumną ElementWBS, CostElement albo RodzajKosztu (niepustą, w tej kolejności)
    /// – ten element, rodzaj kosztu albo cost element; inny wynik reguły albo etapu – cały program. Para uruchomień jak w przebiegu
    /// (uruchomienie wyniku i poprzednie). Import i przerwanie przebiegu – null.
    /// </summary>
    public static AcwpChangesRequest? ChangesFor(RunIssueRow issue, string project)
    {
        if (issue.CheckName is Import or Run)
            return null;
        var details = RunRules.ReadDetails(issue.Details);
        string? Value(string column) => details.FirstOrDefault(d => string.Equals(d.Column, column, StringComparison.OrdinalIgnoreCase)).Text is { Length: > 0 } text ? text : null;
        if (Value("ElementWBS") is { } wbs)
            return new(project, WbsElements: [wbs], PinId: issue.PinId, Scope: $"{issue.CheckName}: element WBS {wbs}");
        if (Value("RodzajKosztu") is { } kind && RunCostKinds.All.Contains(kind))
            return new(project, Kind: kind, PinId: issue.PinId, Scope: $"{issue.CheckName}: {kind}");
        if (Value("CostElement") is { } costElement)
            return new(project, CostElement: costElement, PinId: issue.PinId, Scope: $"{issue.CheckName}: cost element {costElement}");
        return new(project, PinId: issue.PinId, Scope: issue.CheckName);
    }

    // „R-PAF-2026-10-T41” → „2026-10 T41”.
    private static string Week(string code)
    {
        var parts = code.Split('-');
        return parts.Length >= 5 ? $"{parts[^3]}-{parts[^2]} {parts[^1]}" : code;
    }
}
