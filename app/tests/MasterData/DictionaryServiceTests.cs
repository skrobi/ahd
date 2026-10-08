using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.MasterData;

/// <summary>Serwis słowników na bazie testowej (TestDatabase).</summary>
public sealed class DictionaryServiceTests : IDisposable
{
    private readonly TestServices _services = new();
    private TestDatabase? _database;
    private IDictionaryStore _store = null!;
    private IJournal _journal = null!;
    private DictionaryService _service = null!;

    public DictionaryServiceTests()
    {
        if (TestDatabase.ConnectionString is not null)   // bez bazy testy są pominięte (SqlFact)
            Use(presets: false);
    }

    /// <summary>Baza testowa: same tabele albo tabele z danymi startowymi (migracja 002).</summary>
    private void Use(bool presets)
    {
        _database?.Dispose();
        _database = new TestDatabase(presets);
        _store = new SqlDictionaryStore(_database.Sql, _services.Clock, _services.User, GlobalDictionaries.Tables);
        _journal = new SqlJournal(_database.Sql, _services.Clock, _services.User);
        _service = new DictionaryService(_store, _journal);
    }

    public void Dispose() => _database?.Dispose();

    private static DictionarySpec Rates => GlobalDictionaries.Get(GlobalDictionaries.DepartmentRates);
    private static DictionarySpec CostCategory => GlobalDictionaries.Get(GlobalDictionaries.CostCategory);

    private static DictRow Rate(string dept, string rate, long? rowId = null, int? version = null) =>
        new(rowId, version, new Dictionary<string, string?> { ["MPK"] = dept, ["Year"] = "2026", ["Labor Rate"] = rate, ["Overhead"] = "0" });

    [SqlFact]
    public void Errors_block_saving()
    {
        var outcome = _service.Save(Rates, [Rate("W30", "abc")], [], confirmWarnings: false);
        Assert.Equal(SaveStatus.Rejected, outcome.Status);
        Assert.Empty(_store.Current(Rates.Code));
    }

    [SqlFact]
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

    [SqlFact]
    public void Save_writes_changes_and_journal()
    {
        _service.Save(Rates, [Rate("W30", "100"), Rate("W40", "110")], [], false);
        var loaded = _service.Load(Rates);
        var w30 = loaded.Single(r => r["MPK"] == "W30");
        var w40 = loaded.Single(r => r["MPK"] == "W40");

        var outcome = _service.Save(Rates, [Rate("W30", "120", w30.RowId, w30.Version), Rate("W51", "90")], [w40], false);

        Assert.Equal(SaveStatus.Saved, outcome.Status);
        Assert.Contains("+1 ~1 −1", outcome.Message);
        var current = _service.Load(Rates).ToDictionary(r => r["MPK"]!);
        Assert.Equal(["W30", "W51"], current.Keys.Order());
        Assert.Equal("120", current["W30"]["Labor Rate"]);
        Assert.Contains(_journal.Recent(10), e => e.Message.Contains("Stawki wydziałów: +1 ~1 −1"));
    }

    [SqlFact]
    public void Unchanged_rows_give_no_changes()
    {
        _service.Save(Rates, [Rate("W30", "100")], [], false);
        var outcome = _service.Save(Rates, _service.Load(Rates), [], false);
        Assert.Equal(SaveStatus.NoChanges, outcome.Status);
    }

