using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>Ekran Administracja (F08): definicje źródeł i lokalizacje RABIT – dodanie, zmiana, dezaktywacja, usunięcie definicji, historia.</summary>
public sealed class AdministrationViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", ModuleKeys.Administration);

    private readonly SourceConfigService _service;
    private readonly IFileDialogs _dialogs;
    private string _definitionMessage = "";
    private SourceDefinitionRow? _selectedDefinition;
    private SourceLocationRow? _selectedLocation;
    private string _status = "";

    // Formularz definicji.
    private long? _defId;
    private int? _defVersion;
    private string _defCode = "", _defPrefix = "", _defReportType = "", _defColumns = "", _defParser = SourceParsers.Actuals;
    private bool _defActive = true;
    private bool _deletePending;

    // Formularz lokalizacji.
    private long? _locId;
    private int? _locVersion;
    private string _locName = "", _locPath = "";
    private bool _locActive = true;

    public AdministrationViewModel(SourceConfigService service, IFileDialogs dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        ColumnsFromFile = new RelayCommand(_ => LoadColumnsFromFile());
        NewDefinition = new RelayCommand(_ => ClearDefinitionForm());
        SaveDefinition = new RelayCommand(_ => DoSaveDefinition());
        DeleteDefinition = new RelayCommand(_ => AskDeleteDefinition(), _ => _defId is not null);
        ConfirmDeleteDefinition = new RelayCommand(_ => DoDeleteDefinition(), _ => _deletePending);
        NewLocation = new RelayCommand(_ => ClearLocationForm());
        SaveLocation = new RelayCommand(_ => DoSaveLocation());
        Reload();
    }

    public ObservableCollection<SourceDefinitionRow> Definitions { get; } = [];

    public ObservableCollection<SourceLocationRow> Locations { get; } = [];

    public ObservableCollection<SourceDefinitionRow> DefinitionHistory { get; } = [];

    public ObservableCollection<Issue> Issues { get; } = [];

    public IReadOnlyList<ParserOption> ParserOptions { get; } =
    [
        new(SourceParsers.Actuals, "ACTUALS – koszty rzeczywiste CES"),
        new(SourceParsers.None, "brak – tylko wiersze surowe"),
    ];

    public ICommand NewDefinition { get; }
    public ICommand SaveDefinition { get; }
    public ICommand ColumnsFromFile { get; }
    public ICommand DeleteDefinition { get; }
    public ICommand ConfirmDeleteDefinition { get; }
    public ICommand NewLocation { get; }
    public ICommand SaveLocation { get; }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool HasIssues => Issues.Count > 0;

    /// <summary>Usunięcie definicji czeka na potwierdzenie (drugi przycisk).</summary>
    public bool DeletePending { get => _deletePending; private set => SetProperty(ref _deletePending, value); }

    public string DefinitionFormTitle => _defId is null ? "Nowa definicja źródła" : $"Zmiana definicji {_defCode} (wersja {_defVersion})";

    public string LocationFormTitle => _locId is null ? "Nowa lokalizacja" : $"Zmiana lokalizacji {_locName}";

    public SourceDefinitionRow? SelectedDefinition
    {
        get => _selectedDefinition;
        set
        {
            if (!SetProperty(ref _selectedDefinition, value) || value is null)
                return;
            _defId = value.DefinitionId;
            _defVersion = value.Version;
            DefCode = value.Code;
            DefPrefix = value.Prefix;
            DefReportType = value.ReportType;
            DefColumns = string.Join(Environment.NewLine, value.Columns);
            DefParser = value.Parser;
            DefActive = value.Active;
            DeletePending = false;
            DefinitionMessage = "";
            OnPropertyChanged(nameof(DefinitionFormTitle));
            DefinitionHistory.Clear();
            foreach (var version in _service.DefinitionHistory(value.DefinitionId).Reverse())
                DefinitionHistory.Add(version);
        }
    }

    public SourceLocationRow? SelectedLocation
    {
        get => _selectedLocation;
        set
        {
            if (!SetProperty(ref _selectedLocation, value) || value is null)
                return;
            _locId = value.LocationId;
            _locVersion = value.Version;
            LocName = value.Name;
            LocPath = value.Path;
            LocActive = value.Active;
            OnPropertyChanged(nameof(LocationFormTitle));
        }
    }

    public string DefCode { get => _defCode; set => SetProperty(ref _defCode, value); }
    public string DefPrefix { get => _defPrefix; set => SetProperty(ref _defPrefix, value); }
    public string DefReportType { get => _defReportType; set => SetProperty(ref _defReportType, value); }
    public string DefColumns
    {
        get => _defColumns;
        set
        {
            if (SetProperty(ref _defColumns, value))
                OnPropertyChanged(nameof(DefSignatureText));
        }
    }

    /// <summary>Sygnatura wpisanego układu – do porównania z sygnaturą pliku z komunikatu importu.</summary>
    public string DefSignatureText
    {
        get
        {
            var columns = SourceConfigService.ParseColumns(DefColumns);
            return columns.Count == 0 ? "Brak kolumn." : $"Kolumn: {columns.Count}, sygnatura układu: {SourceConfigService.Signature(columns)}";
        }
    }

    /// <summary>Wynik ostatniej operacji na definicji (pod przyciskami formularza).</summary>
    public string DefinitionMessage { get => _definitionMessage; private set => SetProperty(ref _definitionMessage, value); }
    public string DefParser { get => _defParser; set => SetProperty(ref _defParser, value ?? SourceParsers.None); }
    public bool DefActive { get => _defActive; set => SetProperty(ref _defActive, value); }

    public string LocName { get => _locName; set => SetProperty(ref _locName, value); }
    public string LocPath { get => _locPath; set => SetProperty(ref _locPath, value); }
    public bool LocActive { get => _locActive; set => SetProperty(ref _locActive, value); }

    private void Reload()
    {
        Definitions.Clear();
        foreach (var d in _service.Definitions())
            Definitions.Add(d);
        Locations.Clear();
        foreach (var l in _service.Locations())
            Locations.Add(l);
    }

    private void ClearDefinitionForm()
    {
        SelectedDefinition = null;
        _defId = null;
        _defVersion = null;
        DefCode = DefPrefix = DefReportType = "";
        DeletePending = false;
        DefColumns = string.Join(Environment.NewLine, SourceParsers.ActualsColumns);
        DefParser = SourceParsers.Actuals;
        DefActive = true;
        DefinitionMessage = "";
        DefinitionHistory.Clear();
        OnPropertyChanged(nameof(DefinitionFormTitle));
        Status = "Nowa definicja – kolumny wstępnie wypełnione układem ACTUALS_*; zmień, jeśli źródło ma inny układ.";
    }

    private void ClearLocationForm()
    {
        SelectedLocation = null;
        _locId = null;
        _locVersion = null;
        LocName = LocPath = "";
        LocActive = true;
        OnPropertyChanged(nameof(LocationFormTitle));
    }

    private void DoSaveDefinition()
    {
        var input = new DefinitionInput(_defId, _defVersion, DefCode, DefPrefix, DefReportType,
            SourceConfigService.ParseColumns(DefColumns), DefParser, DefActive);
        var result = _service.SaveDefinition(input);
        Show(result);
        if (result.Success)   // formularz zostaje na zapisanej definicji – widać zapisane kolumny, wersję i sygnaturę
            SelectedDefinition = Definitions.FirstOrDefault(d => d.Code == DefinitionCode(input.Code));
        DefinitionMessage = string.Join(Environment.NewLine, new[] { result.Message }.Concat(result.Issues.Select(i => "• " + i.Message)));
    }

    private static string DefinitionCode(string code) => code.Trim().ToUpperInvariant();

    /// <summary>Oczekiwane kolumny z wiersza nagłówków pliku źródła (ten sam odczyt co przy imporcie).</summary>
    private void LoadColumnsFromFile()
    {
        var path = _dialogs.OpenExcel("Plik źródła – kolumny z wiersza nagłówków");
        if (path is null)
            return;
        try
        {
            var data = TabularFileReader.Read(File.ReadAllBytes(path), Path.GetFileName(path));
            DefColumns = string.Join(Environment.NewLine, data.Headers);
            var signature = HeaderSignature.Compute(data.Headers);
            DefinitionMessage = $"Kolumny z pliku {Path.GetFileName(path)}{(data.Sheet is null ? "" : $" (arkusz {data.Sheet})")}: {data.Headers.Count}, " +
                                $"sygnatura pliku {signature}. Sprawdź i kliknij „Zapisz definicję”." +
                                (data.Headers.Any(string.IsNullOrWhiteSpace) ? " Uwaga: plik ma kolumny bez nagłówka – sygnatura pliku je uwzględnia, definicja nie." : "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Warning(ex, "Kolumny z pliku {Path}: błąd odczytu", path);
            DefinitionMessage = $"Nie udało się odczytać pliku {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void AskDeleteDefinition()
    {
        DeletePending = true;
        Status = $"Usunąć definicję {DefCode} (prefiks {DefPrefix})? Pliki o tym prefiksie nie będą importowane; historia i zaimportowane dane zostają. " +
                 "Kliknij „Potwierdź usunięcie”.";
    }

    private void DoDeleteDefinition()
    {
        if (_defId is not { } id || _defVersion is not { } version)
            return;
        DeletePending = false;
        var result = _service.DeleteDefinition(id, version);
        Show(result);
        DefinitionMessage = result.Message;
    }

    private void DoSaveLocation() =>
        Show(_service.SaveLocation(new LocationInput(_locId, _locVersion, LocName, LocPath, LocActive)));

    private void Show(ConfigSaveResult result)
    {
        Issues.Clear();
        foreach (var issue in result.Issues)
            Issues.Add(issue);
        OnPropertyChanged(nameof(HasIssues));
        Status = result.Message;
        if (result.Success)
        {
            Reload();
            ClearLocationForm();
            ClearDefinitionForm();
            Status = result.Message;
        }
    }
}
