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
        Assert.Equal(("8000123401", "10", "4500012301", "FV/2026/03/011"),
            (actuals[0]["OriginalOrderNumber"], actuals[0]["Item"], actuals[0]["PurchaseOrderNumber"], actuals[0]["InvoiceNumber"]));
        Assert.Null(actuals[0]["PartnerCctr"]);                                     // pole parsera bez kolumny w pliku
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
        var statuses = progress.Items.Where(p => p.FileName == "ACTUALS_PAF_01.csv").Select(p => p.Status!).ToList();
        Assert.Equal(   // etapy importu pliku na ekranie, potem decyzja
        [
            ImportService.StageDownload, ImportService.StageCheck, ImportService.StageRows, "4/4 kontrola w bazie (liczba wierszy i sumy)",
            "4/4 zapis treści pliku", "4/4 zatwierdzanie zapisu", "zaimportowany",
        ], statuses.Select(s => s.Split(" – ")[0].Split(" · ")[0]).Distinct());
        Assert.Contains(statuses, s => s.StartsWith($"{ImportService.StageDownload} – ") && s.Contains(" MB, kopia pliku · "));
        Assert.Contains(statuses, s => s.StartsWith($"{ImportService.StageRows} – 6 wierszy"));
        Assert.Equal("zakończony", _store.Batches(1).Single().Status);
    }

    [SqlFact]
    public void Import_cancelled_while_rows_are_written_leaves_nothing_in_database()
    {
        Use();
        var path = CopySample(ImportFolder, "ACTUALS_PAF_01.csv");
        using var cancel = new CancellationTokenSource();
        var progress = new Collect(p =>
        {
            if (p.Status?.StartsWith(ImportService.StageRows) == true)
                cancel.Cancel();   // „Przerwij” w trakcie zapisu wierszy
        });

        var run = _import.Run(progress, cancel.Token);

        Assert.True(run.Cancelled);
        Assert.Empty(run.Files);
        Assert.Null(_store.FindByHash(FileHash.Sha256(File.ReadAllBytes(path))));   // zapis wycofany – kolejny import pobierze plik
        Assert.Equal("przerwany – plik nie zapisany", progress.Items.Last(p => p.FileName == "ACTUALS_PAF_01.csv").Status);
        Assert.Equal("przerwany", _store.Batches(1).Single().Status);
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
        File.AppendAllText(other, "2DI473;2DI473001003;51105550;x;x;PLN;1,00;PLN;1,00;USD;0,25;0,000;;;;;;;;2026;2026-03-31;3;\r\n");

        var run = _import.Run();

        Assert.Equal(2, run.Files.Count(f => f.Decision == FileDecisions.Imported));
    }

    [SqlFact]
    public void Longest_prefix_wins()
    {
        Use();
        _config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS", "ACTUALS", "ogólny", SourceParsers.None, true));
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        Assert.Equal("ACTUALS_PAF", Assert.Single(_import.Run().Files).SourceCode);
    }

    [SqlFact]
    public void One_definition_with_actuals_asterisk_prefix_imports_all_actuals_files()
    {
        Use();
        // Wszystkie ACTUALS_… jako jedno źródło: istniejące definicje wyłączone, nowa z prefiksem „ACTUALS_*”.
        foreach (var d in _config.Definitions())
            Assert.True(_config.SaveDefinition(new DefinitionInput(d.DefinitionId, d.Version, d.Code, d.Prefix, d.ReportType, d.Parser, Active: false)).Success);
        _config.SaveDefinition(new DefinitionInput(null, null, "ACTUALS", "ACTUALS_*", "Koszty rzeczywiste CES", ActualsLayout.Parser, true));
        CopySample(ImportFolder, "ACTUALS_PAF2_B6_AC1.csv");

        var result = Assert.Single(_import.Run().Files);

        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.Equal("ACTUALS", result.SourceCode);
    }

    /// <summary>Zapisuje nową wersję parsera ACTUALS ze zmienionymi polami.</summary>
    private void ChangeActualsParser(Func<ParserField, ParserField> change)
    {
        var parser = _config.Parsers().Single(p => p.Code == ActualsLayout.Parser);
        var saved = _config.SaveParser(new ParserInput(parser.ParserId, parser.Version, parser.Code, parser.Name, parser.Fields.Select(change).ToList(), parser.Active));
        Assert.True(saved.Success, saved.Message + string.Join("; ", saved.Issues.Select(i => i.Message)));
    }

    [SqlFact]
    public void Missing_parser_column_is_an_error_file_is_not_stored_and_is_imported_after_parser_fix()
    {
        Use();
        Directory.CreateDirectory(ImportFolder);
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        File.WriteAllLines(Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), lines.Select(l => l[..l.LastIndexOf(';')]));   // bez Invoice Number

        var first = _import.Run();

        Assert.Equal(FileDecisions.Error, Assert.Single(first.Files).Decision);
        Assert.Contains(first.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("brak kolumn parsera ACTUALS: Invoice Number") && i.Message.Contains("nie zapisany w bazie"));
        Assert.Null(_store.FindByHash(Assert.Single(_store.Seen(first.BatchId)).Sha256!));   // ani plik, ani wiersze surowe
        Assert.Equal(ImportService.WillImport, Assert.Single(_import.Check().Files).Note);   // nie jest „bez zmian”

        ChangeActualsParser(f => f.Field == "InvoiceNumber" ? f with { Column = "" } : f);   // pole przestaje być czytane z pliku
        var second = _import.Run();   // ten sam plik, te same metadane – pobrany ponownie

        Assert.Equal(FileDecisions.Imported, Assert.Single(second.Files).Decision);
        var file = _store.FindByHash(Assert.Single(_store.Seen(second.BatchId)).Sha256!)!;
        Assert.Equal("utworzone", file.CanonicalStatus);
        Assert.Equal(6, Canonical(file.Id).Count);
        Assert.All(Canonical(file.Id), r => Assert.Null(r["InvoiceNumber"]));
    }

    [SqlFact]
    public void Completed_import_resolves_problems_of_earlier_imports_and_cancelled_import_does_not()
    {
        Use();
        Directory.CreateDirectory(ImportFolder);
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        File.WriteAllLines(Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), lines.Select(l => l[..l.LastIndexOf(';')]));   // bez Invoice Number
        var first = _import.Run();
        var firstProblems = _app.Problems.ByReference(ImportBatchRow.ProblemReference(first.BatchId));
        Assert.Contains(firstProblems, p => p.Level == CheckLevel.Error && p.Message.Contains("brak kolumn parsera"));

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            Assert.True(_import.Run(cancellation: cts.Token).Cancelled);
        }
        Assert.All(_app.Problems.ByReference(ImportBatchRow.ProblemReference(first.BatchId)), p => Assert.False(p.Resolved));   // przerwany – bez zmian

        File.Copy(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), overwrite: true);   // poprawiony plik
        var fixedRun = _import.Run();

        Assert.Equal(FileDecisions.Imported, Assert.Single(fixedRun.Files).Decision);
        Assert.All(_app.Problems.ByReference(ImportBatchRow.ProblemReference(first.BatchId)), p =>
        {
            Assert.True(p.Resolved);
            Assert.Equal((_services.User.Account, $"nieaktualny – stan z importu #{fixedRun.BatchId}"), (p.ResolvedBy, p.Resolution));
        });
        Assert.All(_app.Problems.Open(), p => Assert.Equal(ImportBatchRow.ProblemReference(fixedRun.BatchId), p.Reference));   // tylko bieżący stan
    }

    [SqlFact]
    public void Extra_column_in_file_is_imported_raw_only()
    {
        Use();
        Directory.CreateDirectory(ImportFolder);
        var lines = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        File.WriteAllLines(Path.Combine(ImportFolder, "ACTUALS_PAF_01.csv"), lines.Select((l, i) => (i == 0 ? "Dodatkowa;" : "x;") + l));

        var result = Assert.Single(_import.Run().Files);

        Assert.Equal(FileDecisions.Imported, result.Decision);
        Assert.EndsWith("kolumny spoza parsera (tylko w treści pliku): Dodatkowa", result.Description);
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

    [SqlFact]
    public void Source_with_parser_created_in_application_is_imported_without_table_changes()
    {
        Use();
        var parserSaved = _config.SaveParser(new ParserInput(null, null, "KOSZTY", "Koszty – skrót",
        [
            new("Wbs", "WBS Element", FieldTypes.Text, 50, Required: true), new("Pln", "Value in Obj. Crcy", FieldTypes.Decimal),
            new("Rok", "Fiscal Year", FieldTypes.Integer),
        ], true));
        Assert.True(parserSaved.Success, parserSaved.Message);
        Assert.True(_config.SaveDefinition(new DefinitionInput(null, null, "SKROT", "SKROT_", "test", "KOSZTY", true)).Success);
        CopySample(ImportFolder, "SKROT_01.csv");

        var run = _import.Run();

        Assert.Equal(FileDecisions.Imported, Assert.Single(run.Files).Decision);
        var rows = _store.CanonicalRows(_store.ActiveParser("KOSZTY")!, _store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!)!.Id);
        Assert.Equal(6, rows.Count);
        Assert.Equal(["FileId", "RowNumber", "ParserVersion", "Wbs", "Pln", "Rok"], rows[0].Keys);
        Assert.Equal(10574.11m, rows.Sum(r => (decimal)r["Pln"]!));
    }

    [SqlFact]
    public void Canonical_data_not_matching_file_in_database_rolls_back_whole_file()
    {
        Use();
        var parser = _store.ActiveParser(ActualsLayout.Parser)!;
        var fields = parser.Fields.Where(f => f.Field is "WbsElement" or "ValueObjCrcy").ToList();
        var batch = _store.BeginBatch(_services.Clock.Now, @"PZL\test", "PC-1", "0.16.0");
        SourceFileRow FileRow(char hash) => new(0, new string(hash, 64), "RABIT", $"ACTUALS_PAF_{hash}.csv", "ACTUALS_PAF", 10, _services.Clock.Now, "csv", null,
            "utf-8", ";", ["WBS Element", "Value in Obj. Crcy"], "sygnatura", 2, batch, _services.Clock.Now, @"PZL\test", "", 0, null);
        CanonicalRow[] rows = [new(1, ["A.1", 10.5m]), new(2, ["A.2", 5m])];
        int Count(string table)
        {
            using var connection = _database!.Sql.Open();
            return Dapper.SqlMapper.ExecuteScalar<int>(connection, $"SELECT COUNT(*) FROM {_database.Sql.Table(table)}");
        }

        var content = Path.Combine(_root, "plik.csv");
        File.WriteAllText(content, "WBS Element;Value in Obj. Crcy\nA.1;10,5\nA.2;5\n");
        var stages = new List<string>();

        var error = Assert.Throws<CanonicalFlowException>(() => _store.StoreFile(FileRow('a'), content,
            new CanonicalData(parser, fields, rows, () => new CanonicalTotals(2, new Dictionary<string, decimal> { ["ValueObjCrcy"] = 16m }))));
        Assert.Throws<ContentRejectedException>(() => _store.StoreFile(FileRow('c'), content,
            new CanonicalData(parser, fields, rows, () => throw new ContentRejectedException("błędy wartości"))));
        var stored = _store.StoreFile(FileRow('b'), content,
            new CanonicalData(parser, fields, rows, () => new CanonicalTotals(2, new Dictionary<string, decimal> { ["ValueObjCrcy"] = 15.5m })), stages.Add)!;

        Assert.StartsWith("dane kanoniczne w bazie niezgodne z plikiem (wiersze 2/2, suma Value in Obj. Crcy 16/15", error.Message);
        Assert.Null(_store.FindByHash(new string('a', 64)));   // plik, treść i wiersze wycofane razem
        Assert.Null(_store.FindByHash(new string('c', 64)));   // błędy treści wykryte po przesłaniu wierszy – też wycofane
        Assert.Equal(["kontrola w bazie (liczba wierszy i sumy)", "zapis treści pliku", "zatwierdzanie zapisu"], stages);
        Assert.Equal((2, 2), (_store.File(stored.FileId)!.RowCount, _store.File(stored.FileId)!.CanonicalRows));
        Assert.Equal(new string?[] { "A.2", "5" }, _store.RawRows(stored.FileId)[1].Values);   // treść pliku (GZip) z bazy
        Assert.Equal((2, 15.5m), (stored.CanonicalRows, stored.Sums["ValueObjCrcy"]));
        Assert.Equal((1, 2), (Count("meta.SourceFileContent"), Count("can.Row")));
        Assert.Equal(["A.1", "A.2"], _store.CanonicalRows(parser, stored.FileId).Select(r => r["WbsElement"]));
    }

    [SqlFact]
    public void Required_field_empty_in_a_row_is_an_error_and_file_is_not_stored()
    {
        Use();
        ChangeActualsParser(f => f.Field == "PartnerObject" ? f with { Required = true } : f);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");   // Partner object pusty w części wierszy

        var run = _import.Run();

        Assert.Equal(FileDecisions.Error, Assert.Single(run.Files).Decision);
        Assert.Contains(run.Issues, i => i.Message == "Partner object: pole wymagane");
        Assert.Null(_store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!));
    }

    [SqlFact]
    public void Inactive_parser_is_an_error_and_file_is_not_stored()
    {
        Use();
        var parser = _config.Parsers().Single(p => p.Code == ActualsLayout.Parser);
        Assert.True(_config.SaveParser(new ParserInput(parser.ParserId, parser.Version, parser.Code, parser.Name, parser.Fields, Active: false)).Success);
        CopySample(ImportFolder, "ACTUALS_PAF_01.csv");

        var run = _import.Run();

        Assert.Equal(FileDecisions.Error, Assert.Single(run.Files).Decision);
        Assert.Contains(run.Issues, i => i.Message.Contains("parser ACTUALS definicji ACTUALS_PAF nie istnieje albo jest nieaktywny"));
        Assert.Null(_store.FindByHash(Assert.Single(_store.Seen(run.BatchId)).Sha256!));
    }

    [SqlFact]
    public void Excel_file_with_numeric_cells_is_imported()
    {
        Use();
        Directory.CreateDirectory(ImportFolder);
        var sample = TabularFileReader.Read(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"));
        var rows = sample.Rows.Select(r => (IReadOnlyList<object?>)r.Select((v, i) =>
            i is 6 or 8 or 10 or 11 && PolishNumber.TryParse(v, out var d) ? d : (object?)v).ToList());
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
