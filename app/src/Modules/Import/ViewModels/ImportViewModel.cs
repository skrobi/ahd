using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Models;
using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Import.ViewModels;

/// <summary>Ekran Import RABIT (F05): uruchomienie importu, wynik dla każdego pliku, problemy, historia importów.</summary>
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

    public ImportViewModel(ImportService service, IImportStore store)
    {
        _service = service;
        _store = store;
        Run = new AsyncRelayCommand(DoRun, () => !_isRunning);
        Cancel = new RelayCommand(_ => _cancellation?.Cancel(), _ => _isRunning);
        Refresh = new RelayCommand(_ => Reload(), _ => !_isRunning);
        Reload();
    }

    public ObservableCollection<ImportLocation> Locations { get; } = [];

    public ObservableCollection<FileResult> Files { get; } = [];

    public ObservableCollection<Issue> Issues { get; } = [];

    public ObservableCollection<ImportBatchRow> Batches { get; } = [];

    public ObservableCollection<SourceFileSeenRow> BatchFiles { get; } = [];

    public ICommand Run { get; }

    public ICommand Cancel { get; }

    public ICommand Refresh { get; }

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

    private void Reload()
    {
        Locations.Clear();
        foreach (var location in _service.Locations())
            Locations.Add(location);
        Batches.Clear();
        foreach (var batch in _store.Batches(50))
            Batches.Add(batch);
        OnPropertyChanged(nameof(DefinitionsText));
    }
}
