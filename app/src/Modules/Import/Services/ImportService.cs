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
/// SHA-256), zaimportowany (wiersze surowe + dane kanoniczne) albo błąd. Błąd pliku lub niedostępna lokalizacja
/// nie zatrzymuje pozostałych. Kontrola przepływu: wiersze i sumy kwot kanonicznych = wiersze surowe.
/// </summary>
public sealed class ImportService(IImportStore store, AppServices services)
{
    public const string Area = "Import";
    public const string LockName = "import";
    public const string WillImport = "zostanie zaimportowany";
    public const string WillSkip = "zostanie pominięty – bez zmian od ostatniego importu";
    private const string ManualFolderName = "Do_importu";
    public const string NoRabitLocation =
        "Brak aktywnej lokalizacji RABIT – import czyta tylko folder Do_importu; w Administracji zaznacz „Aktywna” przy lokalizacji.";

    /// <summary>Log importu (plik logs\pzl-ev-*.log obok exe): ścieżki, dostęp, decyzje, pełne błędy.</summary>
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

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
            definitions.Count == 0 ? "BRAK" : string.Join("; ", definitions.Select(d => $"{d.Code}: prefiks '{d.Prefix}', parser {(d.Parser.Length == 0 ? "brak" : d.Parser)}, kolumn {d.Columns.Count}")));

    /// <summary>
    /// Import ze wszystkich lokalizacji. Tylko jedna osoba naraz (blokada operacji): gdy import trwa u kogoś
    /// innego – wynik z NotStarted. Wpis „w toku” w historii od początku importu.
    /// </summary>
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
                progress?.Report(new ImportProgress($"[{results.Count + 1}] {location.Name} / {file.Name}: importowanie…", null, location.Name, file.Name, "importowanie…"));
                var result = ImportFile(batchId, location, file, definitions, Problem);
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
        Logger.Information("Import #{Batch} koniec: {Summary}", batchId, run.Summary);
        return run;
    }

    private FileResult ImportFile(long batchId, ImportLocation location, FileInfo file, IReadOnlyList<SourceDefinitionRow> definitions, Action<string, Issue> problem)
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
                (decision, description, sha, rows) = ImportContent(batchId, location, file, modified, definition, problem);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
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

    private (string Decision, string Description, string? Sha, int? Rows) ImportContent(
        long batchId, ImportLocation location, FileInfo file, DateTimeOffset modified, SourceDefinitionRow definition, Action<string, Issue> problem)
    {
        var content = File.ReadAllBytes(file.FullName);
        var sha = FileHash.Sha256(content);
        if (store.FindByHash(sha) is { } existing)
            return (FileDecisions.Duplicate, DuplicateText(existing), sha, null);

        if (!TabularFileReader.IsSupported(file.Name))
            throw new NotSupportedException($"format {file.Extension} nieobsługiwany (dozwolone: xlsx, xlsm, csv, txt)");

        var data = TabularFileReader.Read(content, file.Name);
        var signature = HeaderSignature.Compute(data.Headers);
        var now = services.Clock.Now;
        var fileId = store.RegisterFile(new SourceFileRow(
            0, sha, location.Path, file.Name, definition.Code, file.Length, modified, data.FileType, data.Sheet, data.Encoding,
            data.Delimiter, data.Headers, signature, data.Rows.Count, batchId, now, services.User.Account,
            "w toku", 0, null), data.Rows);
        if (fileId is null)
            return (FileDecisions.Duplicate, DuplicateText(store.FindByHash(sha)!), sha, null);

        var canonical = CreateCanonical(fileId.Value, file.Name, location.Name, definition, data, signature, problem);
        var rowsText = data.Rows.Count.ToString("#,0", CultureInfo.GetCultureInfo("pl-PL"));
        return (FileDecisions.Imported, $"{definition.Code}, {rowsText} wierszy; {canonical}", sha, data.Rows.Count);
    }

    /// <summary>Dane kanoniczne według parsera definicji; zwraca opis wyniku do decyzji pliku.</summary>
    private string CreateCanonical(long fileId, string fileName, string locationName, SourceDefinitionRow definition, TabularData data,
        string signature, Action<string, Issue> problem)
    {
        if (definition.Parser != SourceParsers.Actuals)
        {
            store.CompleteCanonical(fileId, "brak – źródło bez parsera (tylko wiersze surowe)", [], null);
            return "tylko wiersze surowe (źródło bez parsera)";
        }
        if (signature != definition.Signature)
        {
            store.CompleteCanonical(fileId, "brak – sygnatura kolumn niezgodna z definicją", [], null);
            problem("sygnatura kolumn", Issue.Error(
                $"{fileName}: układ kolumn niezgodny z definicją {definition.Code} (sygnatura {signature}, oczekiwana {definition.Signature}) – dane kanoniczne nie powstały do czasu aktualizacji definicji",
                locationName));
            return "BRAK danych kanonicznych – zmieniony układ kolumn";
        }

        var parsed = ActualsParser.Parse(fileId, data.Headers, data.Rows);
        if (parsed.ErrorCount > 0)
        {
            store.CompleteCanonical(fileId, $"brak – {parsed.ErrorCount} błędów wartości", [], null);
            foreach (var issue in parsed.Issues)
                problem("wartość niezgodna z typem", issue with { Element = $"{fileName}, {issue.Element}" });
            return $"BRAK danych kanonicznych – {parsed.ErrorCount} błędów wartości";
        }

        store.CompleteCanonical(fileId, "utworzone", parsed.Rows, ActualsParser.Version);
        return VerifyFlow(fileName, locationName, data, parsed.Rows, problem);
    }

    /// <summary>
    /// Kontrola przepływu: liczba wierszy i sumy kwot danych kanonicznych zgodne z wierszami surowymi
    /// (sumy z wierszy surowych liczone niezależnie od parsera).
    /// </summary>
    private static string VerifyFlow(string fileName, string locationName, TabularData raw, IReadOnlyList<ActualsRow> canonical, Action<string, Issue> problem)
    {
        decimal RawSum(string column)
        {
            var index = raw.Headers.ToList().FindIndex(h => string.Equals(h.Trim(), column, StringComparison.OrdinalIgnoreCase));
            return raw.Rows.Sum(r => index < r.Length && PolishNumber.TryParse(r[index], out var v) ? v : 0m);
        }

        var rawObj = RawSum("Value in Obj. Crcy");
        var rawRep = RawSum("Val.in rep.cur.");
        var canObj = canonical.Sum(r => r.ValueObjCrcy ?? 0m);
        var canRep = canonical.Sum(r => r.ValueRepCur ?? 0m);
        if (raw.Rows.Count != canonical.Count || rawObj != canObj || rawRep != canRep)
        {
            problem("kontrola przepływu", Issue.Error(
                $"{fileName}: dane kanoniczne niezgodne z surowymi (wiersze {raw.Rows.Count}/{canonical.Count}, suma PLN {rawObj}/{canObj}, suma USD {rawRep}/{canRep})",
                locationName));
            return "BŁĄD kontroli przepływu – patrz problemy";
        }
        return $"dane kanoniczne: {canonical.Count} wierszy, suma Value in Obj. Crcy {PolishNumber.ToDisplay(canObj)}, suma Val.in rep.cur. {PolishNumber.ToDisplay(canRep)}";
    }

    private static string DuplicateText(SourceFileRow existing) =>
        $"treść już zaimportowana jako {existing.FileName} ({existing.ImportedBy}, {existing.ImportedAt:yyyy-MM-dd HH:mm})";
}
