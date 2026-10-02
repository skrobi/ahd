using PzlEv.Modules.MasterData.Data;
using PzlEv.Modules.MasterData.Models;
using PzlEv.Modules.MasterData.Services;
using PzlEv.Shared.Utils.Files;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.MasterData;

public class DictionaryServiceTests
{
    private readonly TestServices _services = new();
    private readonly InMemoryDictionaryStore _store;
    private readonly DictionaryService _service;

    public DictionaryServiceTests()
    {
        _store = new InMemoryDictionaryStore(_services.Database, _services.Clock, _services.User);
        _service = new DictionaryService(_store, _services.Journal);
    }

    private static DictionarySpec Rates => GlobalDictionaries.Get(GlobalDictionaries.DepartmentRates);
    private static DictionarySpec CostCategory => GlobalDictionaries.Get(GlobalDictionaries.CostCategory);

    private static DictRow Rate(string dept, string rate, long? rowId = null, int? version = null) =>
        new(rowId, version, new Dictionary<string, string?> { ["Department"] = dept, ["Year"] = "2026", ["Labor Rate"] = rate, ["Overhead"] = "0" });

    [Fact]
    public void Errors_block_saving()
    {
        var outcome = _service.Save(Rates, [Rate("W30", "abc")], [], confirmWarnings: false);
        Assert.Equal(SaveStatus.Rejected, outcome.Status);
        Assert.Empty(_store.Current(Rates.Code));
    }

    [Fact]
    public void Warnings_need_confirmation()
    {
        DictRow[] rows = [new(null, null, new Dictionary<string, string?> { ["Numer elementu kosztowego"] = "123", ["Cost Category"] = null })];

        var first = _service.Save(CostCategory, rows, [], confirmWarnings: false);
        Assert.Equal(SaveStatus.NeedsConfirmation, first.Status);
        Assert.Empty(_store.Current(CostCategory.Code));

        var second = _service.Save(CostCategory, rows, [], confirmWarnings: true);
        Assert.Equal(SaveStatus.Saved, second.Status);
        Assert.Equal("0000000123", Assert.Single(_store.Current(CostCategory.Code)).Values["Numer elementu kosztowego"]);
    }

    [Fact]
    public void Save_writes_changes_and_journal()
    {
        _service.Save(Rates, [Rate("W30", "100"), Rate("W40", "110")], [], false);
        var loaded = _service.Load(Rates);
        var w30 = loaded.Single(r => r["Department"] == "W30");
        var w40 = loaded.Single(r => r["Department"] == "W40");

        var outcome = _service.Save(Rates, [Rate("W30", "120", w30.RowId, w30.Version), Rate("W51", "90")], [w40], false);

        Assert.Equal(SaveStatus.Saved, outcome.Status);
        Assert.Contains("+1 ~1 −1", outcome.Message);
        var current = _service.Load(Rates).ToDictionary(r => r["Department"]!);
        Assert.Equal(["W30", "W51"], current.Keys.Order());
        Assert.Equal("120", current["W30"]["Labor Rate"]);
        Assert.Contains(_services.Journal.Recent(10), e => e.Message.Contains("Stawki wydziałów: +1 ~1 −1"));
    }

    [Fact]
    public void Unchanged_rows_give_no_changes()
    {
        _service.Save(Rates, [Rate("W30", "100")], [], false);
        var outcome = _service.Save(Rates, _service.Load(Rates), [], false);
        Assert.Equal(SaveStatus.NoChanges, outcome.Status);
    }

    [Fact]
    public void Excel_export_and_import_roundtrip_has_no_differences()
    {
        MasterDataSeed.EnsureSeeded(_store, 2026);
        var path = TempXlsx();
        try
        {
            _service.Export(CostCategory, path);
            var preview = _service.PreviewImport(CostCategory, path);
            Assert.False(preview.HasErrors);
            Assert.False(preview.HasChanges);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Excel_import_shows_added_changed_and_removed_rows_and_applies_them()
    {
        _service.Save(Rates, [Rate("W30", "100"), Rate("W40", "110")], [], false);
        var path = TempXlsx();
        try
        {
            // Nagłówki inną wielkością liter; numer jako liczba w Excelu.
            ExcelTableWriter.Write(path, "Stawki", ["department", "YEAR", "labor rate", "Overhead"],
            [
                ["W30", 2026L, 150m, 0m],
                ["W52", 2026L, 95.5m, 3m],
            ]);

            var preview = _service.PreviewImport(Rates, path);

            Assert.Equal(["W52 | 2026"], preview.Added);
            Assert.Single(preview.Changed);
            Assert.Contains("Labor Rate: 100 → 150", preview.Changed[0]);
            Assert.Equal(["W40 | 2026"], preview.Removed);

            var outcome = _service.ApplyImport(Rates, preview);
            Assert.Equal(SaveStatus.Saved, outcome.Status);
            var current = _service.Load(Rates).ToDictionary(r => r["Department"]!);
            Assert.Equal(["W30", "W52"], current.Keys.Order());
            Assert.Equal("95.5", current["W52"]["Labor Rate"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Excel_file_with_errors_is_not_saved()
    {
        _service.Save(Rates, [Rate("W30", "100")], [], false);
        var path = TempXlsx();
        try
        {
            ExcelTableWriter.Write(path, "Stawki", ["Department", "Year", "Labor Rate", "Overhead"], [["W30", 2026L, "sto", 0m]]);
            var preview = _service.PreviewImport(Rates, path);
            Assert.True(preview.HasErrors);
            Assert.Equal(SaveStatus.Rejected, _service.ApplyImport(Rates, preview).Status);
            Assert.Equal("100", Assert.Single(_service.Load(Rates))["Labor Rate"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Excel_without_key_column_is_rejected()
    {
        var path = TempXlsx();
        try
        {
            ExcelTableWriter.Write(path, "Stawki", ["Year", "Labor Rate"], [[2026L, 1m]]);
            var preview = _service.PreviewImport(Rates, path);
            Assert.Contains(preview.Issues, i => i.Message == "Brak kolumny 'Department'");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Seeded_calendar_is_valid_and_cost_category_matches_appendix_a()
    {
        MasterDataSeed.EnsureSeeded(_store, 2026);
        var calendarSpec = GlobalDictionaries.Get(GlobalDictionaries.Calendar);
        var calendar = _service.Load(calendarSpec);

        Assert.Equal(53 + 52, calendar.Count); // ISO 2026 ma 53 tygodnie, 2027 – 52
        Assert.Empty(DictionaryValidator.Validate(calendarSpec, calendar));
        Assert.Equal(24, calendar.Count(r => r["Zamykający"] == "tak"));

        var costCategory = _service.Load(CostCategory);
        Assert.Equal(33, costCategory.Count);
        var warning = Assert.Single(DictionaryValidator.Validate(CostCategory, costCategory));
        Assert.Contains("0057100000", warning.Message);

        Assert.Equal(0, MasterDataSeed.EnsureSeeded(_store, 2026)); // drugi raz – bez zmian
    }

    private static string TempXlsx() => Path.Combine(Path.GetTempPath(), $"pzl-ev-{Guid.NewGuid():N}.xlsx");
}
