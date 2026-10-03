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
/// Ekran Import RABIT (F05). „Importuj”: czy nikt inny nie importuje → logowanie do SharePoint (brama F5, jak Office)
/// → sprawdzenie źródeł (pliki pasujące do definicji z datami, „zostanie zaimportowany”) → import ze zmianą statusu
/// każdego pliku; wpis „w toku” w historii od początku importu. „Sprawdź źródła” – to samo bez importu.
/// </summary>
public sealed class ImportViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    private readonly ImportService _service;
    private readonly IImportStore _store;
    private readonly ISharePointLoginDialog _login;
    private readonly List<ImportFileRow> _allFiles = [];
    private CancellationTokenSource? _cancellation;
    private bool _isRunning;
    private bool _showAllFiles;
    private string _progress = "";
    private string _summary = "";
    private string _runningElsewhere = "";
    private ImportBatchRow? _selectedBatch;

    public ImportViewModel(ImportService service, IImportStore store, ISharePointLoginDialog login)
    {
        _service = service;
        _store = store;
        _login = login;
        Run = new AsyncRelayCommand(DoRun, () => !_isRunning);
        Check = new AsyncRelayCommand(DoCheck, () => !_isRunning);
        Cancel = new RelayCommand(_ => _cancellation?.Cancel(), _ => _isRunning);
        Refresh = new RelayCommand(_ => Reload(), _ => !_isRunning);
        Reload();
    }

    public ObservableCollection<ImportLocation> Locations { get; } = [];

    public ObservableCollection<LocationCheck> LocationChecks { get; } = [];

    public ObservableCollection<ImportFileRow> Files { get; } = [];

    public ObservableCollection<Issue> Issues { get; } = [];

    public ObservableCollection<ImportBatchRow> Batches { get; } = [];

    public ObservableCollection<SourceFileSeenRow> BatchFiles { get; } = [];

    public ICommand Run { get; }

    public ICommand Check { get; }

    public ICommand Cancel { get; }

    public ICommand Refresh { get; }

    public string Progress { get => _progress; private set => SetProperty(ref _progress, value); }

    /// <summary>Stan importu: rozpoczęty / nie rozpoczęty (i dlaczego) / zakończony.</summary>
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }

    public bool HasIssues => Issues.Count > 0;

    public bool HasFiles => LocationChecks.Count > 0 || _allFiles.Count > 0;

    /// <summary>Import trwający u innej osoby (blokada wspólna) – komunikat; pusty – nikt nie importuje.</summary>
    public string RunningElsewhere { get => _runningElsewhere; private set => SetProperty(ref _runningElsewhere, value); }

    public bool IsRunningElsewhere => RunningElsewhere.Length > 0;

    /// <summary>false – tylko pliki pasujące do definicji; true – także nierozpoznane.</summary>
    public bool ShowAllFiles
    {
        get => _showAllFiles;
        set
        {
            if (SetProperty(ref _showAllFiles, value))
                FillFiles();
        }
    }

    public string DefinitionsText => $"Rozpoznawane źródła (aktywne definicje): {string.Join(", ", _store.ActiveDefinitions().Select(d => $"{d.Code} – pliki zaczynające się od {SourceMatcher.Prefix(d.Prefix)}"))}";

    public string ManualFolderText =>
        "Do_importu – folder na pliki pobrane ręcznie; import czyta go razem z SharePoint. Każdy plik do zaimportowania import " +
        "najpierw pobiera na dysk lokalny (folder tymczasowy, usuwany po pliku), potem zapisuje dane w bazie – status pliku pokazuje " +
        "etap (1/4 pobieranie, 2/4 sprawdzanie, 3/4 odczyt i zapis wierszy, 4/4 kontrola w bazie i treść pliku).";

    /// <summary>Katalog logu aplikacji (App.xaml.cs) – szczegóły importu i sprawdzenia źródeł.</summary>
    public string LogText => $"Szczegóły (ścieżki, dostęp, decyzje, pełne błędy) w logu: {Path.Combine(AppContext.BaseDirectory, "logs")}";

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

    private async Task DoCheck()
    {
        IsRunning = true;
        try
        {
            if (!await LoginToSharePoint())
            {
                Summary = "Sprawdzenie źródeł przerwane – logowanie do SharePoint anulowane.";
                return;
            }
            if (await CheckSources() is { } check)
                Summary = CheckSummary(check);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task DoRun()
    {
        IsRunning = true;
        Issues.Clear();
        OnPropertyChanged(nameof(HasIssues));
        try
        {
            if (_service.RunningImport() is { } holder)
            {
                Summary = $"Import nie rozpoczęty – trwa import: {holder.Text}.";
                return;
            }
            Summary = "Logowanie do SharePoint…";
            if (!await LoginToSharePoint())
            {
                Summary = "Import nie rozpoczęty – logowanie do SharePoint anulowane.";
                return;
            }
            if (await CheckSources() is not { } check)
            {
                Summary = "Import nie rozpoczęty – sprawdzenie źródeł przerwane błędem.";
                return;
            }
            Summary = CheckSummary(check) + " Import…";

            _cancellation = new CancellationTokenSource();
            var progress = new Progress<ImportProgress>(OnProgress);
            var result = await Task.Run(() => _service.Run(progress, _cancellation.Token));
            foreach (var issue in result.Issues)
                Issues.Add(issue);
            Summary = result.NotStarted ?? $"Import #{result.BatchId} {(result.Cancelled ? "przerwany" : "zakończony")}: {result.Summary}";
            Progress = "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Import przerwany błędem");
            Summary = $"Import przerwany błędem: {ex.Message}";
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            IsRunning = false;
            OnPropertyChanged(nameof(HasIssues));
            Reload();
        }
    }

    private void OnProgress(ImportProgress progress)
    {
        Progress = progress.Message;
        if (progress.BatchId is { } batchId)
        {
            Summary = $"Import #{batchId} rozpoczęty {DateTime.Now:HH:mm} – w toku (wpis w historii importów).";
            ReloadBatches();
        }
        if (progress.FileName is null || progress.Status is null)
            return;
        var row = _allFiles.FirstOrDefault(f => f.Location == progress.Location && f.FileName == progress.FileName);
        if (row is null)
        {
            row = new ImportFileRow(progress.Location ?? "", progress.FileName, null, null, "", !progress.Status.StartsWith(FileDecisions.Unrecognized), progress.Status);
            _allFiles.Add(row);
            FillFiles();
        }
        row.Status = progress.Status;
    }

    /// <summary>
    /// Logowanie do bramy SharePoint każdej aktywnej lokalizacji (jak Office – MS-OFBA; bez niego strona folderu
    /// w oknie aplikacji). Bez lokalizacji SharePoint – nic do zrobienia. false – logowanie anulowane.
    /// </summary>
    private async Task<bool> LoginToSharePoint()
    {
        var addresses = _service.Locations().Where(l => !l.IsManualFolder)
            .Select(l => SharePointAddress.Parse(l.ConfiguredPath))
            .OfType<SharePointAddress>()
            .DistinctBy(a => a.Origin, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var address in addresses)
        {
            Progress = $"Logowanie do SharePoint ({address.Host})…";
            var request = await Task.Run(() =>
            {
                using var login = new SharePointLogin();
                return login.Prepare(address);
            });
            if (!_login.Login(request))
            {
                Logger.Information("Logowanie SharePoint {Host} anulowane", address.Host);
                Progress = "";
                return false;
            }
        }
        Progress = "";
        return true;
    }

    /// <summary>Sprawdzenie źródeł (bez importu); null – przerwane błędem (opis w Progress).</summary>
    private async Task<SourcesCheckResult?> CheckSources()
    {
        Progress = "Sprawdzanie źródeł…";
        LocationChecks.Clear();
        _allFiles.Clear();
        FillFiles();
        try
        {
            var result = await Task.Run(_service.Check);
            foreach (var location in result.Locations)
                LocationChecks.Add(location);
            _allFiles.AddRange(result.Files.Select(f => new ImportFileRow(f)));
            FillFiles();
            Progress = "";
            return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Sprawdzenie źródeł przerwane błędem");
            Progress = $"Sprawdzenie źródeł przerwane błędem: {ex.Message}";
            return null;
        }
        finally
        {
            OnPropertyChanged(nameof(HasFiles));
        }
    }

    private static string CheckSummary(SourcesCheckResult check)
    {
        var matching = check.Files.Where(f => f.Matches).ToList();
        var toImport = matching.Count(f => f.Note == ImportService.WillImport);
        var unavailable = check.Locations.Count(l => !l.Accessible);
        return $"Pliki pasujące do definicji: {matching.Count}" +
               (matching.Count > 0 ? $" (najnowszy z {matching.Max(f => f.ModifiedLocal):yyyy-MM-dd HH:mm})" : "") +
               $", do zaimportowania: {toImport}." +
               (unavailable > 0 ? $" Niedostępne lokalizacje: {unavailable} – szczegóły poniżej." : "");
    }

    private void FillFiles()
    {
        Files.Clear();
        foreach (var file in _allFiles.Where(f => _showAllFiles || f.Matches).OrderByDescending(f => f.Matches).ThenByDescending(f => f.ModifiedLocal))
            Files.Add(file);
        OnPropertyChanged(nameof(HasFiles));
    }

    private void ReloadBatches()
    {
        Batches.Clear();
        foreach (var batch in _store.Batches(50))
            Batches.Add(batch);
    }

    private void Reload()
    {
        Locations.Clear();
        foreach (var location in _service.Locations())
            Locations.Add(location);
        ReloadBatches();
        RunningElsewhere = _isRunning || _service.RunningImport() is not { } holder ? "" : $"Trwa import: {holder.Text} – poczekaj na jego zakończenie.";
        OnPropertyChanged(nameof(IsRunningElsewhere));
        OnPropertyChanged(nameof(DefinitionsText));
    }
}
