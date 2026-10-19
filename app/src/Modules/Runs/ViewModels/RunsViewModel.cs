using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>
/// Ekran Przebiegi (docs/funkcjonalnosc.md, F06) – administracja szablonów przebiegów: lista szablonów (Wewnętrzny, CAS, własne),
/// etapy szablonu w kafelkach (Import źródeł i Przypięcie stanu – stałe; etapy walidacji z kontrolami z katalogu; zatwierdzenie),
/// okno Kontrole (ustawienia domyślne kontroli, reguły uzgodnień). Program wybiera szablon w Konfiguracji; przebieg uruchamia
/// i analizuje się na ekranie programu.
/// </summary>
public sealed class RunsViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly RunTemplateService _service;
    private readonly IRunStore _store;
    private IReadOnlyDictionary<string, RunCheckSetting> _defaults = new Dictionary<string, RunCheckSetting>();
    private IReadOnlyList<string> _reconciliations = [];
    private IReadOnlyList<RunTemplate> _templates = [];
    private RunTemplate? _selectedTemplate;
    private RunStepTileViewModel? _selectedStep;
    private string _code = "";
    private string _projectType = RunCheckRules.AllTypes;
    private bool _isActive = true;
    private string _description = "";
    private bool _isNew;
    private bool _isDirty;
    private bool _loading;
    private string _status = "";
    private IReadOnlyList<Issue> _issues = [];

    public RunsViewModel(RunTemplateService service, IRunStore store, Action<Action> openChecks)
    {
        _service = service;
        _store = store;
        Refresh = new AsyncRelayCommand(() => Load(_selectedTemplate?.Definition.Code), () => !Busy.IsBusy);
        CreateStarters = new AsyncRelayCommand(DoCreateStarters, () => !Busy.IsBusy && !IsDirty && MissingStarters);
        NewTemplate = new RelayCommand(_ => StartNew(null), _ => !Busy.IsBusy && !IsDirty);
        CopyTemplate = new RelayCommand(_ => StartNew(_selectedTemplate), _ => !Busy.IsBusy && !IsDirty && _selectedTemplate is not null);
        DeleteTemplate = new AsyncRelayCommand(DoDelete, () => !Busy.IsBusy && !IsDirty && _selectedTemplate is not null);
        AddValidation = new RelayCommand(_ => AddStep(new RunStepDefinition(0, NextName("Walidacja"), RunActions.Checks, RunGates.Confirm, false, [])), _ => HasEditor);
        AddApproval = new RelayCommand(_ => AddStep(new RunStepDefinition(0, NextName("Zatwierdzenie"), RunActions.Approval, RunGates.Confirm, true, [],
            "Analityk zatwierdza wynik przebiegu (komentarz w dzienniku).")), _ => HasEditor);
        MoveLeft = new RelayCommand(p => Move(p as RunStepTileViewModel, -1), p => CanMove(p as RunStepTileViewModel, -1));
        MoveRight = new RelayCommand(p => Move(p as RunStepTileViewModel, 1), p => CanMove(p as RunStepTileViewModel, 1));
        RemoveStep = new RelayCommand(p => Remove(p as RunStepTileViewModel), p => p is RunStepTileViewModel { IsFixed: false });
        Save = new AsyncRelayCommand(DoSave, () => !Busy.IsBusy && HasEditor && IsDirty);
        Discard = new AsyncRelayCommand(() => { IsDirty = false; return Load(_isNew ? null : _selectedTemplate?.Definition.Code); }, () => !Busy.IsBusy && IsDirty);
        OpenChecks = new RelayCommand(_ => openChecks(() => _ = Load(_selectedTemplate?.Definition.Code)), _ => !Busy.IsBusy);
        _ = Load(null);
    }

    public BusyState Busy { get; } = new();

    public ICommand Refresh { get; }
    public ICommand CreateStarters { get; }
    public ICommand NewTemplate { get; }
    public ICommand CopyTemplate { get; }
    public ICommand DeleteTemplate { get; }
    public ICommand AddValidation { get; }
    public ICommand AddApproval { get; }
    public ICommand MoveLeft { get; }
    public ICommand MoveRight { get; }
    public ICommand RemoveStep { get; }
    public ICommand Save { get; }
    public ICommand Discard { get; }

    /// <summary>Okno Kontrole: ustawienia domyślne kontroli, reguły uzgodnień, miary.</summary>
    public ICommand OpenChecks { get; }

    public IReadOnlyList<RunTemplate> Templates { get => _templates; private set => SetProperty(ref _templates, value); }

    public RunTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (ReferenceEquals(value, _selectedTemplate))
                return;
            if (IsDirty)
            {
                Status = "Masz niezapisane zmiany szablonu – zapisz albo odrzuć je przed wyborem innego.";
                OnPropertyChanged();
                return;
            }
            _selectedTemplate = value;
            OnPropertyChanged();
            Edit(value);
        }
    }

    public bool NoTemplates => _templates.Count == 0;

    /// <summary>Brakuje któregoś z szablonów startowych (Wewnętrzny, CAS).</summary>
    public bool MissingStarters => RunDefinitions.Starters.Any(s => _templates.All(t => !string.Equals(t.Definition.Code, s.Code, StringComparison.OrdinalIgnoreCase)));

    public bool HasEditor => _isNew || _selectedTemplate is not null;

    public IReadOnlyList<string> ProjectTypes => RunCheckRules.ProjectTypes;

    /// <summary>Nazwa szablonu – zmiana tylko dla nowego (programy wskazują szablon po nazwie).</summary>
    public string Code { get => _code; set { if (SetProperty(ref _code, value ?? "")) Touch(); } }

    public bool IsNew { get => _isNew; private set { if (SetProperty(ref _isNew, value)) { OnPropertyChanged(nameof(HasEditor)); OnPropertyChanged(nameof(Programs)); } } }

    /// <summary>Typ programu, dla którego szablon jest domyślny (program bez wybranego przebiegu).</summary>
    public string ProjectType { get => _projectType; set { if (SetProperty(ref _projectType, value ?? RunCheckRules.AllTypes)) Touch(); } }

    public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) Touch(); } }

    public string Description { get => _description; set { if (SetProperty(ref _description, value ?? "")) Touch(); } }

    /// <summary>Programy, które wybrały szablon (Konfiguracja → Parametry programu → „Przebieg”).</summary>
    public string Programs => _isNew || _selectedTemplate is null ? ""
        : _selectedTemplate.Programs.Count == 0 ? "Żaden program nie wybrał tego szablonu (programy bez wyboru – szablon domyślny dla typu)."
        : $"Wybrany w programach: {string.Join(", ", _selectedTemplate.Programs)}";

    public ObservableCollection<RunStepTileViewModel> Steps { get; } = [];

    public RunStepTileViewModel? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (SetProperty(ref _selectedStep, value))
                OnPropertyChanged(nameof(HasSelectedStep));
        }
    }

    public bool HasSelectedStep => _selectedStep is not null;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public IReadOnlyList<Issue> Issues { get => _issues; private set => SetProperty(ref _issues, value); }

    private void Touch()
    {
        if (!_loading)
            IsDirty = true;
    }

    /// <summary>Szablony, ustawienia domyślne kontroli, nazwy reguł uzgodnień; wybór – code albo pierwszy.</summary>
    private async Task Load(string? code)
    {
        try
        {
            var loaded = await Busy.Run<Loaded>("Wczytywanie szablonów przebiegów…",
                () => new Loaded(_service.Load(), _service.CheckDefaults(), _store.Reconciliations().Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
            _defaults = loaded.Defaults;
            _reconciliations = loaded.Reconciliations;
            Templates = loaded.Templates;
            OnPropertyChanged(nameof(NoTemplates));
            OnPropertyChanged(nameof(MissingStarters));
            IsDirty = false;
            IsNew = false;
            _selectedTemplate = Templates.FirstOrDefault(t => string.Equals(t.Definition.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Templates.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedTemplate));
            Edit(_selectedTemplate);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Szablony przebiegów");
            Status = $"Nie udało się wczytać szablonów: {ex.Message}";
        }
    }

    private sealed record Loaded(IReadOnlyList<RunTemplate> Templates, IReadOnlyDictionary<string, RunCheckSetting> Defaults, IReadOnlyList<string> Reconciliations);

    /// <summary>Szablon w edytorze: pola i kafelki etapów; null – pusty edytor.</summary>
    private void Edit(RunTemplate? template)
    {
        _loading = true;
        Code = template?.Definition.Code ?? "";
        ProjectType = template?.Definition.ProjectType ?? RunCheckRules.AllTypes;
        IsActive = template?.IsActive ?? true;
        Description = template?.Definition.Description ?? "";
        Fill(template?.Definition.Steps ?? []);
        _loading = false;
        Issues = [];
        OnPropertyChanged(nameof(HasEditor));
        OnPropertyChanged(nameof(Programs));
    }

    private void Fill(IEnumerable<RunStepDefinition> steps)
    {
        Steps.Clear();
        foreach (var step in steps.OrderBy(s => s.No))
            Steps.Add(Tile(step));
        Renumber();
        SelectedStep = Steps.FirstOrDefault(s => s.IsChecks) ?? Steps.FirstOrDefault();
    }

    private RunStepTileViewModel Tile(RunStepDefinition step)
    {
        var tile = new RunStepTileViewModel(step, _defaults, _reconciliations);
        tile.Changed += Touch;
        return tile;
    }

    /// <summary>Nowy szablon: kopia wybranego (nazwa „… – kopia”) albo etapy stałe, walidacja i zatwierdzenie.</summary>
    private void StartNew(RunTemplate? source)
    {
        _selectedTemplate = null;
        OnPropertyChanged(nameof(SelectedTemplate));
        IsNew = true;
        _loading = true;
        var names = _templates.Select(t => t.Definition.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var basis = source is null ? "Nowy przebieg" : $"{source.Definition.Code} – kopia";
        Code = Enumerable.Range(1, 99).Select(i => i == 1 ? basis : $"{basis} {i}").First(n => !names.Contains(n));
        ProjectType = source?.Definition.ProjectType ?? RunCheckRules.AllTypes;
        IsActive = false;
        Description = source?.Definition.Description ?? "";
        Fill(source?.Definition.Steps ??
        [
            .. RunDefinitions.FixedSteps,
            new(3, "Walidacja ACTUALS", RunActions.Checks, RunGates.Confirm, false, []),
            new(4, "Zatwierdzenie analityka", RunActions.Approval, RunGates.Confirm, true, [], "Analityk zatwierdza wynik przebiegu (komentarz w dzienniku)."),
        ]);
        _loading = false;
        IsDirty = true;
        Issues = [];
        Status = "Nowy szablon (nieaktywny) – ustaw etapy i kontrole, zaznacz Aktywny i zapisz.";
    }

    private string NextName(string basis)
    {
        var names = Steps.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(1, 99).Select(i => i == 1 ? basis : $"{basis} {i}").First(n => !names.Contains(n));
    }

    private void AddStep(RunStepDefinition step)
    {
        var tile = Tile(step);
        Steps.Add(tile);
        Renumber();
        SelectedStep = tile;
        IsDirty = true;
    }

    private bool CanMove(RunStepTileViewModel? tile, int delta)
    {
        if (tile is not { IsFixed: false })
            return false;
        var target = Steps.IndexOf(tile) + delta;
        return target >= 0 && target < Steps.Count && !Steps[target].IsFixed;
    }

    private void Move(RunStepTileViewModel? tile, int delta)
    {
        if (!CanMove(tile, delta))
            return;
        var index = Steps.IndexOf(tile!);
        Steps.Move(index, index + delta);
        Renumber();
        IsDirty = true;
    }

    private void Remove(RunStepTileViewModel? tile)
    {
        if (tile is not { IsFixed: false })
            return;
        Steps.Remove(tile);
        Renumber();
        if (ReferenceEquals(SelectedStep, tile))
            SelectedStep = Steps.LastOrDefault();
        IsDirty = true;
    }

    private void Renumber()
    {
        for (var i = 0; i < Steps.Count; i++)
            Steps[i].No = i + 1;
    }

    private async Task DoSave()
    {
        var thresholds = Steps.SelectMany(s => s.InvalidThresholds).ToList();
        if (thresholds.Count > 0)
        {
            Issues = thresholds.Select(t => Issue.Error($"{t} – liczba nieujemna albo puste")).ToList();
            Status = "Popraw progi kontroli.";
            return;
        }
        if (_isNew && _templates.Any(t => string.Equals(t.Definition.Code, Code.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            Status = $"Szablon „{Code.Trim()}” już jest – zmień nazwę.";
            return;
        }
        var definition = new RunDefinition(Code.Trim(), ProjectType, [.. Steps.Select(s => s.ToStep())], Description.Trim().Length == 0 ? null : Description.Trim());
        var active = IsActive;
        try
        {
            var outcome = await Busy.Run($"Zapisywanie szablonu „{definition.Code}”…", () => _service.Save(definition, active));
            Issues = outcome.Issues;
            if (outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges)
            {
                IsDirty = false;
                await Load(definition.Code);
                Issues = outcome.Issues;
            }
            Status = outcome.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Zapis szablonu {Template}", definition.Code);
            Status = $"Zapis nieudany: {ex.Message}";
        }
    }

    private async Task DoDelete()
    {
        var code = _selectedTemplate!.Definition.Code;
        try
        {
            var outcome = await Busy.Run($"Usuwanie szablonu „{code}”…", () => _service.Delete(code));
            if (outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges)
                await Load(null);
            Issues = outcome.Issues;
            Status = outcome.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Usunięcie szablonu {Template}", code);
            Status = $"Usunięcie nieudane: {ex.Message}";
        }
    }

    private async Task DoCreateStarters()
    {
        try
        {
            var message = await Busy.Run("Tworzenie szablonów startowych…", _service.CreateStarters);
            await Load(_selectedTemplate?.Definition.Code);
            Status = message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Szablony startowe");
            Status = $"Nie udało się utworzyć szablonów: {ex.Message}";
        }
    }
}
