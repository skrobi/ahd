using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Files;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Import;

/// <summary>Import G1 na danych wzorcowych: decyzje, izolacja błędów, dane kanoniczne i kontrola przepływu.</summary>
public sealed class ImportServiceTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-import-").FullName;
    private readonly SourceConfigService _config;
    private readonly InMemoryImportStore _store;
    private readonly ImportService _import;

    public ImportServiceTests()
    {
        _config = new SourceConfigService(new InMemorySourceConfigStore(_services.Database, _services.Clock, _services.User), _services.Journal);
        SourceConfigSeed.EnsureSeeded(_config);
        _store = new InMemoryImportStore(_services.Database);
        _import = new ImportService(_store, _services.App(_root));
    }

    private string ImportFolder => _services.App(_root).Config.ImportFolder;

    private string CopySample(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, name);
        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), target, overwrite: true);
        return target;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Sample_file_is_imported_with_canonical_data_matching_expected_sums()
    {
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        var result = Assert.Single(run.Files);
        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.Equal("ACTUALS_PAF", result.SourceCode);
        Assert.Equal(6, result.Rows);
        Assert.Empty(run.Issues);

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
        Assert.Contains(_services.Journal.Recent(5), e => e.Message.StartsWith($"Import #{run.BatchId}"));
    }

    [Fact]
    public void Second_run_skips_unchanged_files()
    {
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        _import.Run();

        var second = _import.Run();

        Assert.Equal(FileDecisions.Skipped, Assert.Single(second.Files).Decision);
    }

    [Fact]
    public void Same_content_under_another_name_is_a_duplicate()
    {
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        _import.Run();
        CopySample(ImportFolder, "ACTUALS_PAF_02.csv");

        var run = _import.Run();

        var duplicate = run.Files.Single(f => f.FileName == "ACTUALS_PAF_02.csv");
        Assert.Equal(FileDecisions.Duplicate, duplicate.Decision);
        Assert.Contains("ACTUALS_PAF_01.csv", duplicate.Description);
    }

    [Fact]
    public void Unrecognized_corrupted_and_ignored_files_do_not_stop_the_import()
    {
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
        Assert.Contains(_services.Problems.ByReference($"import:{run.BatchId}"), p => p.Level == CheckLevel.Error);
    }

    [Fact]
    public void Unavailable_location_is_an_error_and_manual_folder_is_still_imported()
    {
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", Path.Combine(_root, "brak-folderu"), Active: true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        Assert.Contains(run.Issues, i => i.Level == CheckLevel.Error && i.Element == "RABIT test" && i.Message.StartsWith("Lokalizacja niedostępna"));
        Assert.Equal(FileDecisions.Imported, Assert.Single(run.Files).Decision);
    }

    [Fact]
    public void Same_name_in_two_locations_are_different_files()
    {
        var rabit = Path.Combine(_root, "rabit");
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", rabit, Active: true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        var other = CopySample(rabit, "ACTUALS_PAF_01.csv");
        File.AppendAllText(other, "2DI473;2DI473001003;51105550;x;x;x;PLN;1,00;PLN;1,00;USD;0,25;0,000;;;;;;;2026;2026-03-31;3\r\n");

        var run = _import.Run();

        Assert.Equal(2, run.Files.Count(f => f.Decision == FileDecisions.Imported));
    }

    [Fact]
    public void Longest_prefix_wins()
    {
        _config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS", "ACTUALS", "ogólny", ["A"], "", [], "", "", "", SourceParsers.None, true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        Assert.Equal("ACTUALS_PAF", Assert.Single(_import.Run().Files).SourceCode);
    }

    [Fact]
    public void One_definition_with_actuals_asterisk_prefix_imports_all_actuals_files()
    {
        // Wszystkie ACTUALS_… jako jedno źródło: istniejące definicje wyłączone, nowa z prefiksem „ACTUALS_*”.
        foreach (var d in _config.Definitions())
            _config.SaveDefinition(new DefinitionInput(d.DefinitionId, d.Version, d.Code, d.Prefix, d.ReportType, d.Columns, d.Grain,
                d.KeyColumns, d.PeriodMeaning, d.Currency, d.NumberFormat, d.Parser, Active: false));
        _config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS", "ACTUALS_*", "Koszty rzeczywiste CES", SourceParsers.ActualsColumns,
            "", [], "", "", "", SourceParsers.Actuals, true));
        CopySample(ImportFolder, "ACTUALS_PAF2_B6_AC1.csv");

        var result = Assert.Single(_import.Run().Files);

        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.Equal("ACTUALS", result.SourceCode);
    }

    [Fact]
    public void Changed_column_layout_keeps_raw_rows_but_no_canonical_data()
    {
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

    [Fact]
    public void Value_type_error_blocks_canonical_data_with_row_number()
    {
        Directory.CreateDirectory(ImportFolder);
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        lines[3] = lines[3].Replace("2 400,00;PLN;2 400,00", "2 400,00;PLN;dwa tysiące");
        File.WriteAllLines(Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), lines);

        var run = _import.Run();

        Assert.Contains(run.Issues, i => i.Message.StartsWith("Value in Obj. Crcy: 'dwa tysiące'") && i.Element!.EndsWith("wiersz danych 3"));
        var file = _store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!;
        Assert.Empty(_store.Actuals(file.Id));
    }

    [Fact]
    public void Excel_file_with_numeric_cells_is_imported()
    {
        Directory.CreateDirectory(ImportFolder);
        var sample = TabularFileReader.Read(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        var rows = sample.Rows.Select(r => (IReadOnlyList<object?>)r.Select((v, i) =>
            i is 7 or 9 or 11 or 12 && PolishNumber.TryParse(v, out var d) ? d : (object?)v).ToList());
        ExcelTableWriter.Write(Path.Combine(ImportFolder, "ACTUALS_PAF_01.xlsx"), "Sheet1", sample.Headers, rows);

        var run = _import.Run();

        var file = _store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!;
        Assert.Empty(run.Issues);
        Assert.Equal(2203.12m, _store.Actuals(file.Id).Sum(a => a.ValueRepCur));
    }

    [Fact]
    public void Cancelled_import_stops_and_is_marked()
    {
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var run = _import.Run(cancellation: cts.Token);

        Assert.True(run.Cancelled);
        Assert.Empty(run.Files);
        Assert.Equal("przerwany", _store.Batches(1).Single().Status);
    }
}
