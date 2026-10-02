using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Models;
using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Import.ViewModels;

/// <summary>
/// Ekran Import RABIT (F05): uruchomienie importu, wynik dla każdego pliku, problemy, historia importów;
/// sprawdzenie źródeł bez importu (co aplikacja widzi w każdej lokalizacji); logowanie do SharePoint za bramą F5
/// (jak Office, MS-OFBA) z listą pasujących plików i ich datami; test dostępu do SharePoint (metody A–I).
/// </summary>
public sealed class ImportViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    private readonly ImportService _service;
    private readonly IImportStore _store;
    private readonly ISharePointLoginDialog _login;
    private readonly List<FileCheck> _allFileChecks = [];
    private bool _showAllFiles;
    private bool _needsLogin;
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

    public ImportViewModel(ImportService service, IImportStore store, ISharePointLoginDialog login)
    {
        _service = service;
        _store = store;
        _login = login;
        Run = new AsyncRelayCommand(DoRun, () => !_isRunning);
        Cancel = new RelayCommand(_ => _cancellation?.Cancel(), _ => _isRunning);
        Refresh = new RelayCommand(_ => Reload(), _ => !_isRunning);
        Check = new AsyncRelayCommand(DoCheck, () => !_isRunning);
        TestAccess = new AsyncRelayCommand(DoTestAccess, () => !_isRunning);
        Login = new AsyncRelayCommand(DoLogin, () => !_isRunning);
        LoginAndRun = new AsyncRelayCommand(DoLoginAndRun, () => !_isRunning);
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

    /// <summary>false – w sprawdzeniu źródeł tylko pliki pasujące do definicji (z datami); true – wszystkie.</summary>
    public bool ShowAllFiles
    {
        get => _showAllFiles;
        set
        {
            if (SetProperty(ref _showAllFiles, value))
                FillFileChecks();
        }
    }

    /// <summary>Ostatni import nie dotarł do lokalizacji SharePoint – propozycja „Zaloguj i ponów”.</summary>
    public bool NeedsLogin { get => _needsLogin; private set => SetProperty(ref _needsLogin, value); }

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

    public ICommand Login { get; }

    public ICommand LoginAndRun { get; }

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
        NeedsLogin = false;
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
            NeedsLogin = result.UnavailableSharePoint.Count > 0;
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
        try
        {
            if (await CheckSources() is not null)
                Progress = "";
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Sprawdzenie źródeł (bez importu); null – przerwane błędem (opis w Progress).</summary>
    private async Task<SourcesCheckResult?> CheckSources()
    {
        Progress = "Sprawdzanie źródeł…";
        LocationChecks.Clear();
        _allFileChecks.Clear();
        FileChecks.Clear();
        try
        {
            var result = await Task.Run(_service.Check);
            foreach (var location in result.Locations)
                LocationChecks.Add(location);
            _allFileChecks.AddRange(result.Files);
            FillFileChecks();
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
            OnPropertyChanged(nameof(HasCheck));
        }
    }

    private void FillFileChecks()
    {
        FileChecks.Clear();
        foreach (var file in _allFileChecks.Where(f => _showAllFiles || f.Matches).OrderByDescending(f => f.Matches).ThenByDescending(f => f.Modified))
            FileChecks.Add(file);
    }

    /// <summary>
    /// Logowanie do każdej bramy SharePoint aktywnych lokalizacji (jak Office – MS-OFBA, inaczej strona folderu
    /// w oknie aplikacji); true – wszystkie okna zakończone zalogowaniem.
    /// </summary>
    private async Task<bool> LoginToSharePoint()
    {
        var addresses = Locations.Where(l => !l.IsManualFolder)
            .Select(l => SharePointAddress.Parse(l.ConfiguredPath))
            .OfType<SharePointAddress>()
            .DistinctBy(a => a.Origin, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (addresses.Count == 0)
        {
            Progress = "Brak aktywnej lokalizacji SharePoint – włącz lokalizację w Administracji.";
            return false;
        }
        foreach (var address in addresses)
        {
            Progress = $"Logowanie do {address.Host}…";
            var (request, probe) = await Task.Run(() =>
            {
                using var test = new RabitAccessTest();
                return test.Ofba(address, CancellationToken.None);
            });
            Logger.Information("Logowanie SharePoint {Host}: {Status} – {Details}", address.Host, probe.Status, string.Join(" | ", probe.Details));
            if (!_login.Login(request ?? SharePointLogin.Fallback(address)))
            {
                Progress = $"Logowanie do {address.Host} anulowane.";
                return false;
            }
        }
        return true;
    }

    private async Task DoLogin()
    {
        IsRunning = true;
        try
        {
            if (!await LoginToSharePoint())
                return;
            ShowAllFiles = false;
            if (await CheckSources() is not { } check)
                return;
            var sharePoint = check.Locations.Where(l => WebDavPath.IsWebDav(l.Path)).ToList();
            var matching = check.Files.Where(f => f.Matches).ToList();
            Progress = sharePoint.Count > 0 && sharePoint.All(l => l.Accessible)
                ? $"Zalogowano – pliki pasujące do definicji: {matching.Count}" +
                  (matching.Count > 0 ? $", najnowszy z {matching.Max(f => f.ModifiedLocal):yyyy-MM-dd HH:mm}" : "") + " (lista poniżej, z datami modyfikacji)."
                : "Logowanie zakończone, ale lokalizacja SharePoint nadal niedostępna – brama nie udostępniła sesji usłudze WebClient. " +
                  "Na razie zaloguj się przez Excel („Eksport do Excela”) i uruchom test dostępu.";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task DoLoginAndRun()
    {
        IsRunning = true;
        bool loggedIn;
        try
        {
            loggedIn = await LoginToSharePoint();
        }
        finally
        {
            IsRunning = false;
        }
        if (loggedIn)
            await DoRun();
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
