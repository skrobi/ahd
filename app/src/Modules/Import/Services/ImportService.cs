using System.Globalization;
using System.IO;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using Serilog;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Import źródeł G1 (docs/pipeline-fazy.md): wszystkie aktywne lokalizacje RABIT i folder Do_importu. Dla każdego
/// pliku decyzja: nierozpoznany (brak prefiksu – WARNING), pominięty (te same metadane), duplikat (ten sam
/// SHA-256), zaimportowany (treść pliku + dane kanoniczne) albo błąd. Błąd pliku lub niedostępna lokalizacja
/// nie zatrzymuje pozostałych. Kontrola przepływu: wiersze i sumy kwot kanonicznych = kolumny pliku (także w bazie po zapisie).
/// </summary>
public sealed class ImportService(IImportStore store, AppServices services, Func<FileDownloader>? downloader = null)
{
    public const string StageDownload = "1/4 pobieranie na dysk";
    public const string StageCheck = "2/4 sprawdzanie pliku (duplikat, nagłówek, parser)";
    public const string StageRows = "3/4 odczyt i zapis wierszy do bazy";
    public const string StageCount = "3/4 liczenie wierszy";
    public const string StageFinish = "4/4";

    public const string Area = "Import";
    public const string LockName = "import";
    public const string WillImport = "zostanie zaimportowany";
    public const string WillSkip = "zostanie pominięty – bez zmian od ostatniego importu";
    private const string ManualFolderName = "Do_importu";
    public const string NoRabitLocation =
        "Brak aktywnej lokalizacji RABIT – import czyta tylko folder Do_importu; w Administracji zaznacz „Aktywna” przy lokalizacji.";

    /// <summary>Log importu (plik logs\pzl-ev-*.log obok exe): ścieżki, dostęp, decyzje, pełne błędy.</summary>
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    /// <summary>Pobieranie plików bieżącego importu (Run).</summary>
    private FileDownloader _downloader = null!;

    /// <summary>Kto teraz importuje (blokada wspólna dla wszystkich użytkowników); null – nikt.</summary>
    public LockHolder? RunningImport() => services.Locks.Holder(LockName);

    public IReadOnlyList<ImportLocation> Locations() =>
        store.ActiveLocations().Select(l => new ImportLocation(l.Name, WebDavPath.ToUnc(l.Path), false, l.Path))
            .Append(new ImportLocation(ManualFolderName, services.Config.ImportFolder, true, services.Config.ImportFolder))
            .ToList();

