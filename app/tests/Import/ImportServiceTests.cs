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

    /// <summary>
    /// Magazyny testu w bazie testowej z danymi startowymi (migracja 002: definicje ACTUALS_PAF i ACTUALS_CES);
    /// lokalizacja RABIT z presetów wyłączona – testy nie sięgają do SharePoint.
    /// </summary>
    private void Use()
    {
        _database = new TestDatabase(presets: true);
        _app = _services.App(_root, _database.Sql);
        _config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, _services.Clock, _services.User), _app.Journal);
        _store = new SqlImportStore(_database.Sql);
        var rabit = Assert.Single(_config.Locations());
        Assert.True(_config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        _import = new ImportService(_store, _app);
    }

    private string ImportFolder => _app.Config.ImportFolder;

    private IReadOnlyList<ParserField> ParserFields => _store.ActiveParser(ActualsLayout.Parser)!.Fields;

    /// <summary>Dane kanoniczne pliku z tabeli parsera ACTUALS.</summary>
    private IReadOnlyList<IReadOnlyDictionary<string, object?>> Canonical(long fileId) => _store.CanonicalRows(_store.ActiveParser(ActualsLayout.Parser)!, fileId);

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

    [SqlFact]
    public void Sample_file_is_imported_with_canonical_data_matching_expected_sums()
    {
        Use();
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        var result = Assert.Single(run.Files);
        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.Equal("ACTUALS_PAF", result.SourceCode);
        Assert.Equal(6, result.Rows);
        Assert.Equal(ImportService.NoRabitLocation, Assert.Single(run.Issues).Message);   // lokalizacja z presetów wyłączona

        var seen = Assert.Single(_store.Seen(run.BatchId));
        var file = _store.FindByHash(seen.Sha256!)!;
        Assert.Equal("utworzone", file.CanonicalStatus);
        Assert.Equal(6, _store.RawRows(file.Id).Count);
        var actuals = Canonical(file.Id);
        Assert.Equal(6, actuals.Count);
        Assert.Equal(10574.11m, actuals.Sum(a => (decimal)a["ValueObjCrcy"]!));     // testdata/README.md
        Assert.Equal(2203.12m, actuals.Sum(a => (decimal)a["ValueRepCur"]!));
        Assert.Equal("0051105550", actuals[0]["CostElement"]);
        Assert.Equal(-230.40m, (decimal)actuals[4]["ValueObjCrcy"]!);               // minus na końcu (zapis SAP)
        Assert.Equal(new DateTime(2026, 3, 29), actuals[0]["CreatedOn"]);
        Assert.Null(actuals[0]["InvoiceNumber"]);                                   // pole parsera bez kolumny w pliku
        Assert.Equal("zakończony", _store.Batches(1).Single().Status);
        Assert.Contains(_app.Journal.Recent(5), e => e.Message.StartsWith($"Import #{run.BatchId}"));
    }

    [SqlFact]
    public void Second_run_skips_unchanged_files()
    {
        Use();
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        _import.Run();

        var second = _import.Run();

        Assert.Equal(FileDecisions.Skipped, Assert.Single(second.Files).Decision);
    }

    [SqlFact]
    public void Same_content_under_another_name_is_a_duplicate()
    {
        Use();
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        _import.Run();
        CopySample(ImportFolder, "ACTUALS_PAF_02.csv");

        var run = _import.Run();

        var duplicate = run.Files.Single(f => f.FileName == "ACTUALS_PAF_02.csv");
        Assert.Equal(FileDecisions.Duplicate, duplicate.Decision);
        Assert.Contains("ACTUALS_PAF_01.csv", duplicate.Description);
    }

    [SqlFact]
    public void Unrecognized_corrupted_and_ignored_files_do_not_stop_the_import()
    {
        Use();
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

    [SqlFact]
    public void Unavailable_location_is_an_error_and_manual_folder_is_still_imported()
    {
        Use();
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", Path.Combine(_root, "brak-folderu"), Active: true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        Assert.Contains(run.Issues, i => i.Level == CheckLevel.Error && i.Element == "RABIT test" && i.Message.StartsWith("Lokalizacja niedostępna"));
        Assert.Equal(FileDecisions.Imported, Assert.Single(run.Files).Decision);
    }

    [SqlFact]
    public void Unavailable_sharepoint_location_explains_gateway_login()
    {
        Use();
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

    [SqlFact]
    public void Import_start_is_in_history_and_file_status_changes_during_import()
    {
        Use();
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

    [SqlFact]
    public void Import_does_not_start_while_another_person_imports()
    {
        Use();
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        var other = new TestServices().App(_root, _database!.Sql);   // druga osoba – ten sam folder sieciowy i baza
        using var lease = other.Locks.TryAcquire(ImportService.LockName, out _);
        Assert.NotNull(lease);

        var run = _import.Run();

        Assert.NotNull(run.NotStarted);
        Assert.StartsWith(@"Import nie rozpoczęty – trwa import: PZL\analityk (", run.NotStarted);
        Assert.Empty(run.Files);
        Assert.Empty(_store.Batches(10));
        Assert.NotNull(_import.RunningImport());
    }

    [SqlFact]
    public void Lock_is_released_after_import_and_unfinished_import_is_marked()
    {
        Use();
        var stale = _store.BeginBatch(_services.Clock.Now, "ktos", "PC1", "test");   // np. awaria aplikacji w trakcie importu

        var run = _import.Run();

        Assert.Null(run.NotStarted);
        Assert.Null(_import.RunningImport());
        Assert.Equal(ImportBatchStatus.Abandoned, _store.Batches(10).Single(b => b.Id == stale).Status);
        Assert.Null(_import.Run().NotStarted);
    }

    [SqlFact]
    public void Same_name_in_two_locations_are_different_files()
    {
        Use();
        var rabit = Path.Combine(_root, "rabit");
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", rabit, Active: true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        var other = CopySample(rabit, "ACTUALS_PAF_01.csv");
        File.AppendAllText(other, "2DI473;2DI473001003;51105550;x;x;x;PLN;1,00;PLN;1,00;USD;0,25;0,000;;;;;;;2026;2026-03-31;3\r\n");

        var run = _import.Run();

        Assert.Equal(2, run.Files.Count(f => f.Decision == FileDecisions.Imported));
    }

    [SqlFact]
    public void Longest_prefix_wins()
    {
        Use();
        _config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS", "ACTUALS", "ogólny", ["A"], SourceParsers.None, true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        Assert.Equal("ACTUALS_PAF", Assert.Single(_import.Run().Files).SourceCode);
    }

    [SqlFact]
    public void One_definition_with_actuals_asterisk_prefix_imports_all_actuals_files()
    {
        Use();
        // Wszystkie ACTUALS_… jako jedno źródło: istniejące definicje wyłączone, nowa z prefiksem „ACTUALS_*”.
        foreach (var d in _config.Definitions())
            Assert.True(_config.SaveDefinition(new DefinitionInput(d.DefinitionId, d.Version, d.Code, d.Prefix, d.ReportType, d.Columns, d.Parser, Active: false, d.Mapping)).Success);
        _config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS", "ACTUALS_*", "Koszty rzeczywiste CES", ActualsLayout.Columns,
            ActualsLayout.Parser, true, ActualsLayout.Mapping(ParserFields)));
        CopySample(ImportFolder, "ACTUALS_PAF2_B6_AC1.csv");

        var result = Assert.Single(_import.Run().Files);

        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.Equal("ACTUALS", result.SourceCode);
    }

    [SqlFact]
    public void Changed_column_layout_is_an_error_file_is_not_stored_and_is_imported_after_definition_fix()
    {
        Use();
        Directory.CreateDirectory(ImportFolder);
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        File.WriteAllLines(Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), lines.Select((l, i) => (i == 0 ? "Dodatkowa;" : "x;") + l)); // dodatkowa kolumna

        var first = _import.Run();

        Assert.Equal(FileDecisions.Error, Assert.Single(first.Files).Decision);
        Assert.Contains(first.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("układ kolumn niezgodny") && i.Message.Contains("nie zapisany w bazie"));
        Assert.Null(_store.FindByHash(Assert.Single(_store.Seen(first.BatchId)).Sha256!));   // ani plik, ani wiersze surowe
        Assert.Equal(ImportService.WillImport, Assert.Single(_import.Check().Files).Note);   // nie jest „bez zmian”

        var paf = _config.Definitions().Single(d => d.Code == "ACTUALS_PAF");
        Assert.True(_config.SaveDefinition(new DefinitionInput(paf.DefinitionId, paf.Version, paf.Code, paf.Prefix, paf.ReportType,
            ["Dodatkowa", .. ActualsLayout.Columns], paf.Parser, paf.Active, paf.Mapping)).Success);
        var second = _import.Run();   // ten sam plik, te same metadane – pobrany ponownie

        Assert.Equal(FileDecisions.Imported, Assert.Single(second.Files).Decision);
        var file = _store.FindByHash(Assert.Single(_store.Seen(second.BatchId)).Sha256!)!;
        Assert.Equal("utworzone", file.CanonicalStatus);
        Assert.Equal(6, Canonical(file.Id).Count);
    }

    [SqlFact]
    public void Value_type_error_is_an_error_with_row_number_and_file_is_not_stored()
    {
        Use();
        Directory.CreateDirectory(ImportFolder);
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        lines[3] = lines[3].Replace("2 400,00;PLN;2 400,00", "2 400,00;PLN;dwa tysiące");
        File.WriteAllLines(Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), lines);

        var run = _import.Run();

        var result = Assert.Single(run.Files);
        Assert.Equal(FileDecisions.Error, result.Decision);
        Assert.Equal("1 błędów wartości – nie zapisany", result.Description);
        Assert.Contains(run.Issues, i => i.Message.StartsWith("Value in Obj. Crcy: 'dwa tysiące'") && i.Element!.EndsWith("wiersz danych 3"));
        Assert.Null(_store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!));
    }

    /// <summary>Nowy układ raportu (bez 3 kolumn opisowych, z 4 nowymi) – kolumny i mapowanie ustawione w definicji.</summary>
    private static readonly string[] NewLayout =
    [
        "Project Definition", "WBS Element", "Cost Element", "Cost element name", "CO object name", "Transaction Currency", "Value TranCurr",
        "Object Currency", "Value in Obj. Crcy", "Report currency", "Val.in rep.cur.", "Total Quantity", "Partner Object Class", "Partner object",
        "Original material", "Original material description", "Original Order Number", "Item", "Purchase order number", "Fiscal Year",
        "Created on", "Period", "Invoice Number",
    ];

    /// <summary>Plik wzorcowy przepisany na nowy układ; nowe kolumny: ORD{n}, {n}0, PO{n}, FV/{n}.</summary>
    private void WriteNewLayoutFile(string name)
    {
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        var header = lines[0].Split(';');
        var output = new List<string> { string.Join(";", NewLayout) };
        for (var r = 1; r < lines.Length; r++)
        {
            var values = header.Zip(lines[r].Split(';')).ToDictionary(p => p.First, p => p.Second);
            values["Original Order Number"] = $"ORD{r}";
            values["Item"] = $"{r}0";
            values["Purchase order number"] = $"PO{r}";
            values["Invoice Number"] = $"FV/{r}";
            output.Add(string.Join(";", NewLayout.Select(c => values[c])));
        }
        Directory.CreateDirectory(ImportFolder);
        File.WriteAllLines(Path.Combine(ImportFolder, name), output);
    }

    [SqlFact]
    public void New_layout_is_imported_by_mapping_new_columns_to_parser_fields()
    {
        Use();
        var paf = _config.Definitions().Single(d => d.Code == "ACTUALS_PAF");
        var saved = _config.SaveDefinition(new DefinitionInput(paf.DefinitionId, paf.Version, paf.Code, paf.Prefix, paf.ReportType, NewLayout,
            paf.Parser, paf.Active, ActualsLayout.Mapping(ParserFields, NewLayout)));
        Assert.True(saved.Success, string.Join("; ", saved.Issues.Select(i => i.Message)));
        WriteNewLayoutFile("ACTUALS_PAF2_B6_AC1.csv");

        var run = _import.Run();

        var result = Assert.Single(run.Files);
        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.Contains("dane kanoniczne (ACTUALS): 6 wierszy, suma Value TranCurr", result.Description);
        var rows = Canonical(_store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!.Id);
        Assert.Equal(6, rows.Count);
        Assert.Equal(("ORD1", "10", "PO1", "FV/1"), (rows[0]["OriginalOrderNumber"], rows[0]["Item"], rows[0]["PurchaseOrderNumber"], rows[0]["InvoiceNumber"]));
        Assert.Null(rows[0]["PartnerCctr"]);                         // kolumny nie ma w nowym układzie
        Assert.Equal(10574.11m, rows.Sum(a => (decimal)a["ValueObjCrcy"]!));
    }

    [SqlFact]
    public void Source_with_parser_created_in_application_is_imported_to_its_own_table()
    {
        Use();
        var parserSaved = _config.SaveParser(new ParserInput(null, null, "KOSZTY", "Koszty – skrót",
            [new("Wbs", "WBS Element", FieldTypes.Text, 50), new("Pln", "Value in Obj. Crcy", FieldTypes.Decimal), new("Rok", "Fiscal Year", FieldTypes.Integer)], true));
        Assert.True(parserSaved.Success, parserSaved.Message);
        var fields = _config.Parsers().Single(p => p.Code == "KOSZTY").Fields;
        Assert.True(_config.SaveDefinition(new DefinitionInput(null, null, "SKROT", "SKROT_", "test", ActualsLayout.Columns, "KOSZTY", true,
            SourceConfigService.MapByName(ActualsLayout.Columns, fields))).Success);
        CopySample(ImportFolder, "SKROT_01.csv");

        var run = _import.Run();

        Assert.Equal(FileDecisions.Imported, Assert.Single(run.Files).Decision);
        var rows = _store.CanonicalRows(_store.ActiveParser("KOSZTY")!, _store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!.Id);
        Assert.Equal(6, rows.Count);
        Assert.Equal(["FileId", "RowNumber", "ParserVersion", "Wbs", "Pln", "Rok"], rows[0].Keys);
        Assert.Equal(10574.11m, rows.Sum(r => (decimal)r["Pln"]!));
    }

    [SqlFact]
    public void Required_field_empty_in_a_row_is_an_error_and_file_is_not_stored()
    {
        Use();
        var paf = _config.Definitions().Single(d => d.Code == "ACTUALS_PAF");
        var mapping = paf.Mapping.Select(m => m.Field == "PartnerObject" ? m with { Required = true } : m).ToList();
        Assert.True(_config.SaveDefinition(new DefinitionInput(paf.DefinitionId, paf.Version, paf.Code, paf.Prefix, paf.ReportType, paf.Columns,
            paf.Parser, paf.Active, mapping)).Success);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");   // Partner object pusty w części wierszy

        var run = _import.Run();

        Assert.Equal(FileDecisions.Error, Assert.Single(run.Files).Decision);
        Assert.Contains(run.Issues, i => i.Message == "Partner object: pole wymagane");
        Assert.Null(_store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!));
    }

    [SqlFact]
    public void Definition_with_parser_but_without_mapping_is_an_error()
    {
        Use();
        using (var connection = _database!.Sql.Open())   // np. definicja o niestandardowym układzie sprzed migracji 004
            Dapper.SqlMapper.Execute(connection, $"UPDATE {_database.Sql.Table("meta.SourceDefinition")} SET Mapping = NULL WHERE Code = 'ACTUALS_PAF'");
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        Assert.Equal(FileDecisions.Error, Assert.Single(run.Files).Decision);
        Assert.Contains(run.Issues, i => i.Message.Contains("definicja ACTUALS_PAF nie ma mapowania kolumn na pola parsera ACTUALS"));
        Assert.Null(_store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!));
    }

    [SqlFact]
    public void Excel_file_with_numeric_cells_is_imported()
    {
        Use();
        Directory.CreateDirectory(ImportFolder);
        var sample = TabularFileReader.Read(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        var rows = sample.Rows.Select(r => (IReadOnlyList<object?>)r.Select((v, i) =>
            i is 7 or 9 or 11 or 12 && PolishNumber.TryParse(v, out var d) ? d : (object?)v).ToList());
        ExcelTableWriter.Write(Path.Combine(ImportFolder, "ACTUALS_PAF_01.xlsx"), "Sheet1", sample.Headers, rows);

        var run = _import.Run();

        var file = _store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!;
        Assert.DoesNotContain(run.Issues, i => i.Message != ImportService.NoRabitLocation);
        Assert.Equal(2203.12m, Canonical(file.Id).Sum(a => (decimal)a["ValueRepCur"]!));
    }

    [SqlFact]
    public void Cancelled_import_stops_and_is_marked()
    {
        Use();
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var run = _import.Run(cancellation: cts.Token);

        Assert.True(run.Cancelled);
        Assert.Empty(run.Files);
        Assert.Equal("przerwany", _store.Batches(1).Single().Status);
    }

    [SqlFact]
    public void Check_shows_access_files_recognition_and_subfolders_without_importing()
    {
        Use();
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

    [SqlFact]
    public void Check_reports_missing_rabit_location_unavailable_folder_and_unchanged_files()
    {
        Use();
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

    [SqlFact]
    public void Files_only_in_subfolders_are_reported()
    {
        Use();
        var rabit = Path.Combine(_root, "rabit");
        _config.SaveLocation(new LocationInput(null, null, "RABIT test", rabit, Active: true));
        CopySample(Path.Combine(rabit, "2026"), "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        Assert.Empty(run.Files);
        Assert.Contains(run.Issues, i => i.Level == CheckLevel.Warning && i.Element == "RABIT test" && i.Message.Contains("podfoldery: 2026"));
    }
}
