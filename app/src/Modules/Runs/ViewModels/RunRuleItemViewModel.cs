using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>
/// Reguła walidacji na ekranie Przebiegi (lista i edytor): pola reguły, kontrola zapytania (RunRules.Check) przy każdej zmianie,
/// użyte tabele; Saved – reguła zapisana (null – nowa, niezapisana); IsVisible – filtr „Szukaj” (nazwa albo tabela).
/// </summary>
public sealed class RunRuleItemViewModel : ObservableObject
{
    private string _name;
    private string _description;
    private string _level;
    private bool _isActive;
    private string _sql;
    private string _resultTemplate;
    private string _error = "";
    private bool _isVisible = true;

    public RunRuleItemViewModel(RunRule rule, bool saved)
    {
        Saved = saved ? rule : null;
        _name = rule.Name;
        _description = rule.Description ?? "";
        _level = RunRules.LevelText(rule.Level);
        _isActive = rule.IsActive;
        _sql = rule.Sql;
        _resultTemplate = rule.ResultTemplate ?? "";
        Recheck();
    }

    /// <summary>Reguła w słowniku (albo reguła startowa, gdy słownik pusty); null – nowa.</summary>
    public RunRule? Saved { get; }

    public event Action? Changed;

    public string Name { get => _name; set => Set(ref _name, value); }

    public string Description { get => _description; set => Set(ref _description, value); }

    public string Level { get => _level; set => Set(ref _level, value); }

    public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) Changed?.Invoke(); } }

    public string Sql { get => _sql; set => Set(ref _sql, value); }

    public string ResultTemplate { get => _resultTemplate; set => Set(ref _resultTemplate, value); }

    /// <summary>Błąd zapytania (pusty – zapytanie dopuszczalne).</summary>
    public string Error { get => _error; private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }

    public bool HasError => Error.Length > 0;

    /// <summary>Tabele danych uruchomienia użyte w zapytaniu (lista reguł).</summary>
    public string Tables => RunRules.TablesUsed(_sql) is { Count: > 0 } tables ? $"Dane: {string.Join(", ", tables)}" : "Dane: –";

    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }

    public bool Matches(string search) =>
        search.Trim().Length == 0 || Name.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase)
        || RunRules.TablesUsed(_sql).Any(t => t.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

    public RunRule ToRule() =>
        new(Name.Trim(), RunRules.LevelOf(Level) ?? CheckLevel.Warning, IsActive, Sql.Trim(), Blank(ResultTemplate), Blank(Description));

    private void Set(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value ?? "", name))
            return;
        if (name is nameof(Sql) or nameof(Name))
            Recheck();
        if (name == nameof(Sql))
            OnPropertyChanged(nameof(Tables));
        Changed?.Invoke();
    }

    private void Recheck() =>
        Error = Name.Trim().Length == 0 ? "Nazwa reguły pusta" : Name.Trim().Length > 200 ? "Nazwa reguły – najwyżej 200 znaków" : RunRules.Check(_sql) ?? "";

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
