using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Models;
using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Import.ViewModels;

/// <summary>
/// Ekran Import RABIT (F05): uruchomienie importu, wynik dla każdego pliku, problemy, historia importów;
/// sprawdzenie źródeł bez importu (co aplikacja widzi w każdej lokalizacji); test dostępu do SharePoint (metody A–H).
/// </summary>
public sealed class ImportViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    private readonly ImportService _service;
    private readonly IImportStore _store;
    private CancellationTokenSource? _cancellation;
    private bool _isRunning;
    private string _progress = "";
    private string _summary = "";
    private ImportBatchRow? _selectedBatch;
    private string _accessFolder = "";
    private string _accessFile = "";
    private string _accessListId = AccessTestInput.RabitListId;
    private string _accessViewId = AccessTestInput.RabitViewId;
    private string _accessEnvironment = "";
    private string _accessConclusion = "";
    private string _accessReport = "";

    public ImportViewModel(ImportService service, IImportStore store)
    {
        _service = service;
        _store = store;
        Run = new AsyncRelayCommand(DoRun, () => !_isRunning);
        Cancel = new RelayCommand(_ => _cancellation?.Cancel(), _ => _isRunning);
        Refresh = new RelayCommand(_ => Reload(), _ => !_isRunning);
        Check = new AsyncRelayCommand(DoCheck, () => !_isRunning);
        TestAccess = new AsyncRelayCommand(DoTestAccess, () => !_isRunning);
        Reload();
    }

    public ObservableCollection<ImportLocation> Locations { get; } = [];

    public ObservableCollection<FileResult> Files { get; } = [];

    public ObservableCollection<Issue> Issues { get; } = [];

    public ObservableCollection<ImportBatchRow> Batches { get; } = [];

    public ObservableCollection<SourceFileSeenRow> BatchFiles { get; } = [];

    public ObservableCollection<LocationCheck> LocationChecks { get; } = [];

    public ObservableCollection<FileCheck> FileChecks { get; } = [];

    public bool HasCheck => LocationChecks.Count > 0;

    public ObservableCollection<AccessMethodResult> AccessResults { get; } = [];

    public string AccessFolder { get => _accessFolder; set => SetProperty(ref _accessFolder, value); }

    public string AccessFile { get => _accessFile; set => SetProperty(ref _accessFile, value); }

    public string AccessListId { get => _accessListId; set => SetProperty(ref _accessListId, value); }

    public string AccessViewId { get => _accessViewId; set => SetProperty(ref _accessViewId, value); }

    public string AccessEnvironment { get => _accessEnvironment; private set => SetProperty(ref _accessEnvironment, value); }

    public string AccessConclusion { get => _accessConclusion; private set => SetProperty(ref _accessConclusion, value); }

    public string AccessReport { get => _accessReport; private set => SetProperty(ref _accessReport, value); }

    /// <summary>Katalog logu aplikacji (App.xaml.cs) – szczegóły importu i sprawdzenia źródeł, raporty testu dostępu.</summary>
    private static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    public string LogText => $"Szczegóły (ścieżki, dostęp, decyzje, pełne błędy) w logu: {LogDirectory}";

    public ICommand Run { get; }

    public ICommand Cancel { get; }

    public ICommand Refresh { get; }

    public ICommand Check { get; }

    public ICommand TestAccess { get; }

    public string Progress { get => _progress; private set => SetProperty(ref _progress, value); }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }

    public bool HasIssues => Issues.Count > 0;

    public string DefinitionsText => $"Rozpoznawane źródła (aktywne definicje): {string.Join(", ", _store.ActiveDefinitions().Select(d => $"{d.Code} – pliki zaczynające się od {SourceMatcher.Prefix(d.Prefix)}"))}";

    public ImportBatchRow? SelectedBatch
    {
        get => _selectedBatch;
        set
        {
            if (!SetProperty(ref _selectedBatch, value))
                return;
            BatchFiles.Clear();
            if (value is not null)
            {
                foreach (var seen in _store.Seen(value.Id))
                    BatchFiles.Add(seen);
            }
        }
    }

    private async Task DoRun()
    {
        IsRunning = true;
        Progress = "Import w toku…";
        Files.Clear();
        Issues.Clear();
        OnPropertyChanged(nameof(HasIssues));
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<string>(message => Progress = message);
        try
        {
            var result = await Task.Run(() => _service.Run(progress, _cancellation.Token));
            foreach (var file in result.Files)
                Files.Add(file);
            foreach (var issue in result.Issues)
                Issues.Add(issue);
            Summary = $"Import #{result.BatchId}: {result.Summary}";
            Progress = "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Import przerwany błędem");
            Progress = $"Import przerwany błędem: {ex.Message}";
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsRunning = false;
            OnPropertyChanged(nameof(HasIssues));
            Reload();
        }
    }

    private async Task DoCheck()
    {
        IsRunning = true;
        Progress = "Sprawdzanie źródeł…";
        LocationChecks.Clear();
        FileChecks.Clear();
        try
        {
            var result = await Task.Run(_service.Check);
            foreach (var location in result.Locations)
                LocationChecks.Add(location);
            foreach (var file in result.Files)
                FileChecks.Add(file);
            Progress = "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Sprawdzenie źródeł przerwane błędem");
            Progress = $"Sprawdzenie źródeł przerwane błędem: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(nameof(HasCheck));
        }
    }

    private async Task DoTestAccess()
    {
        IsRunning = true;
        Progress = "Test dostępu…";
        AccessResults.Clear();
        AccessEnvironment = AccessConclusion = AccessReport = "";
        var input = new AccessTestInput(AccessFolder, AccessFile, AccessListId, AccessViewId);
        var progress = new Progress<string>(code => Progress = $"Test dostępu: metoda {code}…");
        try
        {
            var result = await Task.Run(() =>
            {
                using var test = new RabitAccessTest();
                return test.Run(input, progress);
            });
            AccessEnvironment = string.Join(Environment.NewLine, result.Environment);
            foreach (var method in result.Methods)
                AccessResults.Add(method);
            AccessConclusion = result.Conclusion;
            Progress = "";
            var path = Path.Combine(LogDirectory, $"test-dostepu-rabit-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            try
            {
                File.WriteAllText(path, result.Report);
                AccessReport = $"Raport (bez zawartości plików): {path}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AccessReport = $"Raport nie został zapisany ({ex.Message}) – jest w logu aplikacji.";
            }
        }
        catch (ArgumentException ex)
        {
            Progress = ex.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Test dostępu przerwany błędem");
            Progress = $"Test dostępu przerwany błędem: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Reload()
    {
        Locations.Clear();
        foreach (var location in _service.Locations())
            Locations.Add(location);
        if (AccessFolder.Length == 0)
            AccessFolder = Locations.Where(l => !l.IsManualFolder).Select(l => SharePointAddress.Parse(l.ConfiguredPath)?.FolderUrl).FirstOrDefault(u => u is not null) ?? "";
        Batches.Clear();
        foreach (var batch in _store.Batches(50))
            Batches.Add(batch);
        OnPropertyChanged(nameof(DefinitionsText));
    }
}