    /// <summary>
    /// Sprawdzenie źródeł bez importu: dostęp do każdej lokalizacji, pliki i to, jak zostałyby rozpoznane;
    /// wynik także w logu.
    /// </summary>
    public SourcesCheckResult Check()
    {
        var definitions = store.ActiveDefinitions();
        LogDefinitions(definitions);
        var locations = new List<LocationCheck>();
        var files = new List<FileCheck>();
        if (store.ActiveLocations().Count == 0)
        {
            locations.Add(new LocationCheck("RABIT", "", "", false, NoRabitLocation));
            Logger.Warning(NoRabitLocation);
        }
        foreach (var location in Locations())
        {
            try
            {
                var (found, ignored, subfolders) = ListFiles(location);
                var matching = found.Where(f => SourceMatcher.Match(f.Name, definitions) is not null).ToList();
                var newest = matching.Count > 0 ? $" (najnowszy z {matching.Max(f => f.LastWriteTime):yyyy-MM-dd HH:mm})" : "";
                var status = $"dostęp OK: plików {found.Count}, pasujących do definicji {matching.Count}{newest}" +
                             (ignored.Count > 0 ? $", pominiętych tymczasowych {ignored.Count}" : "") +
                             (subfolders.Count > 0 ? $", podfoldery (import ich nie czyta): {string.Join(", ", subfolders.Take(10))}" : "");
                locations.Add(new LocationCheck(location.Name, location.ConfiguredPath, location.Path, true, status));
                foreach (var file in found)
                {
                    var definition = SourceMatcher.Match(file.Name, definitions);
                    var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
                    var last = definition is null ? null : store.LastSettled(location.Path, file.Name);
                    var recognition = definition is null ? "nierozpoznany" : $"{definition.Code} (prefiks {SourceMatcher.Prefix(definition.Prefix)})";
                    var note = definition is null
                        ? "żaden aktywny prefiks nie pasuje do początku nazwy"
                        : last is not null && last.Size == file.Length && last.ModifiedAt == modified
                            ? WillSkip
                            : TabularFileReader.IsSupported(file.Name) ? WillImport : $"format {file.Extension} nieobsługiwany";
                    files.Add(new FileCheck(location.Name, file.Name, file.Length, modified, recognition, note, definition is not null));
                    Logger.Information("Sprawdzenie: {Location} / {File} ({Size} B, {Modified:u}) → {Recognition}; {Note}",
                        location.Name, file.Name, file.Length, modified, recognition, note);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                var status = $"BRAK DOSTĘPU: {ex.GetType().Name}: {ex.Message}" + (WebDavPath.IsWebDav(location.Path) ? $" {WebDavPath.AccessHint}" : "");
                locations.Add(new LocationCheck(location.Name, location.ConfiguredPath, location.Path, false, status));
                Logger.Error(ex, "Sprawdzenie: lokalizacja {Location} niedostępna ({Path})", location.Name, location.Path);
            }
        }
        return new SourcesCheckResult(locations, files);
    }

    /// <summary>Pliki lokalizacji (bez podfolderów), pliki tymczasowe pominięte; zapis do logu. Brak dostępu = wyjątek.</summary>
    private static (List<FileInfo> Files, List<string> Ignored, List<string> Subfolders) ListFiles(ImportLocation location)
    {
        Logger.Information("Lokalizacja {Location}: zapisana ścieżka {Configured}, czytana ścieżka {Path}", location.Name, location.ConfiguredPath, location.Path);
        if (location.IsManualFolder)
            Directory.CreateDirectory(location.Path);
        var dir = new DirectoryInfo(location.Path);
        if (!dir.Exists)
            throw new DirectoryNotFoundException("folder nie istnieje albo brak dostępu (Directory.Exists = false)");
        var (all, subfolders) = FolderEntries.List(dir.FullName);
        var ignored = all.Where(f => SourceMatcher.IsIgnored(f.Name)).Select(f => f.Name).ToList();
        var files = all.Where(f => !SourceMatcher.IsIgnored(f.Name)).ToList();
        Logger.Information("Lokalizacja {Location}: plików {Count} ({Files}); pominięte tymczasowe: {Ignored}; podfoldery: {Subfolders}",
            location.Name, files.Count, string.Join(", ", files.Select(f => f.Name)), string.Join(", ", ignored), string.Join(", ", subfolders));
        return (files, ignored, subfolders);
    }

    private static void LogDefinitions(IReadOnlyList<SourceDefinitionRow> definitions) =>
        Logger.Information("Aktywne definicje źródeł: {Definitions}",
            definitions.Count == 0 ? "BRAK" : string.Join("; ", definitions.Select(d => $"{d.Code}: prefiks '{d.Prefix}', parser {(d.Parser.Length == 0 ? "brak" : d.Parser)}")));

    /// <summary>
    /// Import ze wszystkich lokalizacji. Tylko jedna osoba naraz (blokada operacji): gdy import trwa u kogoś
    /// innego – wynik z NotStarted. Wpis „w toku” w historii od początku importu.
    /// </summary>
    /// <summary>
    /// Zakończony import ocenia wszystkie pliki od nowa (plik z błędem nie trafia do bazy, więc jest czytany ponownie),
    /// więc problemy wcześniejszych importów są nieaktualne – rozwiązane automatycznie; to, co nadal jest błędne, ma
    /// problem w bieżącym imporcie. Przerwany import niczego nie rozwiązuje.
    /// </summary>
    private void ResolveEarlierProblems(long batchId, string reference)
    {
        var stale = services.Problems.Open()
            .Where(p => p.Area == Area && p.Reference is { } r && r.StartsWith(ImportBatchRow.ProblemReferencePrefix, StringComparison.Ordinal) && r != reference)
            .Select(p => p.Id)
            .ToList();
        var resolved = services.Problems.Resolve(stale, $"nieaktualny – stan z importu #{batchId}");
        if (resolved > 0)
            Logger.Information("Import #{Batch}: rozwiązane problemy wcześniejszych importów: {Count}", batchId, resolved);
    }

    public ImportRunResult Run(IProgress<ImportProgress>? progress = null, CancellationToken cancellation = default)
    {
        using var lease = services.Locks.TryAcquire(LockName, out var holder);
        if (lease is null)
        {
            var busy = $"Import nie rozpoczęty – trwa import: {holder!.Text}.";
            Logger.Warning(busy);
            return new ImportRunResult(0, [], [Issue.Warning(busy, "import")], false) { NotStarted = busy };
        }

        store.AbandonRunning(services.Clock.Now);
        var definitions = store.ActiveDefinitions();
        var batchId = store.BeginBatch(services.Clock.Now, services.User.Account, Environment.MachineName, services.AppVersion);
        services.Journal.Add(Area, $"Import #{batchId} rozpoczęty ({services.User.Account}, {Environment.MachineName})");
        Logger.Information("Import #{Batch} start – {User} na {Machine}, wersja {Version}", batchId, services.User.Account, Environment.MachineName, services.AppVersion);
        progress?.Report(new ImportProgress($"Import #{batchId} rozpoczęty…", BatchId: batchId));
        LogDefinitions(definitions);
        using var fetch = downloader?.Invoke() ?? new FileDownloader();
        fetch.CleanStale();
        _downloader = fetch;
        var reference = ImportBatchRow.ProblemReference(batchId);
        var results = new List<FileResult>();
        var issues = new List<Issue>();
        var cancelled = false;

        void Problem(string check, Issue issue)
        {
            issues.Add(issue);
            services.Problems.Add(Area, check, issue, reference);
        }

        if (store.ActiveLocations().Count == 0)
        {
            Logger.Warning(NoRabitLocation);
            Problem("brak lokalizacji RABIT", Issue.Warning(NoRabitLocation, "RABIT"));
        }

        foreach (var location in Locations())
        {
            if (cancellation.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            List<FileInfo> files;
            try
            {
                (files, _, var subfolders) = ListFiles(location);
                if (files.Count == 0 && subfolders.Count > 0)
                    Problem("pliki w podfolderach", Issue.Warning(
                        $"Brak plików w folderze lokalizacji, są podfoldery: {string.Join(", ", subfolders.Take(10))} – import nie czyta podfolderów; wskaż w Administracji folder z plikami.",
                        location.Name));
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                Logger.Error(ex, "Lokalizacja {Location} niedostępna ({Path})", location.Name, location.Path);
                var hint = WebDavPath.IsWebDav(location.Path) ? $" {WebDavPath.AccessHint}" : "";
                Problem("lokalizacja niedostępna", Issue.Error($"Lokalizacja niedostępna: {ex.GetType().Name}: {ex.Message} ({location.Path}).{hint}", location.Name));
                progress?.Report(new ImportProgress($"{location.Name}: niedostępna"));
                continue;
            }

            foreach (var file in files)
            {
                if (cancellation.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                var label = $"[{results.Count + 1}] {location.Name} / {file.Name}";
                FileResult result;
                using (var stage = new StageReporter(text => progress?.Report(new ImportProgress($"{label}: {text}", null, location.Name, file.Name, text))))
                {
                    try
                    {
                        result = ImportFile(batchId, location, file, definitions, Problem, stage, cancellation);
                    }
                    catch (Exception ex) when (cancellation.IsCancellationRequested && ex is not OutOfMemoryException)
                    {
                        Logger.Warning("Plik {Location} / {File}: import przerwany przez użytkownika ({Stages})", location.Name, file.Name, stage.Summary);
                        stage.Dispose();
                        progress?.Report(new ImportProgress($"{label}: przerwany", null, location.Name, file.Name, "przerwany – plik nie zapisany"));
                        cancelled = true;
                        break;
                    }
                    Logger.Information("Plik {Location} / {File}: etapy – {Stages}", location.Name, file.Name, stage.Summary);
                }
                results.Add(result);
                progress?.Report(new ImportProgress($"[{results.Count}] {location.Name} / {file.Name}: {result.Decision}", null, location.Name, file.Name,
                    $"{result.Decision} – {result.Description}"));
            }
            if (cancelled)
                break;
        }

        int Count(string decision) => results.Count(r => r.Decision == decision);
        var status = cancelled ? "przerwany" : Count(FileDecisions.Error) > 0 || issues.Any(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error) ? "zakończony z błędami" : "zakończony";
        store.FinishBatch(batchId, services.Clock.Now, results.Count, Count(FileDecisions.Imported), Count(FileDecisions.Skipped),
            Count(FileDecisions.Duplicate), Count(FileDecisions.Unrecognized), Count(FileDecisions.Error), status);

        var run = new ImportRunResult(batchId, results, issues, cancelled);
        services.Journal.Add(Area, $"Import #{batchId}: {run.Summary}");
        if (!cancelled)
            ResolveEarlierProblems(batchId, reference);
        Logger.Information("Import #{Batch} koniec: {Summary}", batchId, run.Summary);
        return run;
    }

    private FileResult ImportFile(long batchId, ImportLocation location, FileInfo file, IReadOnlyList<SourceDefinitionRow> definitions,
        Action<string, Issue> problem, StageReporter stage, CancellationToken cancellation)
    {
        var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        string? sha = null;
        string? sourceCode = null;
        int? rows = null;
        string decision;
        string description;

        try
        {
            var definition = SourceMatcher.Match(file.Name, definitions);
            var last = definition is null ? null : store.LastSettled(location.Path, file.Name);
            if (definition is null)
            {
                decision = FileDecisions.Unrecognized;
                description = "brak pasującego prefiksu w definicjach źródeł – plik nie jest importowany";
                problem("plik nierozpoznany", Issue.Warning($"Plik nierozpoznany: {file.Name}", location.Name));
            }
            else if (last is not null && last.Size == file.Length && last.ModifiedAt == modified)
            {
                sourceCode = definition.Code;
                sha = last.Sha256;
                decision = FileDecisions.Skipped;
                description = "bez zmian od poprzedniego importu (te same metadane)";
            }
            else
            {
                sourceCode = definition.Code;
                (decision, description, sha, rows) = ImportContent(batchId, location, file, modified, definition, problem, stage, cancellation);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException && !cancellation.IsCancellationRequested)
        {
            // Błąd jednego pliku (uszkodzony Excel, brak dostępu, format) nie zatrzymuje importu pozostałych.
            Logger.Error(ex, "Plik {Location} / {File}: błąd", location.Name, file.Name);
            var hint = ex is IOException && WebDavPath.IsWebDav(location.Path) ? $" {WebDavPath.SizeLimitHint}" : "";
            decision = FileDecisions.Error;
            description = $"{ex.GetType().Name}: {ex.Message}{hint}";
            problem("błąd pliku", Issue.Error($"{file.Name}: {ex.Message}{hint}", location.Name));
        }

        store.RecordSeen(new SourceFileSeenRow(0, batchId, location.Path, file.Name, file.Length, modified, sha, decision, sourceCode, rows, description));
        Logger.Information("Plik {Location} / {File} ({Size} B, {Modified:u}): {Decision} – źródło {Source}; {Description}",
            location.Name, file.Name, file.Length, modified, decision, sourceCode ?? "-", description);
        return new FileResult(location.Name, file.Name, decision, sourceCode, rows, description);
    }

    /// <summary>
    /// Nowa treść pliku, etapami widocznymi na ekranie:
    /// 1) pobranie na dysk lokalny (HTTPS albo WebDAV, SHA-256 w trakcie) – dalsze odczyty z dysku, nie z sieci;
    /// 2) duplikat (ten sam SHA-256), nagłówek i parser – kolumny parsera obecne w pliku;
    /// 3) jeden przebieg pliku: wiersze czytane i parsowane w osobnym wątku i jednocześnie zapisywane wsadowo do CAN_Row
    ///    w jednej transakcji; błąd wartości, puste pole wymagane albo niezgodne sumy kwot (z kolumn pliku i z wartości pól)
    ///    wycofują zapis – plik nie trafia do bazy (decyzja „błąd”, problem w rejestrze) i przy kolejnym imporcie jest
    ///    pobierany ponownie;
    /// 4) kontrola w bazie (liczba wierszy i sumy po zapisie), treść pliku z dysku (Excel bez zmian, tekst – GZip),
    ///    zatwierdzenie; niezgodność wycofuje cały zapis.
    /// </summary>
    private (string Decision, string Description, string? Sha, int? Rows) ImportContent(long batchId, ImportLocation location, FileInfo file,
        DateTimeOffset modified, SourceDefinitionRow definition, Action<string, Issue> problem, StageReporter stage, CancellationToken cancellation)
    {
        if (!TabularFileReader.IsSupported(file.Name))
            throw new NotSupportedException($"format {file.Extension} nieobsługiwany (dozwolone: xlsx, xlsm, csv, txt)");

        stage.Start(StageDownload);
        using var local = _downloader.Download(location, file, stage.Detail, cancellation);
        Logger.Information("Plik {Location} / {File}: pobrany na dysk ({Method}) – {Megabytes} MB w {Elapsed} ({Speed} MB/s)", location.Name, file.Name,
            local.Method, StageReporter.Megabytes(local.Size), StageReporter.Seconds(local.Elapsed),
            StageReporter.Megabytes((long)(local.Size / Math.Max(local.Elapsed.TotalSeconds, 0.001))));
        var sha = local.Sha256;

        cancellation.ThrowIfCancellationRequested();
        stage.Start(StageCheck);
        if (store.FindByHash(sha) is { } existing)
            return (FileDecisions.Duplicate, DuplicateText(existing), sha, null);
        var source = TabularFileReader.OpenFile(local.Path);
        var signature = HeaderSignature.Compute(source.Headers);
        RowMapper? mapper = null;
        if (definition.Parser != SourceParsers.None)
        {
            var parser = store.ActiveParser(definition.Parser);
            if (parser is null)
            {
                var notReady = $"parser {definition.Parser} definicji {definition.Code} nie istnieje albo jest nieaktywny";
                problem("parser", Issue.Error($"{file.Name}: {notReady} – plik nie zapisany w bazie; popraw w Administracji i zaimportuj ponownie", location.Name));
                return (FileDecisions.Error, $"{notReady} – nie zapisany", sha, null);
            }
            mapper = MappedParser.Prepare(parser, source.Headers);
            if (mapper.MissingColumns.Count > 0)
            {
                var layout = $"brak kolumn parsera {parser.Code}: {string.Join(", ", mapper.MissingColumns)}";
                problem("układ kolumn", Issue.Error(
                    $"{file.Name}: {layout} – plik nie zapisany w bazie; popraw kolumny parsera w Administracji (Parsery) i zaimportuj ponownie",
                    location.Name));
                return (FileDecisions.Error, $"{layout} – nie zapisany", sha, null);
            }
        }

        var fileRow = new SourceFileRow(
            0, sha, location.Path, file.Name, definition.Code, file.Length, modified, source.FileType, source.Sheet, source.Encoding,
            source.Delimiter, source.Headers, signature, 0, batchId, services.Clock.Now, services.User.Account,
            "brak – źródło bez parsera (tylko treść pliku)", 0, null);
        void Finish(string name) => stage.Start($"{StageFinish} {name}");
        StoredFile? stored;
        int rows;
        if (mapper is null)
        {
            stage.Start(StageCount);
            rows = CountRows(source, stage, cancellation);
            stored = store.StoreFile(fileRow with { RowCount = rows }, local.Path, null, Finish);
        }
        else
        {
            stage.Start(StageRows);
            var check = new ContentCheck(mapper);
            try
            {
                stored = store.StoreFile(fileRow, local.Path,
                    new CanonicalData(mapper.Parser, mapper.Fields, ReadAhead(check.Read(source, stage, cancellation)), check.Totals), Finish);
            }
            catch (ContentRejectedException)
            {
                if (check.Issues.Errors > 0)
                {
                    foreach (var issue in check.Issues.Issues)
                        problem("wartość niezgodna z typem", issue with { Element = $"{file.Name}, {issue.Element}" });
                    return (FileDecisions.Error, $"{check.Issues.Errors} błędów wartości – nie zapisany", sha, check.Rows);
                }
                var differences = check.Differences();
                problem("kontrola przepływu", Issue.Error($"{file.Name}: dane kanoniczne niezgodne z plikiem ({differences}) – plik nie zapisany w bazie", location.Name));
                return (FileDecisions.Error, $"BŁĄD kontroli przepływu ({differences}) – nie zapisany", sha, check.Rows);
            }
            catch (CanonicalFlowException ex)
            {
                problem("kontrola przepływu", Issue.Error($"{file.Name}: {ex.Message} – zapis wycofany", location.Name));
                return (FileDecisions.Error, $"BŁĄD kontroli przepływu w bazie – zapis wycofany ({ex.Message})", sha, check.Rows);
            }
            rows = check.Rows;
        }
        if (stored is null)
            return (FileDecisions.Duplicate, DuplicateText(store.FindByHash(sha)!), sha, null);

        var pl = CultureInfo.GetCultureInfo("pl-PL");
        var canonical = mapper is null
            ? "tylko treść pliku (źródło bez parsera)"
            : $"dane kanoniczne ({mapper.Parser.Code}): {stored.CanonicalRows.ToString("#,0", pl)} wierszy" +
              string.Concat(mapper.Fields.Where(f => f.Type == FieldTypes.Decimal).Select(f => $", suma {f.Column} {PolishNumber.ToDisplay(stored.Sums[f.Field])}"));
        var extra = mapper is { ExtraColumns.Count: > 0 } ? $"; kolumny spoza parsera (tylko w treści pliku): {string.Join(", ", mapper.ExtraColumns)}" : "";
        return (FileDecisions.Imported, $"{definition.Code}, {rows.ToString("#,0", pl)} wierszy; {canonical}{extra}", sha, rows);
    }

    /// <summary>Liczba wierszy danych źródła bez parsera (postęp na ekranie).</summary>
    private static int CountRows(TabularSource source, StageReporter stage, CancellationToken cancellation)
    {
        var rows = 0;
        foreach (var _ in source.Rows())
        {
            if ((++rows & 0x3FFF) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
                stage.Detail($"{StageReporter.Count(rows)} wierszy");
            }
        }
        stage.Detail($"{StageReporter.Count(rows)} wierszy", final: true);
        return rows;
    }

    /// <summary>
    /// Przebieg pliku przez parser: liczba wierszy, błędy wartości (do 20 opisów), sumy kwot liczone niezależnie od
    /// parsera z tekstu kolumn pliku (format polski) i z wartości pól po parsowaniu. Wiersze do zapisu – do pierwszego
    /// błędu (dalej plik jest tylko sprawdzany, a zapis zostanie wycofany).
    /// </summary>
    private sealed class ContentCheck(RowMapper mapper)
    {
        private readonly List<(ParserField Field, int Position)> _decimals =
            mapper.Fields.Select((f, i) => (Field: f, Position: i)).Where(x => x.Field.Type == FieldTypes.Decimal).ToList();
        private readonly Dictionary<string, decimal> _raw = mapper.Fields.Where(f => f.Type == FieldTypes.Decimal).ToDictionary(f => f.Field, _ => 0m);
        private readonly Dictionary<string, decimal> _canonical = mapper.Fields.Where(f => f.Type == FieldTypes.Decimal).ToDictionary(f => f.Field, _ => 0m);

        public int Rows { get; private set; }

        public IssueCollector Issues { get; } = new();

        public IEnumerable<CanonicalRow> Read(TabularSource source, StageReporter stage, CancellationToken cancellation)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var cells in source.Rows())
            {
                Rows++;
                if ((Rows & 0x3FF) == 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    stage.Detail(Progress(watch.Elapsed));
                }
                var values = mapper.Map(cells, Rows, Issues);
                foreach (var (field, position) in _decimals)
                {
                    var index = mapper.Indexes[position];
                    if (index < cells.Length && PolishNumber.TryParse(MappedParser.Clean(cells[index]), out var value))
                        _raw[field.Field] += Math.Round(value, MappedParser.DecimalPlaces, MidpointRounding.AwayFromZero);
                    if (values?[position] is decimal parsed)
                        _canonical[field.Field] += parsed;
                }
                if (values is not null && Issues.Errors == 0)
                    yield return new CanonicalRow(Rows, values);
            }
            cancellation.ThrowIfCancellationRequested();   // przerwanie przed zatwierdzeniem zapisu
            stage.Detail(Progress(watch.Elapsed) + (Issues.Errors > 0 ? $", błędów wartości: {Issues.Errors}" : ""), final: true);
        }

        /// <summary>Kwoty niezgodne między kolumnami pliku a wartościami pól; pusty – zgodne.</summary>
        public string Differences() =>
            string.Join(", ", _decimals.Where(d => _raw[d.Field.Field] != _canonical[d.Field.Field])
                .Select(d => $"suma {mapper.Columns[d.Position]} {_raw[d.Field.Field]}/{_canonical[d.Field.Field]}"));

        /// <summary>Po przesłaniu wierszy: oczekiwane w bazie albo ContentRejectedException (zapis wycofany).</summary>
        public CanonicalTotals Totals()
        {
            if (Issues.Errors > 0)
                throw new ContentRejectedException($"{Issues.Errors} błędów wartości");
            if (Differences() is { Length: > 0 } differences)
                throw new ContentRejectedException($"kontrola przepływu: {differences}");
            return new CanonicalTotals(Rows, _canonical);
        }

        private string Progress(TimeSpan elapsed) =>
            $"{StageReporter.Count(Rows)} wierszy" + (elapsed.TotalSeconds >= 1 ? $" ({StageReporter.Count((long)(Rows / elapsed.TotalSeconds))} wierszy/s)" : "");
    }

    /// <summary>
    /// Odczyt z wyprzedzeniem: wiersze czytane i parsowane w osobnym wątku (bufor 20 000 wierszy), a zapis do bazy w tym
    /// samym czasie pobiera je z bufora – czas pliku to dłuższy z obu, nie ich suma. Błąd odczytu przechodzi do zapisu;
    /// przerwany zapis zatrzymuje odczyt.
    /// </summary>
    private static IEnumerable<T> ReadAhead<T>(IEnumerable<T> source, int capacity = 20_000)
    {
        using var buffer = new System.Collections.Concurrent.BlockingCollection<T>(capacity);
        using var stop = new CancellationTokenSource();
        Exception? error = null;
        var reader = Task.Run(() =>
        {
            try
            {
                foreach (var item in source)
                    buffer.Add(item, stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // zapis przerwany – odczyt niepotrzebny
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                buffer.CompleteAdding();
            }
        });
        try
        {
            foreach (var item in buffer.GetConsumingEnumerable())
                yield return item;
            reader.Wait();
            if (error is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        }
        finally
        {
            stop.Cancel();
            reader.Wait();
        }
    }

    private static string DuplicateText(SourceFileRow existing) =>
        $"treść już zaimportowana jako {existing.FileName} ({existing.ImportedBy}, {existing.ImportedAt:yyyy-MM-dd HH:mm})";
}
