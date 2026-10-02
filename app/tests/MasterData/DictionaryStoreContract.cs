using PzlEv.Modules.MasterData.Data;
using PzlEv.Modules.MasterData.Models;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.MasterData;

/// <summary>
/// Testy kontraktu magazynu słowników – te same dla wersji w pamięci i (F10.3) wersji SQL.
/// Implementacja testu dostarcza magazyn, zegar i użytkownika.
/// </summary>
public abstract class DictionaryStoreContract
{
    protected abstract (IDictionaryStore Store, TestClock Clock, TestUser User) Create();

    private static Dictionary<string, string?> Rate(string dept, string rate) =>
        new() { ["Department"] = dept, ["Year"] = "2026", ["Labor Rate"] = rate, ["Overhead"] = "0" };

    private static RowChange Add(string dept, string rate) => new(RowChangeKind.Added, null, null, $"{dept} | 2026", Rate(dept, rate));

    [Fact]
    public void Added_rows_are_current_with_version_1()
    {
        var (store, _, user) = Create();
        var result = store.Save("department-rates", null, [Add("W30", "100"), Add("W40", "110")]);

        Assert.True(result.Success);
        Assert.Equal(2, result.Added);
        var rows = store.Current("department-rates");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Version));
        Assert.All(rows, r => Assert.Equal(user.Account, r.RecordedBy));
    }

    [Fact]
    public void Update_keeps_history_and_as_of_returns_previous_state()
    {
        var (store, clock, user) = Create();
        store.Save("department-rates", null, [Add("W30", "100")]);
        var before = clock.Now;
        var row = Assert.Single(store.Current("department-rates"));

        clock.Advance(TimeSpan.FromHours(1));
        user.Account = @"PZL\inna.osoba";
        var result = store.Save("department-rates", null,
            [new RowChange(RowChangeKind.Updated, row.RowId, 1, row.Key, Rate("W30", "120"))]);

        Assert.True(result.Success);
        var history = store.History(row.RowId);
        Assert.Equal(2, history.Count);
        Assert.Equal("100", history[0].Values["Labor Rate"]);
        Assert.Equal(@"PZL\inna.osoba", history[0].SupersededBy);
        Assert.Equal("120", history[1].Values["Labor Rate"]);
        Assert.Equal("100", Assert.Single(store.AsOf("department-rates", before)).Values["Labor Rate"]);
        Assert.Equal("120", Assert.Single(store.Current("department-rates")).Values["Labor Rate"]);
    }

    [Fact]
    public void Remove_closes_current_version_without_deleting_history()
    {
        var (store, clock, _) = Create();
        store.Save("department-rates", null, [Add("W30", "100")]);
        var row = Assert.Single(store.Current("department-rates"));
        clock.Advance(TimeSpan.FromMinutes(5));

        store.Save("department-rates", null, [new RowChange(RowChangeKind.Removed, row.RowId, 1, row.Key, null)]);

        Assert.Empty(store.Current("department-rates"));
        var version = Assert.Single(store.History(row.RowId));
        Assert.NotNull(version.SupersededAt);
    }

    [Fact]
    public void Stale_version_is_a_conflict_and_nothing_is_saved()
    {
        var (store, clock, _) = Create();
        store.Save("department-rates", null, [Add("W30", "100"), Add("W40", "110")]);
        var rows = store.Current("department-rates");
        var w30 = rows.Single(r => r.Values["Department"] == "W30");
        var w40 = rows.Single(r => r.Values["Department"] == "W40");
        store.Save("department-rates", null, [new RowChange(RowChangeKind.Updated, w30.RowId, 1, w30.Key, Rate("W30", "105"))]);
        clock.Advance(TimeSpan.FromMinutes(1));

        // Druga osoba pracuje na wersji 1 wiersza W30 i jednocześnie zmienia W40.
        var result = store.Save("department-rates", null,
        [
            new RowChange(RowChangeKind.Updated, w40.RowId, 1, w40.Key, Rate("W40", "999")),
            new RowChange(RowChangeKind.Updated, w30.RowId, 1, w30.Key, Rate("W30", "999")),
        ]);

        Assert.False(result.Success);
        Assert.Contains("odśwież", result.Conflict);
        Assert.Equal("110", store.Current("department-rates").Single(r => r.RowId == w40.RowId).Values["Labor Rate"]);
    }

    [Fact]
    public void Duplicate_key_is_rejected()
    {
        var (store, _, _) = Create();
        store.Save("department-rates", null, [Add("W30", "100")]);
        var result = store.Save("department-rates", null, [Add("W30", "200")]);
        Assert.False(result.Success);
        Assert.Single(store.Current("department-rates"));
    }

    [Fact]
    public void Dictionaries_and_projects_are_separate()
    {
        var (store, _, _) = Create();
        store.Save("department-rates", null, [Add("W30", "100")]);
        store.Save("department-rates", "M28", [Add("W30", "100")]);
        Assert.Single(store.Current("department-rates"));
        Assert.Single(store.Current("department-rates", "M28"));
        Assert.Empty(store.Current("fx-rates"));
    }
}

public sealed class InMemoryDictionaryStoreTests : DictionaryStoreContract
{
    protected override (IDictionaryStore Store, TestClock Clock, TestUser User) Create()
    {
        var services = new TestServices();
        return (new InMemoryDictionaryStore(services.Database, services.Clock, services.User), services.Clock, services.User);
    }
}
