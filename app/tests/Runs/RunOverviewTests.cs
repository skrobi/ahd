using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.Runs;

/// <summary>Ekran Przebiegi (RunOverview): macierz kontrola × uruchomienia, definicja przebiegu, zapis ustawień uruchomienia.</summary>
public sealed class RunOverviewTests
{
    private static RunPinRow Pin(long id, string code, int no, int day) =>
        new(id, 1, code, "PAF", no, new DateTimeOffset(2026, 10, day, 8, 0, 0, TimeSpan.Zero), "jan", null, RunStatuses.Ok, null, 10, 100m, 0m, 0, 0, null, null);

    private static RunIssueRow Issue(long pin, string check, CheckLevel level, long id) => new(id, pin, check, level, null, "x", null);

    [Fact]
    public void Matrix_has_runs_from_oldest_checks_in_catalog_order_and_worst_level_per_cell()
    {
        RunPinRow[] pins = [Pin(2, "R-PAF-2026-10-T41", 1, 9), Pin(1, "R-PAF-2026-10-T40", 2, 2)];   // od najnowszego, jak magazyn
        var issues = new Dictionary<long, IReadOnlyList<RunIssueRow>>
        {
            [1] = [Issue(1, RunChecks.NetZero, CheckLevel.Error, 1), Issue(1, RunChecks.NetZero, CheckLevel.Error, 2), Issue(1, "Import", CheckLevel.Warning, 3)],
            [2] = [Issue(2, RunChecks.NetZero, CheckLevel.Pass, 4), Issue(2, "Koszty po narzutach CAS", CheckLevel.Pass, 5), Issue(2, "Przebieg", CheckLevel.Error, 6)],
        };

        var matrix = RunOverview.Matrix(pins, issues);

        Assert.Equal(["2026-10 T40 #2", "2026-10 T41 #1"], matrix.Columns.Select(c => c.Label));
        var checks = matrix.Rows.Select(r => r.CheckName).ToList();
        Assert.Equal("Import", checks[0]);
        Assert.Equal("Koszty po narzutach CAS", checks[1]);
        Assert.Equal("Przebieg", checks[^1]);
        Assert.Equal(RunCheckRules.All.Select(r => r.Check).Distinct(), checks.Skip(2).SkipLast(1));
        var net = matrix.Rows.Single(r => r.CheckName == RunChecks.NetZero).Cells;
        Assert.Equal(("2 ERROR", CheckLevel.Error), (net[0].Text, net[0].Level));
        Assert.Equal(("OK", CheckLevel.Pass), (net[1].Text, net[1].Level));
        var quality = matrix.Rows.Single(r => r.CheckName == RunChecks.Quality).Cells;
        Assert.All(quality, c => Assert.Equal(("–", (CheckLevel?)null), (c.Text, c.Level)));
        Assert.Equal("1 WARNING", matrix.Rows[0].Cells[0].Text);
    }

    [Fact]
    public void Definition_shows_both_run_variants_source_and_settings_stored_in_run()
    {
        RunCheckRow[] rows =
        [
            new("CAS", RunCheckRules.NetZeroBalance.Name, RunCheckRules.ClosingRun, true, "WARNING", 10m),
            new("SAC", RunCheckRules.NoWbsElement.Name, RunCheckRules.AnyRun, false, null, null),
        ];
        var stored = RunChecks.Settings(rows, "CAS", isClosing: true);

        var definition = RunOverview.Definition(rows, "CAS", RunOverview.Deserialize(RunOverview.Serialize(stored)));

        Assert.Equal(RunCheckRules.All.Count, definition.Count);
        var net = definition.Single(d => d.Rule == RunCheckRules.NetZeroBalance);
        Assert.Equal(("ERROR, próg 1,00", "WARNING, próg 10,00", "słownik", "WARNING, próg 10,00"), (net.MidPeriodText, net.ClosingText, net.Source, net.InRunText));
        var noWbs = definition.Single(d => d.Rule == RunCheckRules.NoWbsElement);   // wiersz innego typu programu
        Assert.Equal(("WARNING", "domyślne"), (noWbs.MidPeriodText, noWbs.Source));
        Assert.Equal("wyłączona", RunOverview.Definition(rows, "SAC").Single(d => d.Rule == RunCheckRules.NoWbsElement).ClosingText);
        Assert.Equal("–", RunOverview.Definition(rows, "SAC")[0].InRunText);
        Assert.Null(RunOverview.Deserialize("nie json"));
        Assert.Null(RunOverview.Deserialize(null));
    }
}
