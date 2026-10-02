using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Models;
using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Import;

/// <summary>Import G1 na danych wzorcowych: decyzje, izolacja błędów, dane kanoniczne i kontrola przepływu.</summary>
public sealed class ImportServiceTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-import-").FullName;
    private TestDatabase? _database;
    private AppServices _app = null!;
    private SourceConfigService _config = null!;
    private IImportStore _store = null!;
    private ImportService _import = null!;

    /// <summary>Magazyny testu: w pamięci albo w bazie testowej (TestStores).</summary>
    private void Use(string store)
    {
        if (store == TestStores.Sql)
        {
            _database = new TestDatabase();
            _app = _services.App(_root, _database.Sql);
            _config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), _app.Journal);
            _store = new SqlImportStore(_database.Sql);
        }
        else
        {
            _app = _services.App(_root);
            _config = new SourceConfigService(new InMemorySourceConfigStore(_services.Database, _services.Clock, _services.User), _app.Journal);
            _store = new InMemoryImportStore(_services.Database);
        }
        SourceConfigSeed.EnsureSeeded(_config);
        _import = new ImportService(_store, _app);
    }

    private string ImportFolder => _services.App(_root).Config.ImportFolder;

    private string CopySample(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, name);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), target, overwrite: true);
        return target;
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Sample_file_is_imported_with_canonical_data_matching_expected_sums(string store)
    {
        Use(store);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        var result = Assert.Single(run.Files);
        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.Equal("ACTUALS_PAF", result.SourceCode);
        Assert.Equal(6, result.Rows);
        Assert.Equal(ImportService.NoRabitLocation, Assert.Single(run.Issues).Message);   // lokalizacja z seeda nieaktywna

        var seen = Assert.Single(_store.Seen(run.BatchId));
        var file = _store.FindByHash(seen.Sha256!)!;
        Assert.Equal("utworzone", file.CanonicalStatus);
        Assert.Equal(6, _store.RawRows(file.Id).Count);
        var actuals = _store.Actuals(file.Id);
        Assert.Equal(6, actuals.Count);
        Assert.Equal(10574.11m, actuals.Sum(a => a.ValueObjCrcy));     // testdata/README.md
        Assert.Equal(2203.12m, actuals.Sum(a => a.ValueRepCur));
        Assert.Equal("0051105550", actuals[0].CostElement);
        Assert.Equal(-230.40m, actuals[4].ValueObjCrcy);               // minus na końcu (zapis SAP)
        Assert.Equal(new DateOnly(2026, 3, 29), actuals[0].CreatedOn);
        Assert.Equal("zakończony", _store.Batches(1).Single().Status);
        Assert.Contains(_app.Journal.Recent(5), e => e.Message.StartsWith($"Import #{run.BatchId}"));
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Second_run_skips_unchanged_files(string store)
    {
        Use(store);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        _import.Run();

        var second = _import.Run();

        Assert.Equal(FileDecisions.Skipped, Assert.Single(second.Files).Decision);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Same_content_under_another_name_is_a_duplicate(string store)
    {
        Use(store);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        _import.Run();
        CopySample(ImportFolder, "ACTUALS_PAF_02.csv");

        var run = _import.Run();

        var duplicate = run.Files.Single(f => f.FileName == "ACTUALS_PAF_02.csv");
        Assert.Equal(FileDecisions.Duplicate, duplicate.Decision);
        Assert.Contains("ACTUALS_PAF_01.csv", duplicate.Description);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Unrecognized_corrupted_and_ignored_files_do_not_stop_the_import(string store)
    {
        Use(store);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        File.WriteAllText(Path.Combine(ImportFolder, "RAPORT_NIEZNANY.csv"), "A;B\n1;2\n");
        File.WriteAllBytes(Path.Combine(ImportFolder, "ACTUALS_CES_01.xlsx"), [1, 2, 3, 4, 5]);
        File.WriteAllText(Path.Combine(ImportFolder, "~$ACTUALS_PAF_01.csv"), "blokada Excela");

        var run = _import.Run();

        Assert.Equal(3, run.Files.Count);
        Assert.Equal(FileDecisions.Imported, run.Files.Single(f => f.FileName == "ACTUALS_PAF_01.csv").Decision);
        Assert.Equal(FileDecisions.Unrecognized, run.Files.Single(f => f.FileName == "RAPORT_NIEZNANY.csv").Decision);
        Assert.Equal(FileDecisions.Error, run.Files.Single(f => f.FileName == "ACTUALS_CES_01.xlsx").Decision);
        Assert.Contains(run.Issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("RAPORT_NIEZNANY.csv"));
        Assert.Equal("zakończony z błędami", _store.Batches(1).Single().Status);
        Assert.Contains(_app.Problems.ByReference($"import:{run.BatchId}"), p => p.Level == CheckLevel.Error);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Unavailable_location_is_an_error_and_manual_folder_is_still_imported(string store)
    {
        Use(store);
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", Path.Combine(_root, "brak-folderu"), Active: true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        Assert.Contains(run.Issues, i => i.Level == CheckLevel.Error && i.Element == "RABIT test" && i.Message.StartsWith("Lokalizacja niedostępna"));
        Assert.Equal(FileDecisions.Imported, Assert.Single(run.Files).Decision);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Unavailable_sharepoint_location_explains_gateway_login(string store)
    {
        Use(store);
        _config.SaveLocation(new LocationInput(null, null, "RABIT SP", @"\\sp.example.com@SSL\DavWWWRoot\sites\R\Shared Documents\E1", Active: true));

        var run = _import.Run();

        Assert.Contains(run.Issues, i => i.Element == "RABIT SP" && i.Message.StartsWith("Lokalizacja niedostępna") && i.Message.Contains("bramą logowania F5"));
    }

    /// <summary>Postęp zbierany synchronicznie (Progress&lt;T&gt; w aplikacji przekazuje go do wątku UI).</summary>
    private sealed class Collect(Action<ImportProgress>? onReport = null) : IProgress<ImportProgress>
    {
        public List<ImportProgress> Items { get; } = [];

        public void Report(ImportProgress value)
        {
            Items.Add(value);
            onReport?.Invoke(value);
        }
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Import_start_is_in_history_and_file_status_changes_during_import(string store)
    {
        Use(store);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        string? statusAtStart = null;
        var progress = new Collect(p =>
        {
            if (p.BatchId is not null)
                statusAtStart = _store.Batches(1).Single().Status;
        });

        var run = _import.Run(progress);

        Assert.Equal(ImportBatchStatus.Running, statusAtStart);                   // wpis „w toku” od początku importu
        Assert.Contains(_app.Journal.Recent(5), e => e.Message == $"Import #{run.BatchId} rozpoczęty (PZL\\analityk, {Environment.MachineName})");
        Assert.Equal(["importowanie…", "zaimportowany"],
            progress.Items.Where(p => p.FileName == "ACTUALS_PAF_01.csv").Select(p => p.Status!.Split(" – ")[0]));
        Assert.Equal("zakończony", _store.Batches(1).Single().Status);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Import_does_not_start_while_another_person_imports(string store)
    {
        Use(store);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        var other = new TestServices().App(_root);   // druga osoba – ten sam folder sieciowy, osobna baza
        using var lease = other.Locks.TryAcquire(ImportService.LockName, out _);
        Assert.NotNull(lease);

        var run = _import.Run();

        Assert.NotNull(run.NotStarted);
        Assert.StartsWith(@"Import nie rozpoczęty – trwa import: PZL\analityk (", run.NotStarted);
        Assert.Empty(run.Files);
        Assert.Empty(_store.Batches(10));
        Assert.NotNull(_import.RunningImport());
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Lock_is_released_after_import_and_unfinished_import_is_marked(string store)
    {
        Use(store);
        var stale = _store.BeginBatch(_services.Clock.Now, "ktos", "PC1", "test");   // np. awaria aplikacji w trakcie importu

        var run = _import.Run();

        Assert.Null(run.NotStarted);
        Assert.Null(_import.RunningImport());
        Assert.Equal(ImportBatchStatus.Abandoned, _store.Batches(10).Single(b => b.Id == stale).Status);
        Assert.Null(_import.Run().NotStarted);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Same_name_in_two_locations_are_different_files(string store)
    {
        Use(store);
        var rabit = Path.Combine(_root, "rabit");
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", rabit, Active: true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        var other = CopySample(rabit, "ACTUALS_PAF_01.csv");
        File.AppendAllText(other, "2DI473;2DI473001003;51105550;x;x;x;PLN;1,00;PLN;1,00;USD;0,25;0,000;;;;;;;2026;2026-03-31;3\r\n");

        var run = _import.Run();

        Assert.Equal(2, run.Files.Count(f => f.Decision == FileDecisions.Imported));
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Longest_prefix_wins(string store)
    {
        Use(store);
        _config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS", "ACTUALS", "ogólny", ["A"], SourceParsers.None, true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        Assert.Equal("ACTUALS_PAF", Assert.Single(_import.Run().Files).SourceCode);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void One_definition_with_actuals_asterisk_prefix_imports_all_actuals_files(string store)
    {
        Use(store);
        // Wszystkie ACTUALS_… jako jedno źródło: istniejące definicje wyłączone, nowa z prefiksem „ACTUALS_*”.
        foreach (var d in _config.Definitions())
            _config.SaveDefinition(new DefinitionInput(d.DefinitionId, d.Version, d.Code, d.Prefix, d.ReportType, d.Columns, d.Parser, Active: false));
        _config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS", "ACTUALS_*", "Koszty rzeczywiste CES", SourceParsers.ActualsColumns,
            SourceParsers.Actuals, true));
        CopySample(ImportFolder, "ACTUALS_PAF2_B6_AC1.csv");

        var result = Assert.Single(_import.Run().Files);

        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.Equal("ACTUALS", result.SourceCode);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Changed_column_layout_keeps_raw_rows_but_no_canonical_data(string store)
    {
        Use(store);
        Directory.CreateDirectory(ImportFolder);
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        File.WriteAllLines(Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), lines.Select(l => l[(l.IndexOf(';') + 1)..])); // bez pierwszej kolumny

        var run = _import.Run();

        Assert.Equal(FileDecisions.Imported, Assert.Single(run.Files).Decision);
        Assert.Contains(run.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("układ kolumn niezgodny"));
        var file = _store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!;
        Assert.StartsWith("brak – sygnatura", file.CanonicalStatus);
        Assert.Equal(6, _store.RawRows(file.Id).Count);
        Assert.Empty(_store.Actuals(file.Id));
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Value_type_error_blocks_canonical_data_with_row_number(string store)
    {
        Use(store);
        Directory.CreateDirectory(ImportFolder);
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        lines[3] = lines[3].Replace("2 400,00;PLN;2 400,00", "2 400,00;PLN;dwa tysiące");
        File.WriteAllLines(Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), lines);

        var run = _import.Run();

        Assert.Contains(run.Issues, i => i.Message.StartsWith("Value in Obj. Crcy: 'dwa tysiące'") && i.Element!.EndsWith("wiersz danych 3"));
        var file = _store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!;
        Assert.Empty(_store.Actuals(file.Id));
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Excel_file_with_numeric_cells_is_imported(string store)
    {
        Use(store);
        Directory.CreateDirectory(ImportFolder);
        var sample = TabularFileReader.Read(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        var rows = sample.Rows.Select(r => (IReadOnlyList<object?>)r.Select((v, i) =>
            i is 7 or 9 or 11 or 12 && PolishNumber.TryParse(v, out var d) ? d : (object?)v).ToList());
        ExcelTableWriter.Write(Path.Combine(ImportFolder, "ACTUALS_PAF_01.xlsx"), "Sheet1", sample.Headers, rows);

        var run = _import.Run();

        var file = _store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!;
        Assert.DoesNotContain(run.Issues, i => i.Message != ImportService.NoRabitLocation);
        Assert.Equal(2203.12m, _store.Actuals(file.Id).Sum(a => a.ValueRepCur));
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Cancelled_import_stops_and_is_marked(string store)
    {
        Use(store);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var run = _import.Run(cancellation: cts.Token);

        Assert.True(run.Cancelled);
        Assert.Empty(run.Files);
        Assert.Equal("przerwany", _store.Batches(1).Single().Status);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Check_shows_access_files_recognition_and_subfolders_without_importing(string store)
    {
        Use(store);
        var rabit = Path.Combine(_root, "rabit");
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", rabit, Active: true));
        CopySample(rabit, "ACTUALS_PAF2_B6_AC1.csv");
        File.WriteAllText(Path.Combine(rabit, "RAPORT_NIEZNANY.csv"), "A;B\n1;2\n");
        File.WriteAllText(Path.Combine(rabit, "~$ACTUALS_PAF2_B6_AC1.csv"), "blokada Excela");
        Directory.CreateDirectory(Path.Combine(rabit, "Archiwum"));

        var check = _import.Check();

        var location = check.Locations.Single(l => l.Name == "RABIT test");
        Assert.True(location.Accessible);
        Assert.Equal(rabit, location.Path);
        Assert.Contains("plików 2, pasujących do definicji 1 (najnowszy z ", location.Status);
        Assert.Contains("pominiętych tymczasowych 1", location.Status);
        Assert.Contains("Archiwum", location.Status);
        Assert.True(check.Locations.Single(l => l.Name == "Do_importu").Accessible);
        Assert.DoesNotContain(check.Locations, l => l.Name == "RABIT");
        var actuals = check.Files.Single(f => f.FileName == "ACTUALS_PAF2_B6_AC1.csv");
        Assert.StartsWith("ACTUALS_PAF", actuals.Recognition);
        Assert.Equal("zostanie zaimportowany", actuals.Note);
        Assert.True(actuals.Matches);
        var unknown = check.Files.Single(f => f.FileName == "RAPORT_NIEZNANY.csv");
        Assert.Equal("nierozpoznany", unknown.Recognition);
        Assert.False(unknown.Matches);
        Assert.Empty(_store.Batches(10));                               // sprawdzenie niczego nie importuje
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Check_reports_missing_rabit_location_unavailable_folder_and_unchanged_files(string store)
    {
        Use(store);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        Assert.Contains(_import.Check().Locations, l => l.Name == "RABIT" && !l.Accessible && l.Status == ImportService.NoRabitLocation);
        _import.Run();
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", Path.Combine(_root, "brak-folderu"), Active: true));

        var check = _import.Check();

        var location = check.Locations.Single(l => l.Name == "RABIT test");
        Assert.False(location.Accessible);
        Assert.StartsWith("BRAK DOSTĘPU", location.Status);
        Assert.Equal(ImportService.WillSkip, Assert.Single(check.Files).Note);
    }

    [Theory]
    [MemberData(nameof(TestStores.Kinds), MemberType = typeof(TestStores))]
    public void Files_only_in_subfolders_are_reported(string store)
    {
        Use(store);
        var rabit = Path.Combine(_root, "rabit");
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", rabit, Active: true));
        CopySample(Path.Combine(rabit, "2026"), "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        Assert.Empty(run.Files);
        Assert.Contains(run.Issues, i => i.Level == CheckLevel.Warning && i.Element == "RABIT test" && i.Message.Contains("podfoldery: 2026"));
    }
}
