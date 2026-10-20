using System.Collections.ObjectModel;
using System.Data;
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
/// Ekran Przebiegi (docs/funkcjonalnosc.md, F06) – szablony przebiegów i reguły walidacji w jednej kolumnie: szablon – etapy
/// w kafelkach (Import źródeł i Przypięcie stanu – stałe; etapy walidacji z zaznaczonymi regułami; zatwierdzenie); reguła – edytor
/// zapytania SQL z listą tabel, zapis (słownik „Reguły walidacji”) i test na programie. Program wybiera szablon w Konfiguracji;
/// przebieg uruchamia i analizuje się na ekranie programu.
/// </summary>
public sealed class RunsViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly RunTemplateService _service;
    private readonly IRunStore _store;
    private IReadOnlyList<RunRule> _library = [];
    private IReadOnlyList<RunTemplate> _templates = [];
    private RunTemplate? _selectedTemplate;
    private RunStepTileViewModel? _selectedStep;
    private string _code = "";
    private string _projectType = RunRules.AllTypes;
    private bool _isActive = true;
    private string _description = "";
    private bool _isNew;
    private bool _isDirty;
    private bool _loading;
    private string _status = "";
    private IReadOnlyList<Issue> _issues = [];
    private RunRuleItemViewModel? _selectedRule;
    private bool _isRuleDirty;
    private bool _rulesAreStarters;
    private string _ruleSearch = "";
    private string _ruleStatus = "";
    private IReadOnlyList<RunProjectRow> _projects = [];
    private RunProjectRow? _testProject;
    private DataView? _testResult;
    private string _testNote = "";

    public RunsViewModel(RunTemplateService service, IRunStore store)
    {
        _service = service;
        _store = store;
        Refresh = new AsyncRelayCommand(() => Load(_selectedTemplate?.Definition.Code, _selectedRule?.Name), () => !Busy.IsBusy && !AnyDirty);
        CreateStarters = new AsyncRelayCommand(DoCreateStarters, () => !Busy.IsBusy && !AnyDirty && MissingStarters);
        NewTemplate = new RelayCommand(_ => StartNew(null), _ => !Busy.IsBusy && !AnyDirty);
        CopyTemplate = new RelayCommand(_ => StartNew(_selectedTemplate), _ => !Busy.IsBusy && !AnyDirty && _selectedTemplate is not null);
        DeleteTemplate = new AsyncRelayCommand(DoDelete, () => !Busy.IsBusy && !AnyDirty && _selectedTemplate is not null);
        AddValidation = new RelayCommand(_ => AddStep(new RunStepDefinition(0, NextName("Walidacja"), RunActions.Checks, RunGates.Confirm, false, [])), _ => HasEditor);
        AddApproval = new RelayCommand(_ => AddStep(new RunStepDefinition(0, NextName("Zatwierdzenie"), RunActions.Approval, RunGates.Confirm, true, [],
            "Analityk zatwierdza wynik przebiegu (komentarz w dzienniku).")), _ => HasEditor);
        MoveLeft = new RelayCommand(p => Move(p as RunStepTileViewModel, -1), p => CanMove(p as RunStepTileViewModel, -1));
        MoveRight = new RelayCommand(p => Move(p as RunStepTileViewModel, 1), p => CanMove(p as RunStepTileViewModel, 1));
        RemoveStep = new RelayCommand(p => Remove(p as RunStepTileViewModel), p => p is RunStepTileViewModel { IsFixed: false });
        Save = new AsyncRelayCommand(DoSave, () => !Busy.IsBusy && HasEditor && IsDirty);
        Discard = new AsyncRelayCommand(() => { IsDirty = false; return Load(_isNew ? null : _selectedTemplate?.Definition.Code); }, () => !Busy.IsBusy && IsDirty);
        NewRule = new RelayCommand(_ => AddRule(new RunRule(NewRuleName("Nowa reguła"), CheckLevel.Warning, true,
            "SELECT ElementWBS, SUM(Kwota) AS Saldo\nFROM biezace\nGROUP BY ElementWBS\nHAVING SUM(Kwota) < -1\nORDER BY SUM(Kwota)")), _ => !Busy.IsBusy && !AnyDirty);
        CopyRule = new RelayCommand(_ => AddRule(_selectedRule!.ToRule() with { Name = NewRuleName($"{_selectedRule!.Name.Trim()} – kopia") }),
            _ => !Busy.IsBusy && !AnyDirty && _selectedRule is not null);
        DeleteRule = new AsyncRelayCommand(DoDeleteRule, () => !Busy.IsBusy && _selectedRule is { Saved: not null } && !IsRuleDirty);
        SaveRule = new AsyncRelayCommand(DoSaveRule, () => !Busy.IsBusy && _selectedRule is not null && (IsRuleDirty || RulesAreStarters));
        DiscardRule = new RelayCommand(_ => DoDiscardRule(), _ => !Busy.IsBusy && IsRuleDirty);
        TestRule = new AsyncRelayCommand(DoTestRule, () => !Busy.IsBusy && _selectedRule is not null && _testProject is not null);
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

    public ICommand NewRule { get; }
    public ICommand CopyRule { get; }
    public ICommand DeleteRule { get; }
    public ICommand SaveRule { get; }
    public ICommand DiscardRule { get; }

    /// <summary>Test zaznaczonej reguły (także niezapisanej) na ostatnim uruchomieniu wybranego programu.</summary>
    public ICommand TestRule { get; }

    public IReadOnlyList<RunTemplate> Templates { get => _templates; private set => SetProperty(ref _templates, value); }

    public RunTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (ReferenceEquals(value, _selectedTemplate))
                return;
            if (IsDirty || IsRuleDirty && value is not null)
            {
                Status = RuleStatus = IsDirty ? "Masz niezapisane zmiany szablonu – zapisz albo odrzuć je przed wyborem innego."
                    : "Masz niezapisane zmiany reguły – zapisz albo odrzuć je przed wyborem szablonu.";
                OnPropertyChanged();
                return;
            }
            _selectedTemplate = value;
            OnPropertyChanged();
            if (value is not null)
                SelectRule(null);
            Edit(value);
        }
    }

    public bool NoTemplates => _templates.Count == 0;

    /// <summary>Brakuje któregoś z szablonów startowych (Wewnętrzny, CAS).</summary>
    public bool MissingStarters => RunDefinitions.Starters.Any(s => _templates.All(t => !string.Equals(t.Definition.Code, s.Code, StringComparison.OrdinalIgnoreCase)));

    public bool HasEditor => _isNew || _selectedTemplate is not null;

    public IReadOnlyList<string> ProjectTypes => RunRules.ProjectTypes;

    /// <summary>Nazwa szablonu – zmiana tylko dla nowego (programy wskazują szablon po nazwie).</summary>
    public string Code { get => _code; set { if (SetProperty(ref _code, value ?? "")) Touch(); } }

    public bool IsNew { get => _isNew; private set { if (SetProperty(ref _isNew, value)) { OnPropertyChanged(nameof(HasEditor)); OnPropertyChanged(nameof(Programs)); } } }

    /// <summary>Typ programu, dla którego szablon jest domyślny (program bez wybranego przebiegu).</summary>
    public string ProjectType { get => _projectType; set { if (SetProperty(ref _projectType, value ?? RunRules.AllTypes)) Touch(); } }

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

    /// <summary>Reguły walidacji (słownik „Reguły walidacji”; pusty – reguły startowe).</summary>
    public ObservableCollection<RunRuleItemViewModel> Rules { get; } = [];

    public string RulesTitle => $"Reguły ({Rules.Count})";

    /// <summary>Słownik reguł pusty – na liście reguły startowe (obowiązują w przebiegu); zapis tworzy wiersze do zmiany.</summary>
    public bool RulesAreStarters { get => _rulesAreStarters; private set => SetProperty(ref _rulesAreStarters, value); }

    /// <summary>Filtr listy reguł: nazwa albo tabela (np. ev).</summary>
    public string RuleSearch
    {
        get => _ruleSearch;
        set
        {
            if (!SetProperty(ref _ruleSearch, value ?? ""))
                return;
            foreach (var rule in Rules)
                rule.IsVisible = rule.Matches(_ruleSearch);
        }
    }

    public RunRuleItemViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (ReferenceEquals(value, _selectedRule))
                return;
            if (IsRuleDirty || IsDirty && value is not null)
            {
                RuleStatus = Status = IsRuleDirty ? "Masz niezapisane zmiany reguły – zapisz albo odrzuć je przed wyborem innej."
                    : "Masz niezapisane zmiany szablonu – zapisz albo odrzuć je przed wyborem reguły.";
                OnPropertyChanged();
                return;
            }
            SelectRule(value);
        }
    }

    public bool HasRule => _selectedRule is not null;

    public bool IsRuleDirty
    {
        get => _isRuleDirty;
        private set
        {
            if (SetProperty(ref _isRuleDirty, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool AnyDirty => IsDirty || IsRuleDirty;

    public string RuleStatus { get => _ruleStatus; private set => SetProperty(ref _ruleStatus, value); }

    public IReadOnlyList<string> Levels => RunRules.Levels;

    /// <summary>Tabele i parametry dostępne w zapytaniu reguły (podpowiedź obok pola SQL).</summary>
    public IReadOnlyList<RunRuleTable> RuleTables { get; } = [.. RunRules.Tables, .. RunRules.Parameters];

    public IReadOnlyList<RunProjectRow> Projects { get => _projects; private set => SetProperty(ref _projects, value); }

    /// <summary>Program do testu reguły (ostatnie uruchomienie z przypiętymi danymi).</summary>
    public RunProjectRow? TestProject { get => _testProject; set => SetProperty(ref _testProject, value); }

    /// <summary>Wynik testu: kolumny zwrócone przez zapytanie i opis wyniku.</summary>
    public DataView? TestResult { get => _testResult; private set => SetProperty(ref _testResult, value); }

    public string TestNote { get => _testNote; private set => SetProperty(ref _testNote, value); }

    private void Touch()
    {
        if (!_loading)
            IsDirty = true;
    }

    /// <summary>Szablony, reguły walidacji, programy do testu; wybór – szablon code albo reguła rule, a bez nich pierwszy szablon.</summary>
    private async Task Load(string? code, string? rule = null)
    {
        try
        {
            var loaded = await Busy.Run<Loaded>("Wczytywanie szablonów i reguł…", () =>
            {
                var (rules, starters) = _service.Rules();
                return new Loaded(_service.Load(), rules, starters, _store.Projects());
            });
            _library = loaded.Rules;
            FillRules(loaded.Rules, loaded.Starters);
            Projects = loaded.Projects;
            TestProject = loaded.Projects.FirstOrDefault(p => p.Code == _testProject?.Code) ?? loaded.Projects.FirstOrDefault();
            Templates = loaded.Templates;
            OnPropertyChanged(nameof(NoTemplates));
            OnPropertyChanged(nameof(MissingStarters));
            IsDirty = false;
            IsNew = false;
            var selectedRule = rule is null ? null : Rules.FirstOrDefault(r => string.Equals(r.Name, rule, StringComparison.OrdinalIgnoreCase));
            _selectedTemplate = selectedRule is not null ? null
                : Templates.FirstOrDefault(t => string.Equals(t.Definition.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Templates.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedTemplate));
            Edit(_selectedTemplate);
            SelectRule(selectedRule);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Szablony przebiegów");
            Status = $"Nie udało się wczytać szablonów: {ex.Message}";
        }
    }

    private sealed record Loaded(IReadOnlyList<RunTemplate> Templates, IReadOnlyList<RunRule> Rules, bool Starters, IReadOnlyList<RunProjectRow> Projects);

    /// <summary>Szablon w edytorze: pola i kafelki etapów; null – pusty edytor.</summary>
    private void Edit(RunTemplate? template)
    {
        _loading = true;
        Code = template?.Definition.Code ?? "";
        ProjectType = template?.Definition.ProjectType ?? RunRules.AllTypes;
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
        var tile = new RunStepTileViewModel(step, _library);
        tile.Changed += Touch;
        return tile;
    }

    /// <summary>Nowy szablon: kopia wybranego (nazwa „… – kopia”) albo etapy stałe, walidacja i zatwierdzenie.</summary>
    private void StartNew(RunTemplate? source)
    {
        SelectRule(null);
        _selectedTemplate = null;
        OnPropertyChanged(nameof(SelectedTemplate));
        IsNew = true;
        _loading = true;
        var names = _templates.Select(t => t.Definition.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var basis = source is null ? "Nowy przebieg" : $"{source.Definition.Code} – kopia";
        Code = Enumerable.Range(1, 99).Select(i => i == 1 ? basis : $"{basis} {i}").First(n => !names.Contains(n));
        ProjectType = source?.Definition.ProjectType ?? RunRules.AllTypes;
        IsActive = false;
        Description = source?.Definition.Description ?? "";
        Fill(source?.Definition.Steps ??
        [
            .. RunDefinitions.FixedSteps,
            new(3, RunRules.ActualsArea, RunActions.Checks, RunGates.Confirm, false, []),
            new(4, "Zatwierdzenie analityka", RunActions.Approval, RunGates.Confirm, true, [], "Analityk zatwierdza wynik przebiegu (komentarz w dzienniku)."),
        ]);
        _loading = false;
        IsDirty = true;
        Issues = [];
        Status = "Nowy szablon (nieaktywny) – ustaw etapy i reguły, zaznacz Aktywny i zapisz.";
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

    // Reguły walidacji: lista, wybór, edycja, zapis i test.

    private void FillRules(IReadOnlyList<RunRule> rules, bool starters)
    {
        Rules.Clear();
        foreach (var rule in rules)
            Rules.Add(RuleItem(rule, saved: true));
        RulesAreStarters = starters;
        IsRuleDirty = false;
        OnPropertyChanged(nameof(RulesTitle));
    }

    private RunRuleItemViewModel RuleItem(RunRule rule, bool saved)
    {
        var item = new RunRuleItemViewModel(rule, saved) { IsVisible = true };
        item.IsVisible = item.Matches(_ruleSearch);
        item.Changed += () => IsRuleDirty = true;
        return item;
    }

    /// <summary>Reguła w edytorze (null – brak); zaznaczenie reguły zdejmuje zaznaczenie szablonu.</summary>
    private void SelectRule(RunRuleItemViewModel? rule)
    {
        _selectedRule = rule;
        OnPropertyChanged(nameof(SelectedRule));
        OnPropertyChanged(nameof(HasRule));
        TestResult = null;
        TestNote = "";
        RuleStatus = RulesAreStarters && rule is not null
            ? "Reguły startowe aplikacji – obowiązują, dopóki ich nie zapiszesz; „Zapisz regułę” zapisuje wszystkie do słownika (potem można je zmieniać)." : "";
        if (rule is null || _selectedTemplate is null)
            return;
        _selectedTemplate = null;
        OnPropertyChanged(nameof(SelectedTemplate));
        Edit(null);
    }

    private string NewRuleName(string basis)
    {
        var names = Rules.Select(r => r.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(1, 99).Select(i => i == 1 ? basis : $"{basis} {i}").First(n => !names.Contains(n));
    }

    private void AddRule(RunRule rule)
    {
        var item = RuleItem(rule, saved: false);
        Rules.Add(item);
        OnPropertyChanged(nameof(RulesTitle));
        item.IsVisible = true;
        SelectRule(item);
        IsRuleDirty = true;
        RuleStatus = "Nowa reguła – wpisz zapytanie, przetestuj na programie i zapisz.";
    }

    private void DoDiscardRule()
    {
        var rule = _selectedRule;
        IsRuleDirty = false;
        if (rule?.Saved is null)
        {
            if (rule is not null)
                Rules.Remove(rule);
            OnPropertyChanged(nameof(RulesTitle));
            SelectRule(null);
            Edit(_selectedTemplate = Templates.FirstOrDefault());
            OnPropertyChanged(nameof(SelectedTemplate));
            return;
        }
        var restored = RuleItem(rule.Saved, saved: true);
        Rules[Rules.IndexOf(rule)] = restored;
        SelectRule(restored);
    }

    /// <summary>Zapis reguły: cały słownik „Reguły walidacji” – reguły zapisane i zmieniona (reguły startowe – wszystkie trafiają do słownika).</summary>
    private async Task DoSaveRule()
    {
        var item = _selectedRule!;
        var rule = item.ToRule();
        if (item.HasError)
        {
            RuleStatus = $"Reguła nie zapisana – {item.Error}.";
            return;
        }
        if (Rules.Any(r => !ReferenceEquals(r, item) && string.Equals(r.Name.Trim(), rule.Name, StringComparison.OrdinalIgnoreCase)))
        {
            RuleStatus = $"Reguła „{rule.Name}” już jest – zmień nazwę.";
            return;
        }
        var rules = Rules.Select(r => ReferenceEquals(r, item) ? rule : r.Saved).OfType<RunRule>().ToList();
        await SaveRules(rules, rule.Name, $"Zapisywanie reguły „{rule.Name}”…");
    }

    /// <summary>Usunięcie reguły (historia zostaje); reguła zaznaczona w szablonach – odmowa.</summary>
    private async Task DoDeleteRule()
    {
        var item = _selectedRule!;
        var used = _templates.Where(t => t.Definition.Steps.Any(s => s.Elements.Any(e => string.Equals(e.Element.Trim(), item.Name.Trim(), StringComparison.OrdinalIgnoreCase))))
            .Select(t => t.Definition.Code).ToList();
        if (used.Count > 0)
        {
            RuleStatus = $"Reguła jest zaznaczona w szablonach: {string.Join(", ", used)} – najpierw odznacz ją w etapach.";
            return;
        }
        var rules = Rules.Where(r => !ReferenceEquals(r, item)).Select(r => r.Saved).OfType<RunRule>().ToList();
        await SaveRules(rules, null, $"Usuwanie reguły „{item.Name}”…");
    }

    private async Task SaveRules(IReadOnlyList<RunRule> rules, string? select, string busy)
    {
        try
        {
            var outcome = await Busy.Run(busy, () => _service.SaveRules(rules));
            if (outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges)
            {
                IsRuleDirty = false;
                await Load(_selectedTemplate?.Definition.Code, select);
                RuleStatus = outcome.Status == SaveStatus.Saved ? outcome.Message : "Bez zmian.";
            }
            else
                RuleStatus = $"{outcome.Message} {string.Join("; ", outcome.Issues.Where(i => i.Level == CheckLevel.Error).Take(5).Select(i => i.Message))}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Zapis reguł walidacji");
            RuleStatus = $"Zapis nieudany: {ex.Message}";
        }
    }

    private async Task DoTestRule()
    {
        var rule = _selectedRule!.ToRule();
        var project = _testProject!.Code;
        try
        {
            var test = await Busy.Run($"Test reguły „{rule.Name}” na programie {project}…", () => _service.TestRule(rule, project));
            TestResult = test.Result.Error is null ? Table(test).DefaultView : null;
            TestNote = test.Result.Error is { } error ? $"{test.Note} Błąd: {error}"
                : test.Note + (test.UnknownColumns.Count == 0 ? "" : $" Opis wyniku – nieznane kolumny: {string.Join(", ", test.UnknownColumns.Select(c => $"{{{c}}}"))}.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Test reguły {Rule}", rule.Name);
            TestNote = $"Test nieudany: {ex.Message}";
        }
    }

    // Wynik testu jako tabela: kolumny zapytania (wartości w formacie polskim) i opis wyniku; znaki psujące wiązanie kolumny
    // DataGrid (. / [ ] ( ) ^) w nazwie – podkreślenie.
    private static DataTable Table(RunRuleTest test)
    {
        var table = new DataTable();
        foreach (var column in test.Result.Columns)
        {
            var name = string.Concat(column.Name.Select(c => ".[]()/^".Contains(c) ? '_' : c));
            table.Columns.Add(Enumerable.Range(1, 99).Select(i => i == 1 ? name : $"{name}_{i}").First(n => !table.Columns.Contains(n)), typeof(string));
        }
        var description = Enumerable.Range(1, 99).Select(i => i == 1 ? "Opis wyniku" : $"Opis wyniku {i}").First(n => !table.Columns.Contains(n));
        table.Columns.Add(description, typeof(string));
        for (var i = 0; i < test.Result.Rows.Count; i++)
            table.Rows.Add([.. test.Result.Rows[i].Select(v => (object)(v is null ? "" : RunRules.Format(v))), test.Descriptions[i]]);
        return table;
    }
}
