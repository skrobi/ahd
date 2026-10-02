using Dapper;
using PzlEv.Modules.Dashboard.Data;
using PzlEv.Modules.Import.Data;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Dashboard;

/// <summary>Pulpit czyta tylko z bazy: pusta baza = puste karty, po zapisach – stan z tabel.</summary>
public sealed class SqlDashboardDataTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-dashboard-").FullName;
    private TestDatabase _database = null!;
    private AppServices _app = null!;
    private SqlDashboardData _data = null!;

    private void Use(bool presets = false)
    {
        _database = new TestDatabase(presets);
        _app = _services.App(_root, _database.Sql);
        _data = new SqlDashboardData(_app);
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [SqlFact]
    public void Empty_database_shows_no_sample_data()
    {
        Use();

        Assert.Equal("piątek 02.10.2026 · brak tygodnia w kalendarzu okresów", _data.Eyebrow);
        var cards = _data.GlobalCards();
        Assert.Equal(["Import RABIT", "Źródła importu", "Słowniki globalne"], cards.Select(c => c.Code));
        Assert.Equal(SqlDashboardData.NoImports, cards[0].Subtitle);
        Assert.Equal(["definicje aktywne: 0 z 0", "lokalizacje aktywne: 0 z 0"], cards[1].Pills.Select(p => p.Text));
        Assert.All(cards[2].Pills, p => Assert.EndsWith(": 0", p.Text));
        Assert.Empty(_data.ZakresCards());
        Assert.Empty(_data.Attention());
        Assert.Empty(_data.Events());
    }

    [SqlFact]
    public void Shows_what_is_stored_in_the_database()
    {
        Use(presets: true);
        var imports = new SqlImportStore(_database.Sql);
        var batch = imports.BeginBatch(_services.Clock.Now, _services.User.Account, "PC-1", "test");
        _app.Problems.Add("Import", "brak lokalizacji RABIT", Issue.Warning("Brak aktywnej lokalizacji RABIT", "RABIT"), ImportBatchRow.ProblemReference(batch));
        imports.FinishBatch(batch, _services.Clock.Now, files: 3, imported: 2, skipped: 0, duplicates: 0, unrecognized: 1, errors: 0, status: "zakończony");
        using (var connection = _database.Sql.Open())
            connection.Execute(
                $"INSERT INTO {_database.Sql.Table("meta.Project")} (ProjectId, Version, Code, Name, ProjectType, Active, RecordedAt, RecordedBy) VALUES (1, 1, 'S70i', N'S-70i', 'SAC', 1, SYSDATETIMEOFFSET(), N'test')");

        Assert.Equal("Tydzień 40 · piątek 02.10.2026 · okres 2026-10", _data.Eyebrow);
        var cards = _data.GlobalCards();
        Assert.StartsWith($"Ostatni import #{batch} · ", cards[0].Subtitle);
        Assert.Equal(["zakończony", "2 zaimportowane", "1 nierozpoznane"], cards[0].Pills.Select(p => p.Text));
        Assert.Equal(["definicje aktywne: 2 z 2", "lokalizacje aktywne: 1 z 1"], cards[1].Pills.Select(p => p.Text));
        Assert.Contains(cards[2].Pills, p => p.Text == "Cost Category: 33");
        Assert.Contains(cards[2].Pills, p => p.Text == "stawki: 0");
        var project = Assert.Single(_data.ZakresCards());
        Assert.Equal(("S70i", "SAC", SqlDashboardData.NoRuns), (project.Code, project.TypeLabel, project.RunLine));
        Assert.Empty(project.Dots);
        var attention = Assert.Single(_data.Attention());
        Assert.Equal(new Pill("warn", $"Import #{batch}"), attention.Tag);
        Assert.Equal("RABIT: Brak aktywnej lokalizacji RABIT", attention.Text);
        Assert.Contains(_data.Events(), e => e.Message.StartsWith("Migracja 002 – dane startowe"));
    }
}
