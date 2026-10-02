using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;
using Xunit;

namespace PzlEv.Tests.Shared;

public class InMemoryDatabaseTests
{
    [Fact]
    public void State_survives_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pzl-ev-state-{Guid.NewGuid():N}.json");
        try
        {
            var db = new InMemoryDatabase(path);
            db.Write(() => db.Table<JournalEntry>("meta.Zdarzenie").Add(
                new JournalEntry(db.NextId("meta.Zdarzenie"), DateTimeOffset.Parse("2026-10-02T10:00:00+02:00"), @"PZL\ab", "Test", null, "zapis")));
            db.Commit();

            var reloaded = new InMemoryDatabase(path);
            Assert.True(reloaded.Load());
            var rows = reloaded.Read(() => reloaded.Table<JournalEntry>("meta.Zdarzenie").ToList());
            var row = Assert.Single(rows);
            Assert.Equal("zapis", row.Message);
            Assert.Equal(2, reloaded.Write(() => reloaded.NextId("meta.Zdarzenie")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Table_access_outside_read_or_write_is_rejected()
    {
        var db = new InMemoryDatabase();
        Assert.Throws<InvalidOperationException>(() => db.Table<JournalEntry>("meta.Zdarzenie"));
    }

    [Fact]
    public void Load_without_file_returns_false()
    {
        var db = new InMemoryDatabase(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        Assert.False(db.Load());
    }
}
