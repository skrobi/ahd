using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Administration.Models;
using PzlEv.Modules.Administration.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Administration.ViewModels;

/// <summary>
/// Ekran Administracja (F08): definicje źródeł (kod, prefiks, typ raportu, parser), parsery (ParserEditor – układ pliku,
/// typy, pola wymagane) i lokalizacje RABIT – dodanie, zmiana, dezaktywacja, usunięcie definicji, historia.
/// </summary>
public sealed class AdministrationViewModel : ObservableObject
{
    private readonly SourceConfigService _service;
    private SourceDefinitionRow? _selectedDefinition;
    private SourceLocationRow? _selectedLocation;
    private string _status = "";
    private string _definitionMessage = "";
    private bool _reloadingParsers;   // przebudowa listy parserów – lista wyboru może wtedy wyczyścić DefParser

    // Formularz definicji.
    private long? _defId;
    private int? _defVersion;
    private string _defCode = "", _defPrefix = "", _defReportType = "", _defParser = SourceParsers.None;
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
        NewDefinition = new RelayCommand(_ => ClearDefinitionForm(), _ => !Busy.IsBusy);
        SaveDefinition = new AsyncRelayCommand(DoSaveDefinition, () => !Busy.IsBusy);
        DeleteDefinition = new RelayCommand(_ => AskDeleteDefinition(), _ => _defId is not null && !Busy.IsBusy);
        ConfirmDeleteDefinition = new AsyncRelayCommand(DoDeleteDefinition, () => _deletePending && !Busy.IsBusy);
        NewLocation = new RelayCommand(_ => ClearLocationForm(), _ => !Busy.IsBusy);
        SaveLocation = new AsyncRelayCommand(DoSaveLocation, () => !Busy.IsBusy);
        ParserEditor = new ParserEditorViewModel(service, dialogs, Busy, () => _ = ReloadParsers());
        _ = Reload();
    }

    /// <summary>Operacja w tle (pasek „Trwa: …”) – wspólna z sekcją Parsery.</summary>
    public BusyState Busy { get; } = new();

    /// <summary>Administracja → Parsery.</summary>
    public ParserEditorViewModel ParserEditor { get; }

    public ObservableCollection<SourceDefinitionRow> Definitions { get; } = [];

    public ObservableCollection<SourceLocationRow> Locations { get; } = [];

    public ObservableCollection<SourceDefinitionRow> DefinitionHistory { get; } = [];

    public ObservableCollection<Issue> Issues { get; } = [];

    /// <summary>Parsery do wyboru w definicji (z bazy) i „brak”.</summary>
    public ObservableCollection<ParserOption> ParserOptions { get; } = [];

    public ICommand NewDefinition { get; }
    public ICommand SaveDefinition { get; }
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

    /// <summary>Wynik ostatniej operacji na definicji (pod przyciskami formularza).</summary>
    public string DefinitionMessage { get => _definitionMessage; private set => SetProperty(ref _definitionMessage, value); }

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
            DefParser = value.Parser;
            DefActive = value.Active;
            DeletePending = false;
            DefinitionMessage = "";
            OnPropertyChanged(nameof(DefinitionFormTitle));
            _ = LoadDefinitionHistory(value.DefinitionId);
        }
    }

    private async Task LoadDefinitionHistory(long definitionId)
    {
        DefinitionHistory.Clear();
        try
        {
            var versions = await Task.Run(() => _service.DefinitionHistory(definitionId));   // krótki odczyt – nie blokuje okna
            if (_selectedDefinition?.DefinitionId != definitionId)
                return;
            foreach (var version in versions.Reverse())
                DefinitionHistory.Add(version);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = $"Nie udało się wczytać historii definicji: {ex.Message}";
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

    public string DefParser
    {
        get => _defParser;
        set
        {
            if (!_reloadingParsers)
                SetProperty(ref _defParser, value ?? SourceParsers.None);
        }
    }

    public bool DefActive { get => _defActive; set => SetProperty(ref _defActive, value); }

    public string LocName { get => _locName; set => SetProperty(ref _locName, value); }
    public string LocPath { get => _locPath; set => SetProperty(ref _locPath, value); }
    public bool LocActive { get => _locActive; set => SetProperty(ref _locActive, value); }

    private async Task Reload()
    {
        try
        {
            var (definitions, locations) = await Busy.Run("Wczytywanie definicji źródeł i lokalizacji…", () => (_service.Definitions(), _service.Locations()));
            Definitions.Clear();
            foreach (var d in definitions)
                Definitions.Add(d);
            Locations.Clear();
            foreach (var l in locations)
                Locations.Add(l);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = $"Nie udało się wczytać konfiguracji: {ex.Message}";
        }
        await ReloadParsers();
    }

    /// <summary>Parsery z bazy (także po zapisie parsera w sekcji Parsery).</summary>
    private async Task ReloadParsers()
    {
        IReadOnlyList<Shared.Models.Db.ParserRow> parsers;
        try
        {
            parsers = await Busy.Run("Wczytywanie parserów…", () => _service.Parsers());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = $"Nie udało się wczytać parserów: {ex.Message}";
            return;
        }
        _reloadingParsers = true;
        try
        {
            ParserOptions.Clear();
            ParserOptions.Add(new ParserOption(SourceParsers.None, "brak – tylko wersja pliku (bez danych)"));
            foreach (var p in parsers)
                ParserOptions.Add(new ParserOption(p.Code, $"{p.Code} – {p.Name}{(p.Active ? "" : " (nieaktywny)")}"));
        }
        finally
        {
            _reloadingParsers = false;
        }
        OnPropertyChanged(nameof(DefParser));
    }

    private void ClearDefinitionForm()
    {
        SelectedDefinition = null;
        _defId = null;
        _defVersion = null;
        DefCode = DefPrefix = DefReportType = "";
        DeletePending = false;
        DefParser = SourceParsers.None;
        DefActive = true;
        DefinitionMessage = "";
        DefinitionHistory.Clear();
        OnPropertyChanged(nameof(DefinitionFormTitle));
        Status = "Nowa definicja – kod, prefiks nazwy pliku i parser (układ kolumn pilnuje parser).";
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

    private async Task DoSaveDefinition()
    {
        var input = new DefinitionInput(_defId, _defVersion, DefCode, DefPrefix, DefReportType, DefParser, DefActive);
        if (await Run("Zapisywanie definicji źródła…", () => _service.SaveDefinition(input)) is not { } result)
            return;
        await Show(result);
        if (result.Success)   // formularz zostaje na zapisanej definicji
            SelectedDefinition = Definitions.FirstOrDefault(d => d.Code == input.Code.Trim().ToUpperInvariant());
        DefinitionMessage = string.Join(Environment.NewLine, new[] { result.Message }.Concat(result.Issues.Select(i => "• " + i.Message)));
    }

    private void AskDeleteDefinition()
    {
        DeletePending = true;
        Status = $"Usunąć definicję {DefCode} (prefiks {DefPrefix})? Pliki o tym prefiksie nie będą importowane; historia i zaimportowane dane zostają. " +
                 "Kliknij „Potwierdź usunięcie”.";
    }

    private async Task DoDeleteDefinition()
    {
        if (_defId is not { } id || _defVersion is not { } version)
            return;
        DeletePending = false;
        if (await Run("Usuwanie definicji źródła…", () => _service.DeleteDefinition(id, version)) is not { } result)
            return;
        await Show(result);
        DefinitionMessage = result.Message;
    }

    private async Task DoSaveLocation()
    {
        var input = new LocationInput(_locId, _locVersion, LocName, LocPath, LocActive);
        if (await Run("Zapisywanie lokalizacji RABIT…", () => _service.SaveLocation(input)) is { } result)
            await Show(result);
    }

    /// <summary>Zapis w tle; null – błąd (komunikat w Status).</summary>
    private async Task<ConfigSaveResult?> Run(string text, Func<ConfigSaveResult> work)
    {
        try
        {
            return await Busy.Run(text, work);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = $"Zapis nieudany: {ex.Message}";
            return null;
        }
    }

    private async Task Show(ConfigSaveResult result)
    {
        Issues.Clear();
        foreach (var issue in result.Issues)
            Issues.Add(issue);
        OnPropertyChanged(nameof(HasIssues));
        Status = result.Message;
        if (result.Success)
        {
            await Reload();
            ClearLocationForm();
            ClearDefinitionForm();
            Status = result.Message;
        }
    }
}
