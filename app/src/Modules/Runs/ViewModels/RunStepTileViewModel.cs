using System.Collections.ObjectModel;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>
/// Kontrola do wyboru w etapie walidacji (kafelek szablonu): reguła katalogu albo reguła uzgodnień; zaznaczona – element kontrolny
/// etapu; poziom i próg – ustawienie w tym szablonie (puste – domyślne z okna Kontrole).
/// </summary>
public sealed class RunCheckChoice : ObservableObject
{
    /// <summary>Poziom do wyboru: pusty – domyślny.</summary>
    public static IReadOnlyList<string> LevelChoices { get; } = ["", .. RunCheckRules.Levels];

    private bool _isSelected;
    private string _level = "";
    private string _threshold = "";

    public RunCheckChoice(string element, string name, string? check, string description, string defaultText, bool hasThreshold)
    {
        Element = element;
        Name = name;
        Check = check;
        Description = description;
        DefaultText = defaultText;
        HasThreshold = hasThreshold;
    }

    public string Element { get; }

    public string Name { get; }

    public string? Check { get; }

    public string Description { get; }

    /// <summary>Ustawienie domyślne (okno Kontrole), np. „WARNING, próg 1,00”.</summary>
    public string DefaultText { get; }

    public bool HasThreshold { get; }

    public event Action? Changed;

    public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) Changed?.Invoke(); } }

    public string Level { get => _level; set { if (SetProperty(ref _level, value ?? "")) Changed?.Invoke(); } }

    public string Threshold { get => _threshold; set { if (SetProperty(ref _threshold, value ?? "")) Changed?.Invoke(); } }

    /// <summary>Element kontrolny etapu; null – niezaznaczona. Próg niepoprawny albo ujemny – null (domyślny); kontrola przed zapisem – InvalidThreshold.</summary>
    public RunElementSetting? ToSetting() => !IsSelected ? null
        : new RunElementSetting(Element, true, RunCheckRules.LevelOf(Level), HasThreshold && PolishNumber.TryParse(Threshold, out var t) ? t : null);

    /// <summary>Próg wpisany, ale nie jest liczbą nieujemną.</summary>
    public bool InvalidThreshold => IsSelected && HasThreshold && Threshold.Trim().Length > 0 && (!PolishNumber.TryParse(Threshold, out var t) || t < 0);

    public void Load(RunElementSetting? setting)
    {
        _isSelected = setting is { Active: true };
        _level = setting?.Level switch { CheckLevel.Error => "ERROR", CheckLevel.Warning => "WARNING", CheckLevel.Pass => "INFO", _ => "" };
        _threshold = setting?.Threshold is { } t ? PolishNumber.ToDisplay(t) : "";
    }
}

/// <summary>Grupa kontroli w doborze kontroli etapu (Walidacja ACTUALS, Spójność ze słownikami, Rodzaje kosztu, Reguły uzgodnień).</summary>
public sealed record RunCheckArea(string Name, IReadOnlyList<RunCheckChoice> Choices);

/// <summary>
/// Kafelek etapu szablonu przebiegu (ekran Przebiegi): nazwa, akcja, bramka WARNING, zatwierdzenie, opis; etap stały (import źródeł,
/// przypięcie stanu) – bez zmian i przesuwania; etap walidacji – dobór kontroli z katalogu i reguł uzgodnień.
/// </summary>
public sealed class RunStepTileViewModel : ObservableObject
{
    private int _no;
    private string _name;
    private string _warningGate;
    private bool _approval;
    private string _description;

