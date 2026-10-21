using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>
/// Reguła do wyboru w etapie walidacji (kafelek szablonu): reguła walidacji z poziomem i użytymi tabelami; zaznaczona – element
/// kontrolny etapu; IsVisible – filtr „Szukaj reguły” (nazwa albo tabela).
/// </summary>
public sealed class RunRuleChoice(string name, string level, string description, string tables) : ObservableObject
{
    private bool _isSelected;
    private bool _isVisible = true;

    public string Name { get; } = name;

    /// <summary>Poziom reguły (ERROR / WARNING / INFO); reguła spoza biblioteki – „nieznana”.</summary>
    public string Level { get; } = level;

    public string Description { get; } = description;

    /// <summary>Tabele danych uruchomienia użyte w zapytaniu reguły.</summary>
    public string Tables { get; } = tables;

    public event Action? Changed;

    public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) Changed?.Invoke(); } }

    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }

    /// <summary>Zgodna z tekstem wyszukiwania: nazwa albo tabela (pusty – każda).</summary>
    public bool Matches(string search) =>
        search.Trim().Length == 0 || Name.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase)
        || Tables.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);

    public void Load(bool selected) => _isSelected = selected;
}

/// <summary>
/// Kafelek etapu szablonu przebiegu (ekran Przebiegi): nazwa, akcja, bramka WARNING, zatwierdzenie, opis; etap stały (import źródeł,
/// przypięcie stanu) – bez zmian i przesuwania; etap walidacji – zaznaczanie reguł walidacji (treść reguły – edytor reguł).
/// </summary>
public sealed class RunStepTileViewModel : ObservableObject
{
    private int _no;
    private string _name;
    private string _warningGate;
    private bool _approval;
    private string _description;
    private string _ruleSearch = "";

    public RunStepTileViewModel(RunStepDefinition step, IReadOnlyList<RunRule> library)
    {
        _no = step.No;
        _name = step.Name;
        Action = step.Action;
        _warningGate = step.WarningGate;
        _approval = step.Approval;
        _description = step.Description ?? "";
        Rules = IsChecks ? BuildRules(step, library) : [];
        foreach (var choice in Rules)
            choice.Changed += () => { OnPropertyChanged(nameof(Summary)); Changed?.Invoke(); };
    }

    public event Action? Changed;

    public int No { get => _no; set { if (SetProperty(ref _no, value)) OnPropertyChanged(nameof(Title)); } }

    public string Title => $"{No}. {Name}";

    public string Name { get => _name; set { if (!IsFixed && SetProperty(ref _name, value ?? "")) { OnPropertyChanged(nameof(Title)); Changed?.Invoke(); } } }

    public string Action { get; }

    /// <summary>Etap stały – import źródeł i przypięcie stanu z konfiguracji programu.</summary>
    public bool IsFixed => RunDefinitions.IsFixed(Action);

    public bool IsEditable => !IsFixed;

    public bool IsChecks => Action == RunActions.Checks;

    public bool IsApproval => Action is RunActions.Approval or RunActions.ApproveActuals;

    /// <summary>Klocek systemowy „Zatwierdzenie ACTUALS” – zatwierdzone dane ACTUALS trafiają do struktury programu.</summary>
    public bool IsActualsApproval => Action == RunActions.ApproveActuals;

    public IReadOnlyList<string> Gates => RunGates.All;

    public string WarningGate { get => _warningGate; set { if (SetProperty(ref _warningGate, value ?? RunGates.Confirm)) { OnPropertyChanged(nameof(GateText)); Changed?.Invoke(); } } }

    public bool Approval { get => _approval; set { if (SetProperty(ref _approval, value)) { OnPropertyChanged(nameof(GateText)); Changed?.Invoke(); } } }

    public string Description { get => _description; set { if (SetProperty(ref _description, value ?? "")) Changed?.Invoke(); } }

    /// <summary>Reguły walidacji do zaznaczenia (etap walidacji).</summary>
    public IReadOnlyList<RunRuleChoice> Rules { get; }

    /// <summary>Filtr listy reguł etapu: nazwa albo tabela.</summary>
    public string RuleSearch
    {
        get => _ruleSearch;
        set
        {
            if (!SetProperty(ref _ruleSearch, value ?? ""))
                return;
            foreach (var choice in Rules)
                choice.IsVisible = choice.Matches(_ruleSearch);
        }
    }

    /// <summary>Bramka na kafelku: ERROR zatrzymuje; WARNING – potwierdzenie albo informacja; zatwierdzenie ręczne.</summary>
    public string GateText =>
        IsApproval || Approval ? "czeka na zatwierdzenie" : IsChecks ? $"WARNING: {WarningGate.ToLowerInvariant()}" : "stały – konfiguracja programu";

    /// <summary>Zawartość kafelka: liczba zaznaczonych reguł i pierwsze z nich.</summary>
    public string Summary
    {
        get
        {
            if (!IsChecks)
                return IsFixed ? "klocek predefiniowany" : IsActualsApproval ? "klocek systemowy – zatwierdzone dane ACTUALS w strukturze programu" : "";
            var selected = Rules.Where(c => c.IsSelected).Select(c => c.Name).ToList();
            return selected.Count == 0 ? "brak reguł – zaznacz"
                : $"{selected.Count} {(selected.Count == 1 ? "reguła" : selected.Count % 10 is >= 2 and <= 4 && selected.Count / 10 % 10 != 1 ? "reguły" : "reguł")}: "
                  + $"{string.Join(", ", selected.Take(3))}{(selected.Count > 3 ? "…" : "")}";
        }
    }

    public RunStepDefinition ToStep() =>
        new(No, Name.Trim(), Action, IsChecks ? WarningGate : RunGates.Info, IsApproval || Approval,
            [.. Rules.Where(c => c.IsSelected).Select(c => new RunElementSetting(c.Name))], Description.Trim().Length == 0 ? null : Description.Trim());

    /// <summary>Reguły biblioteki w jej kolejności; reguły etapu spoza biblioteki (np. usunięte) – na końcu jako „nieznana” (odznaczenie usuwa).</summary>
    private static IReadOnlyList<RunRuleChoice> BuildRules(RunStepDefinition step, IReadOnlyList<RunRule> library)
    {
        var used = step.Elements.Where(e => e.Active).Select(e => e.Element.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = library.Select(r => Choice(r.Name, (r.IsActive ? "" : "wyłączona · ") + RunRules.LevelText(r.Level), r.Description ?? "",
            string.Join(", ", RunRules.TablesUsed(r.Sql)))).ToList();
        var known = library.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        choices.AddRange(used.Where(n => !known.Contains(n)).Select(n => Choice(n, "nieznana", "Reguły nie ma w regułach walidacji (usunięta albo zmieniona nazwa) – odznacz, aby usunąć z etapu.", "")));
        return choices;

        RunRuleChoice Choice(string name, string level, string description, string tables)
        {
            var choice = new RunRuleChoice(name, level, description, tables);
            choice.Load(used.Contains(name));
            return choice;
        }
    }
}
