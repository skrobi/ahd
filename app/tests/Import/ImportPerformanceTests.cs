using System.Diagnostics;
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace PzlEv.Tests.Import;

/// <summary>
/// Ręczny test wydajności importu dużego pliku ACTUALS (wiersze wzorcowe powtórzone): czas, szczytowa pamięć procesu,
/// liczba wierszy i sumy w CAN_Row. Uruchamiany tylko ze zmienną PZLEV_PERF_ROWS (np. 2000000) i bazą testową;
/// PZLEV_PERF_FORMAT=xlsx – plik Excel (tekst we wspólnych napisach, liczby i daty jak z Excela), domyślnie CSV.
/// </summary>
public sealed class ImportPerformanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-perf-").FullName;
    private TestDatabase? _database;

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [PerfFact]
    public void Large_actuals_file_is_imported_streaming()
    {
        var rows = PerfFactAttribute.Rows!.Value / 6 * 6;
        var services = new TestServices();
        _database = new TestDatabase(presets: true);
        var app = services.App(_root, _database.Sql);
        var config = new SourceConfigService(new SqlSourceConfigStore(_database.Sql, services.Clock, services.User), app.Journal);
        var rabit = Assert.Single(config.Locations());
        Assert.True(config.SaveLocation(new LocationInput(rabit.LocationId, rabit.Version, rabit.Name, rabit.Path, Active: false)).Success);
        var store = new SqlImportStore(_database.Sql);

        var sample = File.ReadAllLines(TestServices.TestData("RABIT", "ACTUALS_PAF_01.csv"), Encoding.UTF8);
        Directory.CreateDirectory(app.Config.ImportFolder);
        var xlsx = Environment.GetEnvironmentVariable("PZLEV_PERF_FORMAT") == "xlsx";
        var path = Path.Combine(app.Config.ImportFolder, xlsx ? "ACTUALS_PAF_PERF.xlsx" : "ACTUALS_PAF_PERF.csv");
        // Plik wygenerowany raz i używany ponownie – kolejne uruchomienie mierzy pamięć bez generowania w tym samym procesie.
        var cached = Path.Combine(Path.GetTempPath(), "pzl-ev-perf", $"{rows}{Path.GetExtension(path)}");
        if (!File.Exists(cached))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            IEnumerable<string[]> Lines() => Enumerable.Range(0, rows / 6)
                .SelectMany(i => sample.Skip(1).Select(line => line.Replace("FV/2026/03/", $"FV/{i}/").Split(';')));   // każda partia inna
            if (xlsx)
                WriteExcel(cached, sample[0].Split(';'), Lines());
            else
                using (var writer = new StreamWriter(cached, false, new UTF8Encoding(false)))
                {
                    writer.WriteLine(sample[0]);
                    foreach (var line in Lines())
                        writer.WriteLine(string.Join(';', line));
                }
        }
        File.Copy(cached, path);
        var size = new FileInfo(path).Length;

        GC.Collect();
        var process = Process.GetCurrentProcess();
        long peakWorkingSet = 0, peakHeap = 0;
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                process.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
                await Task.Delay(100);
            }
        });
        var watch = Stopwatch.StartNew();
        var run = new ImportService(store, app).Run();
        watch.Stop();
        sampling.Cancel();
        sampler.Wait();

        var file = Assert.Single(run.Files);
        Assert.True(file.Decision == FileDecisions.Imported, file.Description);
        Assert.Equal(rows, file.Rows);
        var stored = store.FindByHash(Assert.Single(store.Seen(run.BatchId)).Sha256!)!;
        Assert.Equal(rows, stored.CanonicalRows);
        Assert.Contains($"suma Value in Obj. Crcy {PzlEv.Shared.Utils.Files.PolishNumber.ToDisplay(10574.11m * (rows / 6))}", file.Description);
        output.WriteLine($"{rows:#,0} wierszy, plik {Path.GetExtension(path)} {size / 1048576.0:0.0} MB: {watch.Elapsed.TotalSeconds:0.0} s " +
                         $"({rows / watch.Elapsed.TotalSeconds:#,0} wierszy/s), pamięć w czasie importu: proces do {peakWorkingSet / 1048576:#,0} MB, " +
                         $"sterta .NET do {peakHeap / 1048576:#,0} MB");
    }

    /// <summary>Plik Excel jak eksport z SAP: tekst we wspólnych napisach, kwoty i liczby jako liczby, data ze stylem daty.</summary>
    internal static void WriteExcel(string path, string[] headers, IEnumerable<string[]> lines)
    {
        int[] numbers = [6, 8, 10, 11, 19, 21];
        const int date = 20;
        var strings = new Dictionary<string, int>();
        int Shared(string text) => strings.TryGetValue(text, out var index) ? index : strings[text] = strings.Count;
        static string Reference(int column, int row) => $"{(char)('A' + column)}{row}";   // 23 kolumny: A–W

        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        var styles = workbook.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new Fonts(new Font()), new Fills(new Fill()), new Borders(new Border()),
            new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
        var sheet = workbook.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(sheet))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());
            var rowNumber = 1;
            foreach (var cells in lines.Prepend(headers))
            {
                writer.WriteStartElement(new Row { RowIndex = (uint)rowNumber });
                for (var c = 0; c < cells.Length; c++)
                {
                    var text = cells[c];
                    if (text.Length == 0)
                        continue;
                    var reference = Reference(c, rowNumber);
                    if (rowNumber > 1 && numbers.Contains(c) && PzlEv.Shared.Utils.Files.PolishNumber.TryParse(text, out var number))
                        writer.WriteElement(new Cell { CellReference = reference, CellValue = new CellValue(number) });
                    else if (rowNumber > 1 && c == date && DateTime.TryParse(text, CultureInfo.InvariantCulture, out var day))
                        writer.WriteElement(new Cell { CellReference = reference, StyleIndex = 1, CellValue = new CellValue(day.ToOADate().ToString(CultureInfo.InvariantCulture)) });
                    else
                        writer.WriteElement(new Cell { CellReference = reference, DataType = CellValues.SharedString, CellValue = new CellValue(Shared(text)) });
                }
                writer.WriteEndElement();
                rowNumber++;
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        var table = workbook.AddNewPart<SharedStringTablePart>();
        using (var writer = OpenXmlWriter.Create(table))
        {
            writer.WriteStartElement(new SharedStringTable { Count = (uint)strings.Count, UniqueCount = (uint)strings.Count });
            foreach (var text in strings.Keys)
                writer.WriteElement(new SharedStringItem(new Text(text)));
            writer.WriteEndElement();
        }
        workbook.Workbook = new Workbook(new Sheets(new Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "Sheet1" }));
    }
}

/// <summary>Test wydajności: pomijany bez bazy testowej albo bez zmiennej PZLEV_PERF_ROWS (liczba wierszy).</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public const string Variable = "PZLEV_PERF_ROWS";

    public PerfFactAttribute()
    {
        if (TestDatabase.ConnectionString is null || Rows is null)
            Skip = $"Test wydajności (opcjonalny) – uruchamiany tylko ze zmienną {Variable} (np. 2000000) i dostępną bazą testową";
    }

    public static int? Rows => int.TryParse(Environment.GetEnvironmentVariable(Variable), out var rows) && rows >= 6 ? rows : null;
}
