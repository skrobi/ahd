using System.Globalization;
using System.Text.Json;
using PzlEv.Modules.Runs.Data;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Modules.Runs.Services;

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
/// Reguła w definicji przebiegu programu: ustawienie w środku okresu i w przebiegu zamykającym (słownik „Kontrole przebiegu”
/// albo domyślne), FromDictionary – reguła ma wiersz słownika dla typu programu; InRun – ustawienie zapisane w uruchomieniu (null – brak).
/// </summary>
public sealed record RunDefinitionRow(RunCheckRule Rule, RunCheckSetting MidPeriod, RunCheckSetting Closing, bool FromDictionary, RunCheckSetting? InRun)
{
    public string Check => Rule.Check;

    public string Name => Rule.Name;

    public string Description => Rule.Description;

    public string MidPeriodText => RunOverview.Show(MidPeriod);

    public string ClosingText => RunOverview.Show(Closing);

    public string Source => FromDictionary ? "słownik" : "domyślne";

    public string InRunText => InRun is null ? "–" : RunOverview.Show(InRun);
}

/// <summary>
/// Ekran Przebiegi (docs/funkcjonalnosc.md, F06): macierz wyników kontroli kolejnych uruchomień programu i definicja przebiegu
/// (reguły kontroli z ustawieniami dla typu programu); ustawienia reguł użyte w uruchomieniu – zapis JSON (META_RunPin.CheckSettings).
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
        int Rank(string check) => check == Import ? 0 : check == Run ? 1000 : catalog.IndexOf(check) is var i and >= 0 ? 10 + i : 5;
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

    /// <summary>Definicja przebiegu dla typu programu: reguły katalogu z ustawieniami obu wariantów przebiegu i ustawieniem z uruchomienia.</summary>
    public static IReadOnlyList<RunDefinitionRow> Definition(IReadOnlyList<RunCheckRow> rows, string projectType,
        IReadOnlyDictionary<string, RunCheckSetting>? inRun = null)
    {
        var mid = RunChecks.Settings(rows, projectType, isClosing: false);
        var closing = RunChecks.Settings(rows, projectType, isClosing: true);
        bool Matches(RunCheckRow r) =>
            string.Equals(r.ProjectType?.Trim(), projectType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.ProjectType?.Trim(), RunCheckRules.AllTypes, StringComparison.OrdinalIgnoreCase);
        return RunCheckRules.All.Select(rule => new RunDefinitionRow(rule, mid[rule.Code], closing[rule.Code],
            rows.Any(r => Matches(r) && RunCheckRules.ByName(r.RuleName) == rule), inRun?.GetValueOrDefault(rule.Code))).ToList();
    }

    /// <summary>Opis ustawienia: wyłączona albo poziom i próg.</summary>
    public static string Show(RunCheckSetting setting) =>
        !setting.Active ? "wyłączona"
            : (setting.Level switch { CheckLevel.Error => "ERROR", CheckLevel.Warning => "WARNING", _ => "INFO" })
              + (setting.Threshold is { } t ? $", próg {t.ToString("#,0.00", Pl)}" : "");

    private sealed record StoredSetting(bool Active, string Level, decimal? Threshold);

    /// <summary>Ustawienia reguł uruchomienia (kod reguły → ustawienie) do zapisu w META_RunPin.CheckSettings.</summary>
    public static string Serialize(IReadOnlyDictionary<string, RunCheckSetting> settings) =>
        JsonSerializer.Serialize(settings.ToDictionary(s => s.Key, s => new StoredSetting(s.Value.Active, s.Value.Level.ToString(), s.Value.Threshold)));

    /// <summary>Ustawienia zapisane w uruchomieniu; null – brak zapisu (uruchomienie sprzed migracji 048) albo zapis nieczytelny.</summary>
    public static IReadOnlyDictionary<string, RunCheckSetting>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, StoredSetting>>(json)?
                .ToDictionary(s => s.Key, s => new RunCheckSetting(s.Value.Active, Enum.TryParse<CheckLevel>(s.Value.Level, out var level) ? level : CheckLevel.Pass,
                    s.Value.Threshold));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // „R-PAF-2026-10-T41” → „2026-10 T41”.
    private static string Week(string code)
    {
        var parts = code.Split('-');
        return parts.Length >= 5 ? $"{parts[^3]}-{parts[^2]} {parts[^1]}" : code;
    }
}
