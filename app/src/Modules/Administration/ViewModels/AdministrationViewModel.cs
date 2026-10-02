using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>Ekran Administracja (F08): definicje źródeł i lokalizacje RABIT – dodanie, zmiana, dezaktywacja, historia.</summary>
public sealed class AdministrationViewModel : ObservableObject
{
    private readonly SourceConfigService _service;
    private SourceDefinitionRow? _selectedDefinition;
    private SourceLocationRow? _selectedLocation;
    private string _status = "";

    // Formularz definicji.
    private long? _defId;
    private int? _defVersion;
    private string _defCode = "", _defPrefix = "", _defReportType = "", _defColumns = "", _defGrain = "", _defKeyColumns = "";
    private string _defPeriodMeaning = "", _defCurrency = "", _defNumberFormat = "", _defParser = SourceParsers.Actuals;
    private bool _defActive = true;

    // Formularz lokalizacji.
    private long? _locId;
    private int? _locVersion;
    private string _locName = "", _locPath = "";
    private bool _locActive = true;

    public AdministrationViewModel(SourceConfigService service)
    {
        _service = service;
        NewDefinition = new RelayCommand(_ => ClearDefinitionForm());
        SaveDefinition = new RelayCommand(_ => DoSaveDefinition());
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
    public ICommand NewLocation { get; }
    public ICommand SaveLocation { get; }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool HasIssues => Issues.Count > 0;

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
            DefGrain = value.Grain;
            DefKeyColumns = string.Join(", ", value.KeyColumns);
            DefPeriodMeaning = value.PeriodMeaning;
            DefCurrency = value.Currency;
            DefNumberFormat = value.NumberFormat;
            DefParser = value.Parser;
            DefActive = value.Active;
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
    public string DefColumns { get => _defColumns; set => SetProperty(ref _defColumns, value); }
    public string DefGrain { get => _defGrain; set => SetProperty(ref _defGrain, value); }
    public string DefKeyColumns { get => _defKeyColumns; set => SetProperty(ref _defKeyColumns, value); }
    public string DefPeriodMeaning { get => _defPeriodMeaning; set => SetProperty(ref _defPeriodMeaning, value); }
    public string DefCurrency { get => _defCurrency; set => SetProperty(ref _defCurrency, value); }
    public string DefNumberFormat { get => _defNumberFormat; set => SetProperty(ref _defNumberFormat, value); }
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
        DefCode = DefPrefix = DefReportType = DefGrain = DefKeyColumns = DefPeriodMeaning = DefCurrency = DefNumberFormat = "";
        DefColumns = string.Join(Environment.NewLine, SourceParsers.ActualsColumns);
        DefParser = SourceParsers.Actuals;
        DefActive = true;
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
            DefColumns.Split('\n').Select(c => c.Trim()).ToList(), DefGrain,
            DefKeyColumns.Split([',', ';']).Select(c => c.Trim()).ToList(),
            DefPeriodMeaning, DefCurrency, DefNumberFormat, DefParser, DefActive);
        Show(_service.SaveDefinition(input));
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
