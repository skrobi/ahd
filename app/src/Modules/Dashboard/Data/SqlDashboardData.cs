using System.Data;
using System.Globalization;
using Dapper;
using PzlEv.Modules.Dashboard.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Dashboard.Data;

/// <summary>
/// Dane Pulpitu z bazy środowiska: ostatni import (META_ImportBatch), otwarte problemy (META_Problem), konfiguracja
/// importu (META_SourceDefinition, META_SourceLocation), słowniki globalne (DICT_*), projekty (META_Project),
/// dziennik (META_Journal), tydzień i okres z kalendarza okresów (DICT_Calendar). Czego nie ma w bazie
/// (np. przebiegi tygodniowe, mapowanie), Pulpit nie pokazuje.
/// </summary>
public sealed class SqlDashboardData(AppServices services) : IDashboardDataSource
{
    public const string NoImports = "Brak importów w bazie";
    public const string NoRuns = "Brak przebiegów w bazie";

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private static readonly (string Table, string Label)[] Dictionaries =
    [
        ("dict.Calendar", "kalendarz"),
        ("dict.DepartmentRate", "stawki"),
        ("dict.FxRate", "kursy"),
        ("dict.CostCategory", "Cost Category"),
        ("dict.Person", "osoby"),
    ];

    public string Eyebrow
    {
        get
        {
            var today = services.Clock.Now.Date;
            var day = today.ToString("dddd dd.MM.yyyy", Pl);
            using var connection = services.Sql.Open();
            var week = connection.QueryFirstOrDefault<CalendarWeek>(
                $"SELECT TOP (1) Week, Period FROM {services.Sql.Table("dict.Calendar")} WHERE Project IS NULL AND SupersededAt IS NULL AND @today BETWEEN DateFrom AND DateTo",
                new { today });
            return week is null ? $"{day} · brak tygodnia w kalendarzu okresów" : $"Tydzień {week.Week} · {day} · okres {week.Period}";
        }
    }

    public IReadOnlyList<GlobalCard> GlobalCards()
    {
        using var connection = services.Sql.Open();
        return [ImportCard(LastBatch(connection)), SourcesCard(connection), DictionariesCard(connection)];
    }

    public IReadOnlyList<ZakresCard> ZakresCards()
    {
        using var connection = services.Sql.Open();
        return connection.Query<ProjectRow>(
                $"SELECT Code, Name, ProjectType FROM {services.Sql.Table("meta.Project")} WHERE SupersededAt IS NULL AND Active = 1 ORDER BY Code")
            .Select(p => new ZakresCard(p.Code, TypeLabel(p.ProjectType), p.Name, null, "", new Pill("muted", "brak przebiegów"), [], NoRuns, ""))
            .ToList();
    }

    public IReadOnlyList<AttentionItem> Attention() =>
        services.Problems.Open()
            .Where(p => p.Level != CheckLevel.Pass)
            .OrderBy(p => p.Level == CheckLevel.Error ? 0 : 1)
            .ThenByDescending(p => p.Id)
            .Select(p => new AttentionItem(
                p.Id,
                new Pill(p.Level == CheckLevel.Error ? "crit" : "warn", Source(p)),
                p.Element is { Length: > 0 } element ? $"{element}: {p.Message}" : p.Message,
                p.At.ToLocalTime().ToString("dd.MM HH:mm", Pl)))
            .ToList();

    public void Resolve(long problemId)
    {
        var problem = services.Problems.Open().FirstOrDefault(p => p.Id == problemId);
        if (problem is null || services.Problems.Resolve([problemId], "oznaczony jako rozwiązany na Pulpicie") == 0)
            return;
        services.Journal.Add("Pulpit", $"Problem #{problemId} ({problem.Area}) oznaczony jako rozwiązany: {problem.Message}");
    }

    /// <summary>Skąd problem: import (numer), mapowanie (G2) albo obszar.</summary>
    private static string Source(ProblemRecord problem) =>
        problem.Reference is { } reference && reference.StartsWith(ImportBatchRow.ProblemReferencePrefix, StringComparison.Ordinal)
            ? $"Import #{reference[ImportBatchRow.ProblemReferencePrefix.Length..]}"
            : problem.Area;