    [SqlFact]
    public void Excel_export_and_import_roundtrip_has_no_differences()
    {
        Use(presets: true);
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

    [SqlFact]
    public void Excel_import_shows_added_changed_and_removed_rows_and_applies_them()
    {
        _service.Save(Rates, [Rate("W30", "100"), Rate("W40", "110")], [], false);
        var path = TempXlsx();
        try
        {
            // Nagłówki inną wielkością liter; numer jako liczba w Excelu.
            ExcelTableWriter.Write(path, "Stawki", ["mpk", "YEAR", "labor rate", "Overhead"],
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
            var current = _service.Load(Rates).ToDictionary(r => r["MPK"]!);
            Assert.Equal(["W30", "W52"], current.Keys.Order());
            Assert.Equal("95.5", current["W52"]["Labor Rate"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SqlFact]
    public void Excel_file_with_errors_is_not_saved()
    {
        _service.Save(Rates, [Rate("W30", "100")], [], false);
        var path = TempXlsx();
        try
        {
            ExcelTableWriter.Write(path, "Stawki", ["MPK", "Year", "Labor Rate", "Overhead"], [["W30", 2026L, "sto", 0m]]);
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

    [SqlFact]
    public void Excel_without_key_column_is_rejected()
    {
        var path = TempXlsx();
        try
        {
            ExcelTableWriter.Write(path, "Stawki", ["Year", "Labor Rate"], [[2026L, 1m]]);
            var preview = _service.PreviewImport(Rates, path);
            Assert.Contains(preview.Issues, i => i.Message == "Brak kolumny 'MPK'");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SqlFact]
    public void Preset_calendar_is_valid_and_cost_category_matches_appendix_a()
    {
        Use(presets: true);
        var calendarSpec = GlobalDictionaries.Get(GlobalDictionaries.Calendar);
        var calendar = _service.Load(calendarSpec);

        Assert.Equal(53 + 52, calendar.Count); // ISO 2026 ma 53 tygodnie, 2027 – 52
        Assert.Empty(DictionaryValidator.Validate(calendarSpec, calendar));
        Assert.Equal(24, calendar.Count(r => r["Zamykający"] == "tak"));

        var costCategory = _service.Load(CostCategory);
        Assert.Equal(33, costCategory.Count);
        var warning = Assert.Single(DictionaryValidator.Validate(CostCategory, costCategory));
        Assert.Contains("0057100000", warning.Message);
    }

    [SqlFact]
    public void Excel_without_optional_column_keeps_values_and_reports_file_rows()
    {
        var saved = _service.Save(Rates, [new(null, null, new Dictionary<string, string?>
            { ["MPK"] = "W30", ["Department"] = "Wydział 30", ["Year"] = "2026", ["Labor Rate"] = "100", ["Overhead"] = "5" })], [], true);
        Assert.Equal(SaveStatus.Saved, saved.Status);
        var path = TempXlsx();
        try
        {
            // Bez kolumn Department i Overhead, z kolumną spoza słownika; wiersz W31 z błędem – drugi wiersz danych (wiersz 3 w Excelu).
            ExcelTableWriter.Write(path, "Stawki", ["MPK", "Year", "Labor Rate", "Komentarz"], [["W30", 2026L, 120m, "x"], ["W31", 2026L, "sto", "y"]]);
            var preview = _service.PreviewImport(Rates, path);
            Assert.Contains(preview.Issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("Brak kolumny 'Overhead'") && i.Message.Contains("bez zmian"));
            Assert.Contains(preview.Issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("'Komentarz'"));
            Assert.Contains(preview.Issues, i => i.Level == CheckLevel.Error && i.Element == "wiersz 3 w pliku");

            ExcelTableWriter.Write(path, "Stawki", ["MPK", "Year", "Labor Rate"], [["W30", 2026L, 120m]]);
            preview = _service.PreviewImport(Rates, path);
            Assert.False(preview.HasErrors);
            Assert.Equal(SaveStatus.Saved, _service.ApplyImport(Rates, preview).Status);
            var row = Assert.Single(_service.Load(Rates));
            Assert.Equal(("120", "5", "Wydział 30"), (row["Labor Rate"], row["Overhead"], row["Department"]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SqlFact]
    public void Import_warns_when_file_removes_most_rows_and_rejects_excel_errors()
    {
        _service.Save(Rates, [Rate("W30", "100"), Rate("W31", "100"), Rate("W32", "100")], [], true);
        var path = TempXlsx();
        try
        {
            ExcelTableWriter.Write(path, "Stawki", ["MPK", "Year", "Labor Rate", "Overhead"], [["W30", 2026L, "#N/A", 0m]]);
            var preview = _service.PreviewImport(Rates, path);
            Assert.Contains(preview.Issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("usuwa 2 z 3"));
            Assert.Contains(preview.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("błąd formuły"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SqlFact]
    public void Exported_dictionary_reimports_without_changes()
    {
        _service.Save(Rates, [new(null, null, new Dictionary<string, string?>
            { ["MPK"] = "0000412100", ["Department"] = "W", ["Year"] = "2026", ["Labor Rate"] = "123.45678912", ["Overhead"] = "0.25" })], [], true);
        var path = TempXlsx();
        try
        {
            _service.Export(Rates, path);
            var preview = _service.PreviewImport(Rates, path);
            Assert.False(preview.HasChanges, preview.Summary + string.Join("; ", preview.Changed));
            Assert.DoesNotContain(preview.Issues, i => i.Level == CheckLevel.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SqlFact]
    public void Keys_are_compared_without_letter_case()
    {
        // „W31” i „w31” to ten sam klucz – duplikat.
        var duplicate = _service.Save(Rates, [Rate("W31", "100"), Rate("w31", "100")], [], true);
        Assert.Equal(SaveStatus.Rejected, duplicate.Status);
        Assert.Contains(duplicate.Issues, i => i.Message.StartsWith("Duplikat klucza"));

        // Plik z „w30” zmienia wiersz „W30” (historia zostaje), nie dodaje nowego i nie usuwa starego.
        _service.Save(Rates, [Rate("W30", "100")], [], true);
        var path = TempXlsx();
        try
        {
            ExcelTableWriter.Write(path, "Stawki", ["MPK", "Year", "Labor Rate", "Overhead"], [["w30", 2026L, 100m, 0m]]);
            var preview = _service.PreviewImport(Rates, path);
            Assert.Empty(preview.Added);
            Assert.Empty(preview.Removed);
            Assert.Contains("MPK: W30 → w30", Assert.Single(preview.Changed));
            Assert.Equal(SaveStatus.Saved, _service.ApplyImport(Rates, preview).Status);
            var row = Assert.Single(_service.Load(Rates));
            Assert.Equal(("w30", 2), (row["MPK"], row.Version));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempXlsx() => Path.Combine(Path.GetTempPath(), $"pzl-ev-{Guid.NewGuid():N}.xlsx");
}
