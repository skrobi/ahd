using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>Reguła walidacji w edytorze: pola reguły jako tekst, błędy składni sprawdzane przy każdej zmianie.</summary>
public sealed class RunValidationItemViewModel : ObservableObject
{
    private string _name;
    private string _check;
    private bool _isActive;
    private string _filter;
    private string _groupBy;
    private string _condition;
    private string _amount;
    private string _message;
    private string _ok;
    private string _top;
    private string _level;
    private string _threshold;
    private string _description;
    private string _errors = "";

    public RunValidationItemViewModel(RunValidationRule rule)
    {
        _name = rule.Name;
        _check = rule.Check;
        _isActive = rule.IsActive;
        _filter = rule.Filter ?? "";
        _groupBy = rule.GroupBy ?? "";
        _condition = rule.Condition;
        _amount = rule.Amount ?? "";
        _message = rule.Message;
        _ok = rule.OkMessage ?? "";
        _top = rule.Top.ToString(CultureInfo.InvariantCulture);
        _level = rule.Level switch { CheckLevel.Error => "ERROR", CheckLevel.Warning => "WARNING", _ => "INFO" };
        _threshold = rule.Threshold is { } t ? PolishNumber.ToDisplay(t) : "";
        _description = rule.Description ?? "";
        Recheck();
    }