    public IReadOnlyList<EventItem> Events() =>
        services.Journal.Recent(15)
            .Select(e => new EventItem(e.At.ToLocalTime().ToString("dd.MM HH:mm", Pl), e.Scope ?? e.Area, e.User, e.Message))
            .ToList();

    private BatchRow? LastBatch(IDbConnection connection) =>
        connection.QueryFirstOrDefault<BatchRow>(
            $"SELECT TOP (1) BatchId, StartedAt, UserName, Status, Imported, Unrecognized, Errors FROM {services.Sql.Table(DbTables.ImportBatch)} ORDER BY BatchId DESC");

    private static GlobalCard ImportCard(BatchRow? last)
    {
        if (last is null)
            return new("Import RABIT", "G1", NoImports, [new Pill("muted", "brak danych")]);
        var status = last.Status switch
        {
            "zakończony" => "ok",
            ImportBatchStatus.Running => "info",
            "zakończony z błędami" => "crit",
            _ => "warn",
        };
        List<Pill> pills = [new(status, last.Status), new("ok", $"{last.Imported} zaimportowane")];
        if (last.Unrecognized > 0)
            pills.Add(new("warn", $"{last.Unrecognized} nierozpoznane"));
        if (last.Errors > 0)
            pills.Add(new("crit", $"{last.Errors} z błędem"));
        return new("Import RABIT", "G1",
            $"Ostatni import #{last.BatchId} · {last.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Pl)} · {last.UserName}", pills);
    }

    private GlobalCard SourcesCard(IDbConnection connection)
    {
        var counts = connection.QuerySingle<SourceCounts>(
            $"""
            SELECT
                (SELECT COUNT(*) FROM {services.Sql.Table(DbTables.SourceDefinition)} WHERE SupersededAt IS NULL) AS Definitions,
                (SELECT COUNT(*) FROM {services.Sql.Table(DbTables.SourceDefinition)} WHERE SupersededAt IS NULL AND Active = 1) AS ActiveDefinitions,
                (SELECT COUNT(*) FROM {services.Sql.Table(DbTables.SourceLocation)} WHERE SupersededAt IS NULL) AS Locations,
                (SELECT COUNT(*) FROM {services.Sql.Table(DbTables.SourceLocation)} WHERE SupersededAt IS NULL AND Active = 1) AS ActiveLocations
            """);
        return new("Źródła importu", "Administracja", "Definicje źródeł i lokalizacje RABIT",
        [
            new(counts.ActiveDefinitions > 0 ? "ok" : "warn", $"definicje aktywne: {counts.ActiveDefinitions} z {counts.Definitions}"),
            new(counts.ActiveLocations > 0 ? "ok" : "warn", $"lokalizacje aktywne: {counts.ActiveLocations} z {counts.Locations}"),
        ]);
    }

    private GlobalCard DictionariesCard(IDbConnection connection)
    {
        var counts = string.Join(",\n", Dictionaries.Select((d, i) =>
            $"(SELECT COUNT(*) FROM {services.Sql.Table(d.Table)} WHERE Project IS NULL AND SupersededAt IS NULL) AS C{i}"));
        var row = (IDictionary<string, object>)connection.QuerySingle($"SELECT {counts}");
        var pills = Dictionaries.Select((d, i) => (Label: d.Label, Count: Convert.ToInt32(row[$"C{i}"], CultureInfo.InvariantCulture)))
            .Select(d => new Pill(d.Count > 0 ? "ok" : "muted", $"{d.Label}: {d.Count}"))
            .ToList();
        return new("Słowniki globalne", "MS SQL", $"Bieżące wiersze – {services.Sql.Describe}", pills);
    }

    private static string TypeLabel(string type) => type switch
    {
        "WEWNETRZNY" => "Wewnętrzny",
        _ => type,
    };

    private sealed class CalendarWeek
    {
        public int Week { get; set; }
        public string Period { get; set; } = "";
    }

    private sealed class ProjectRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string ProjectType { get; set; } = "";
    }

    private sealed class BatchRow
    {
        public long BatchId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public string UserName { get; set; } = "";
        public string Status { get; set; } = "";
        public int Imported { get; set; }
        public int Unrecognized { get; set; }
        public int Errors { get; set; }
    }

    private sealed class SourceCounts
    {
        public int Definitions { get; set; }
        public int ActiveDefinitions { get; set; }
        public int Locations { get; set; }
        public int ActiveLocations { get; set; }
    }
}
