using System.Collections.Concurrent;
using Dapper;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Shared;

/// <summary>Profilowanie zapytań SQL (zdarzenia diagnostyczne SqlClient) i czasy kroków operacji.</summary>
public sealed class SqlProfilerTests
{
    [SqlFact]
    public void Slow_command_and_error_are_reported_with_time_database_and_text()
    {
        using var database = new TestDatabase();
        var seen = new ConcurrentBag<SqlProfile>();
        using (SqlProfiler.Watch(TimeSpan.FromMilliseconds(300), seen.Add))
        {
            using var connection = database.Sql.Open();
            connection.Execute("SELECT 1 AS Fast");                                // poniżej progu – pominięte
            connection.Execute("WAITFOR DELAY '00:00:00.400'; SELECT 2 AS Slow");
            Assert.ThrowsAny<Exception>(() => connection.Execute("SELECT * FROM dbo.TabelaKtorejNieMa"));
        }

        var slow = Assert.Single(seen, p => p.Error is null);
        Assert.Contains("WAITFOR DELAY '00:00:00.400'; SELECT 2 AS Slow", slow.Sql);
        Assert.True(slow.Elapsed >= TimeSpan.FromMilliseconds(380), slow.Elapsed.ToString());
        Assert.False(string.IsNullOrEmpty(slow.Database));
        Assert.Contains("TabelaKtorejNieMa", Assert.Single(seen, p => p.Error is not null).Sql);
        Assert.DoesNotContain(seen, p => p.Sql.Contains("Fast"));
    }

    /// <summary>Zapytanie wykonane w kroku StepTimer ma nazwę kroku (log SQL – co spowalnia które wczytanie).</summary>
    [SqlFact]
    public void Command_in_step_is_reported_with_step_name()
    {
        using var database = new TestDatabase();
        var seen = new ConcurrentBag<SqlProfile>();
        using (SqlProfiler.Watch(TimeSpan.Zero, seen.Add))
        {
            using var connection = database.Sql.Open();
            new StepTimer().Time("koszty ACWP", () => connection.Execute("SELECT 3 AS InStep"));
            connection.Execute("SELECT 4 AS OutsideStep");
        }

        Assert.Equal("koszty ACWP", Assert.Single(seen, p => p.Sql.Contains("InStep")).Operation);
        Assert.Null(Assert.Single(seen, p => p.Sql.Contains("OutsideStep")).Operation);
    }

    [Fact]
    public void Session_statistics_list_commands_by_total_time_and_errors()
    {
        var statistics = new SqlStatistics();
        statistics.Add(new SqlProfile(TimeSpan.FromMilliseconds(100), "PZLTEST", "SELECT a", null, "koszty"));
        statistics.Add(new SqlProfile(TimeSpan.FromMilliseconds(300), "PZLTEST", "SELECT a", null, "koszty"));
        statistics.Add(new SqlProfile(TimeSpan.FromMilliseconds(250), "PZLPROD", "SELECT b", null));
        statistics.Add(new SqlProfile(TimeSpan.FromMilliseconds(5), "PZLTEST", "SELECT zła", "Invalid object name"));

        var lines = statistics.Summary(10);

        Assert.StartsWith("SQL – podsumowanie sesji: 4 poleceń (3 różnych), błędy 1, łącznie 655 ms", lines[0]);
        Assert.Matches(@"^\s+400 ms · 2× · śr\. 200 ms · max 300 ms \[PZLTEST\] \(koszty\): SELECT a$", lines[1]);   // największy łączny czas – pierwszy
        Assert.Contains("[PZLPROD]: SELECT b", lines[2]);
        Assert.Contains(lines, l => l.Contains("1× z 1 [PZLTEST]: SELECT zła – Invalid object name"));
        Assert.Empty(new SqlStatistics().Summary(10));
    }

    [Fact]
    public void Step_timer_lists_steps_from_the_longest()
    {
        var steps = new StepTimer();
        steps.Time("krótki", () => 1);
        steps.Time("długi", () => { Thread.Sleep(50); return 2; });

        Assert.Matches(@"^\d+ ms – długi \d+ ms, krótki \d+ ms$", steps.Summary);
    }
}
