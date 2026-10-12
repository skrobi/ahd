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

    [Fact]
    public void Step_timer_lists_steps_from_the_longest()
    {
        var steps = new StepTimer();
        steps.Time("krótki", () => 1);
        steps.Time("długi", () => { Thread.Sleep(50); return 2; });

        Assert.Matches(@"^\d+ ms – długi \d+ ms, krótki \d+ ms$", steps.Summary);
    }
}