    public event Action? Changed;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Check { get => _check; set => Set(ref _check, value); }
    public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) Changed?.Invoke(); } }
    public string Filter { get => _filter; set => Set(ref _filter, value); }
    public string GroupBy { get => _groupBy; set => Set(ref _groupBy, value); }
    public string Condition { get => _condition; set => Set(ref _condition, value); }
    public string Amount { get => _amount; set => Set(ref _amount, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    public string OkMessage { get => _ok; set => Set(ref _ok, value); }
    public string Top { get => _top; set => Set(ref _top, value); }
    public string Level { get => _level; set => Set(ref _level, value); }
    public string Threshold { get => _threshold; set => Set(ref _threshold, value); }
    public string Description { get => _description; set => Set(ref _description, value); }

    /// <summary>Błędy składni i pól (puste – reguła poprawna).</summary>
    public string Errors { get => _errors; private set { if (SetProperty(ref _errors, value)) OnPropertyChanged(nameof(HasErrors)); } }

    public bool HasErrors => Errors.Length > 0;

    /// <summary>Reguła startowa o tej nazwie – „Przywróć startową”.</summary>
    public bool IsStarter => RunValidationRules.Starter(Name) is not null;

    public RunValidationRule ToRule() =>
        new(Name.Trim(), Check.Trim(), IsActive, Blank(Filter), Blank(GroupBy), Condition.Trim(), Blank(Amount), Message.Trim(), Blank(OkMessage),
            int.TryParse(Top.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var top) ? top : 0, RunCheckRules.LevelOf(Level) ?? CheckLevel.Warning,
            PolishNumber.TryParse(Threshold, out var threshold) ? threshold : null, Blank(Description));

    private void Set(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value ?? "", name))
            return;
        Recheck();
        if (name == nameof(Name))
            OnPropertyChanged(nameof(IsStarter));
        Changed?.Invoke();
    }

    private void Recheck()
    {
        var errors = new List<string>();
        if (_name.Trim().Length == 0)
            errors.Add("Nazwa: pusta");
        else if (RunCheckRules.ByName(_name) is not null)
            errors.Add("Nazwa zajęta przez regułę wbudowaną (Walidacja EV)");
        if (_check.Trim().Length == 0)
            errors.Add("Kontrola: pusta (wiersz macierzy wyników, np. Jakość danych)");
        if (_top.Trim().Length > 0 && (!int.TryParse(_top.Trim(), out var top) || top < 0))
            errors.Add("Najwyżej: liczba od 0 (0 – wszystkie wyniki)");
        if (_threshold.Trim().Length > 0 && (!PolishNumber.TryParse(_threshold, out var threshold) || threshold < 0))
            errors.Add("Próg: liczba nieujemna albo puste");
        errors.AddRange(RunValidationLanguage.Validate(ToRule()));
        Errors = string.Join(Environment.NewLine, errors);
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Edytor reguł walidacji ACTUALS (okno Kontrole – docs/pipeline-fazy.md, rozdz. 4.1): lista reguł (pusty słownik – reguły startowe
/// do zapisania), edycja formuł z kontrolą składni, podpowiedź pól i funkcji, test reguły na ostatnim uruchomieniu wybranego programu,
/// zapis całego słownika „Reguły walidacji” (historia).
/// </summary>
public sealed class RunValidationEditorViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly RunTemplateService _service;
    private readonly IRunStore _store;
    private RunValidationItemViewModel? _selected;
    private bool _isDirty;
    private bool _starters;
    private string _status = "";
    private IReadOnlyList<RunProjectRow> _projects = [];
    private RunProjectRow? _project;
    private IReadOnlyList<RunValidationResult> _results = [];
    private string _testNote = "";

    public RunValidationEditorViewModel(RunTemplateService service, IRunStore store)
    {
        _service = service;
        _store = store;
        Add = new RelayCommand(_ => AddRule(new RunValidationRule(NewName("Nowa reguła"), RunCheckRules.Quality, true, null, null, "LICZBA() > 0", "SUMA([Kwota])",
            "Opis wyniku: {SUMA([Wiersze])} wierszy", null, RunValidationRules.Top, CheckLevel.Warning, null, null)), _ => !Busy.IsBusy);
        Copy = new RelayCommand(_ => AddRule(_selected!.ToRule() with { Name = NewName($"{_selected!.Name} – kopia") }), _ => _selected is not null && !Busy.IsBusy);
        Remove = new RelayCommand(_ =>
        {
            Rules.Remove(_selected!);
            Selected = Rules.FirstOrDefault();
            Touch();
        }, _ => _selected is not null && !Busy.IsBusy);
        Restore = new RelayCommand(_ =>
        {
            var index = Rules.IndexOf(_selected!);
            var item = Item(RunValidationRules.Starter(_selected!.Name)!);
            Rules[index] = item;
            Selected = item;
            Touch();
        }, _ => _selected is { IsStarter: true } && !Busy.IsBusy);
        Save = new AsyncRelayCommand(DoSave, () => !Busy.IsBusy && (IsDirty || IsStarters));
        Discard = new AsyncRelayCommand(Load, () => !Busy.IsBusy && IsDirty);
        Test = new AsyncRelayCommand(DoTest, () => !Busy.IsBusy && _selected is not null && _project is not null);
        _ = Load();
    }

    public BusyState Busy { get; } = new();

    public ObservableCollection<RunValidationItemViewModel> Rules { get; } = [];

    public RunValidationItemViewModel? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(HasSelected)); } }

    public bool HasSelected => _selected is not null;

    public IReadOnlyList<string> Levels => RunCheckRules.Levels;

    /// <summary>Kontrole (wiersze macierzy wyników) do wyboru – z reguł i katalogu; można wpisać nową.</summary>
    public IReadOnlyList<string> Checks => [.. RunValidationRules.Checks.Concat(Rules.Select(r => r.Check)).Where(c => c.Trim().Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<RunValidationTerm> Terms { get; } =
        [.. RunValidationLanguage.RowFields, .. RunValidationLanguage.ContextFields, .. RunValidationLanguage.Functions];

    public bool IsDirty { get => _isDirty; private set { if (SetProperty(ref _isDirty, value)) CommandManager.InvalidateRequerySuggested(); } }

    /// <summary>Słownik pusty – na liście reguły startowe (obowiązują w przebiegu); zapis tworzy wiersze do edycji.</summary>
    public bool IsStarters { get => _starters; private set => SetProperty(ref _starters, value); }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public IReadOnlyList<RunProjectRow> Projects { get => _projects; private set => SetProperty(ref _projects, value); }

    /// <summary>Program do testu reguły (ostatnie uruchomienie z przypiętymi danymi).</summary>
    public RunProjectRow? Project { get => _project; set => SetProperty(ref _project, value); }

    public IReadOnlyList<RunValidationResult> Results { get => _results; private set => SetProperty(ref _results, value); }

    public string TestNote { get => _testNote; private set => SetProperty(ref _testNote, value); }

    public ICommand Add { get; }
    public ICommand Copy { get; }
    public ICommand Remove { get; }

    /// <summary>Reguła startowa o tej nazwie – formuły jak w aplikacji.</summary>
    public ICommand Restore { get; }

    public ICommand Save { get; }
    public ICommand Discard { get; }

    /// <summary>Test zaznaczonej reguły (także niezapisanej) na ostatnim uruchomieniu wybranego programu.</summary>
    public ICommand Test { get; }

    /// <summary>Zapis reguł – ekran Przebiegi odświeża kafelki szablonów.</summary>
    public event Action? Saved;

    private RunValidationItemViewModel Item(RunValidationRule rule)
    {
        var item = new RunValidationItemViewModel(rule);
        item.Changed += Touch;
        return item;
    }

    private void Touch() => IsDirty = true;

    private void AddRule(RunValidationRule rule)
    {
        var item = Item(rule);
        Rules.Add(item);
        Selected = item;
        Touch();
    }

    private string NewName(string basis)
    {
        var names = Rules.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(1, 99).Select(i => i == 1 ? basis : $"{basis} {i}").First(n => !names.Contains(n));
    }

    private async Task Load()
    {
        try
        {
            var name = _selected?.Name;
            var (rules, starters, projects) = await Busy.Run("Wczytywanie reguł walidacji…", () =>
            {
                var (list, isStarters) = _service.ValidationRules();
                return (list, isStarters, _store.Projects());
            });
            Rules.Clear();
            foreach (var rule in rules)
                Rules.Add(Item(rule));
            IsStarters = starters;
            Projects = projects;
            Project ??= projects.FirstOrDefault();
            Selected = Rules.FirstOrDefault(r => r.Name == name) ?? Rules.FirstOrDefault();
            IsDirty = false;
            OnPropertyChanged(nameof(Checks));
            Status = starters ? "Reguły startowe aplikacji – obowiązują, dopóki ich nie zapiszesz; po zapisie można je zmieniać." : "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Reguły walidacji");
            Status = $"Nie udało się wczytać reguł: {ex.Message}";
        }
    }

    private async Task DoSave()
    {
        var invalid = Rules.Where(r => r.HasErrors).Select(r => r.Name).ToList();
        var duplicates = Rules.GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (invalid.Count > 0 || duplicates.Count > 0)
        {
            Status = invalid.Count > 0 ? $"Popraw reguły z błędami: {string.Join(", ", invalid)}." : $"Nazwy powtórzone: {string.Join(", ", duplicates)}.";
            return;
        }
        var rules = Rules.Select(r => r.ToRule()).ToList();
        try
        {
            var outcome = await Busy.Run("Zapisywanie reguł walidacji…", () => _service.SaveValidations(rules));
            if (outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges)
            {
                await Load();
                Saved?.Invoke();
                Status = outcome.Status == SaveStatus.Saved ? outcome.Message : "Bez zmian.";
            }
            else
                Status = $"{outcome.Message} {string.Join("; ", outcome.Issues.Where(i => i.Level == CheckLevel.Error).Take(5).Select(i => $"{i.Element} {i.Message}"))}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Zapis reguł walidacji");
            Status = $"Zapis nieudany: {ex.Message}";
        }
    }

    private async Task DoTest()
    {
        var rule = _selected!.ToRule();
        var project = _project!.Code;
        try
        {
            var (results, note, error) = await Busy.Run($"Test reguły „{rule.Name}” na programie {project}…", () => _service.TestValidation(rule, project));
            Results = results;
            TestNote = error is null ? note : $"{note} Błąd: {error}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Test reguły {Rule}", rule.Name);
            TestNote = $"Test nieudany: {ex.Message}";
        }
    }
}