    public RunStepTileViewModel(RunStepDefinition step, IReadOnlyDictionary<string, RunCheckSetting> defaults, IReadOnlyList<string> reconciliations)
    {
        _no = step.No;
        _name = step.Name;
        Action = step.Action;
        _warningGate = step.WarningGate;
        _approval = step.Approval;
        _description = step.Description ?? "";
        Areas = IsChecks ? BuildAreas(step, defaults, reconciliations) : [];
        foreach (var choice in Areas.SelectMany(a => a.Choices))
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

    public bool IsApproval => Action == RunActions.Approval;

    public IReadOnlyList<string> Gates => RunGates.All;

    public string WarningGate { get => _warningGate; set { if (SetProperty(ref _warningGate, value ?? RunGates.Confirm)) { OnPropertyChanged(nameof(GateText)); Changed?.Invoke(); } } }

    public bool Approval { get => _approval; set { if (SetProperty(ref _approval, value)) { OnPropertyChanged(nameof(GateText)); Changed?.Invoke(); } } }

    public string Description { get => _description; set { if (SetProperty(ref _description, value ?? "")) Changed?.Invoke(); } }

    public IReadOnlyList<RunCheckArea> Areas { get; }

    /// <summary>Bramka na kafelku: ERROR zatrzymuje; WARNING – potwierdzenie albo informacja; zatwierdzenie ręczne.</summary>
    public string GateText =>
        IsApproval || Approval ? "czeka na zatwierdzenie" : IsChecks ? $"WARNING: {WarningGate.ToLowerInvariant()}" : "stały – konfiguracja programu";

    /// <summary>Zawartość kafelka: liczba wybranych kontroli i pierwsze z nich.</summary>
    public string Summary
    {
        get
        {
            if (!IsChecks)
                return IsFixed ? "klocek predefiniowany" : "";
            var selected = Areas.SelectMany(a => a.Choices).Where(c => c.IsSelected).Select(c => c.Name).ToList();
            return selected.Count == 0 ? "brak kontroli – dodaj"
                : $"{selected.Count} {(selected.Count == 1 ? "kontrola" : selected.Count < 5 ? "kontrole" : "kontroli")}: {string.Join(", ", selected.Take(3))}{(selected.Count > 3 ? "…" : "")}";
        }
    }

    public RunStepDefinition ToStep() =>
        new(No, Name.Trim(), Action, IsChecks ? WarningGate : RunGates.Info, IsApproval || Approval,
            [.. Areas.SelectMany(a => a.Choices).Select(c => c.ToSetting()).OfType<RunElementSetting>()], Description.Trim().Length == 0 ? null : Description.Trim());

    public IEnumerable<string> InvalidThresholds => Areas.SelectMany(a => a.Choices).Where(c => c.InvalidThreshold).Select(c => $"{Name}: próg „{c.Threshold}” ({c.Name})");

    /// <summary>Grupy kontroli: katalog w grupach, reguły uzgodnień (wszystkie aktywne i po nazwie); elementy spoza nich – „Inne” (nie giną przy zapisie).</summary>
    private static IReadOnlyList<RunCheckArea> BuildAreas(RunStepDefinition step, IReadOnlyDictionary<string, RunCheckSetting> defaults, IReadOnlyList<string> reconciliations)
    {
        var used = step.Elements.ToDictionary(e => e.Element.Trim(), StringComparer.OrdinalIgnoreCase);
        RunCheckChoice Choice(string element, string name, string? check, string description, string defaultText, bool hasThreshold)
        {
            var choice = new RunCheckChoice(element, name, check, description, defaultText, hasThreshold);
            choice.Load(used.GetValueOrDefault(element));
            used.Remove(element);
            return choice;
        }
        var areas = RunCheckRules.Areas.Select(area => new RunCheckArea(area, RunCheckRules.All.Where(r => RunCheckRules.AreaOf(r) == area)
            .Select(r => Choice(r.Name, r.Name, r.Check, r.Description, RunOverview.Show(defaults.GetValueOrDefault(r.Code) ?? RunCheckRules.Default(r)), r.Threshold is not null))
            .ToList())).ToList();
        areas.Add(new RunCheckArea(RunReconciliations.CheckName,
        [
            Choice(RunDefinitions.AllReconciliations, "Wszystkie aktywne reguły uzgodnień", null, "Każda aktywna reguła uzgodnień typu programu (okno Kontrole → Reguły uzgodnień).", "poziom reguły", false),
            .. reconciliations.Distinct(StringComparer.OrdinalIgnoreCase).Order().Select(n => Choice(RunDefinitions.ReconciliationPrefix + n, n, null,
                "Reguła uzgodnień po nazwie – poziom i tolerancja z reguły, chyba że ustawione tutaj.", "poziom reguły", true)),
        ]));
        if (used.Count > 0)
            areas.Add(new RunCheckArea("Inne", [.. used.Keys.Select(e => Choice(e, e, null, "Kontrola spoza katalogu (np. usunięta reguła) – odznacz, aby usunąć.", "", false))]));
        return areas;
    }
}
