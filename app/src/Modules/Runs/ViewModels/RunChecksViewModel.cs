using System.Windows.Input;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>Reguła katalogu w oknie Kontrole: ustawienie domyślne dla wszystkich programów (aktywna, poziom, próg) i opis.</summary>
public sealed class RunCheckDefaultViewModel : ObservableObject
{
    private bool _active;
    private string _level;
    private string _threshold;

    public RunCheckDefaultViewModel(RunCheckRule rule, RunCheckSetting setting)
    {
        Rule = rule;
        _active = setting.Active;
        _level = Name(setting.Level);
        _threshold = setting.Threshold is { } t ? PolishNumber.ToDisplay(t) : "";
    }

    public RunCheckRule Rule { get; }

    public IReadOnlyList<string> Levels => RunCheckRules.Levels;

    public bool HasThreshold => Rule.Threshold is not null;

    /// <summary>Ustawienie katalogu (bez wiersza w słowniku), np. „WARNING, próg 1,00”.</summary>
    public string CatalogText => RunOverview.Show(RunCheckRules.Default(Rule));

    public bool Active { get => _active; set => SetProperty(ref _active, value); }

    public string Level { get => _level; set => SetProperty(ref _level, value ?? Name(Rule.Level)); }

    public string Threshold { get => _threshold; set => SetProperty(ref _threshold, value ?? ""); }

    public bool InvalidThreshold => HasThreshold && (Threshold.Trim().Length == 0 || !PolishNumber.TryParse(Threshold, out var t) || t < 0);

    public RunCheckSetting ToSetting() => new(Active, RunCheckRules.LevelOf(Level) ?? Rule.Level,
        HasThreshold && PolishNumber.TryParse(Threshold, out var t) ? t : Rule.Threshold);

    public void Reset()
    {
        Active = true;
        Level = Name(Rule.Level);
        Threshold = Rule.Threshold is { } t ? PolishNumber.ToDisplay(t) : "";
    }

    private static string Name(CheckLevel level) => level switch { CheckLevel.Error => "ERROR", CheckLevel.Warning => "WARNING", _ => "INFO" };
}

/// <summary>Grupa reguł wbudowanych w oknie Kontrole (Walidacja EV: Spójność ze słownikami, Rodzaje kosztu).</summary>
public sealed record RunCheckGroup(string Name, IReadOnlyList<RunCheckDefaultViewModel> Rules);

/// <summary>
/// Okno Kontrole (ekran Przebiegi): reguły walidacji ACTUALS z formułami (edytor, test na programie); reguły wbudowane „Walidacja EV”
/// w grupach z ustawieniem domyślnym dla wszystkich programów (słownik „Kontrole przebiegu”: „Wszystkie”, „Każdy”) – szablon może je
/// zmienić na kafelku etapu; reguły uzgodnień i ustawienia dla typów programów (tabela słownika), miary do formuł uzgodnień.
/// </summary>
public sealed class RunChecksViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly RunTemplateService _service;
    private IReadOnlyList<RunCheckGroup> _groups = [];
    private string _status = "";

    public RunChecksViewModel(RunTemplateService service, RunDictionaryEditorViewModel editor, RunValidationEditorViewModel validations)
    {
        _service = service;
        Editor = editor;
        Validations = validations;
        Editor.Saved += () => Saved?.Invoke();
        Validations.Saved += () => Saved?.Invoke();
        Save = new AsyncRelayCommand(DoSave, () => !Busy.IsBusy);
        Reset = new RelayCommand(p => (p as RunCheckDefaultViewModel)?.Reset());
        _ = Load();
    }

    public BusyState Busy { get; } = new();

    public RunDictionaryEditorViewModel Editor { get; }

    /// <summary>Reguły walidacji ACTUALS – formuły (edytor, test na programie).</summary>
    public RunValidationEditorViewModel Validations { get; }

    public IReadOnlyList<RunCheckGroup> Groups { get => _groups; private set => SetProperty(ref _groups, value); }

    public IReadOnlyList<RunMeasure> Measures => RunMeasures.All;

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public ICommand Save { get; }

    /// <summary>Ustawienie reguły jak w katalogu.</summary>
    public ICommand Reset { get; }

    /// <summary>Zapis ustawień albo słownika – ekran Przebiegi odświeża szablony.</summary>
    public event Action? Saved;

    private async Task Load()
    {
        try
        {
            var defaults = await Busy.Run("Wczytywanie ustawień kontroli…", _service.CheckDefaults);
            Groups = RunCheckRules.Areas.Select(area => new RunCheckGroup(area, RunCheckRules.All.Where(r => RunCheckRules.AreaOf(r) == area)
                .Select(r => new RunCheckDefaultViewModel(r, defaults.GetValueOrDefault(r.Code) ?? RunCheckRules.Default(r))).ToList())).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Ustawienia kontroli");
            Status = $"Nie udało się wczytać ustawień: {ex.Message}";
        }
    }

    private async Task DoSave()
    {
        var invalid = Groups.SelectMany(g => g.Rules).Where(r => r.InvalidThreshold).Select(r => r.Rule.Name).ToList();
        if (invalid.Count > 0)
        {
            Status = $"Próg – liczba nieujemna: {string.Join(", ", invalid)}.";
            return;
        }
        var settings = Groups.SelectMany(g => g.Rules).ToDictionary(r => r.Rule.Code, r => r.ToSetting());
        try
        {
            var outcome = await Busy.Run("Zapisywanie ustawień kontroli…", () => _service.SaveCheckDefaults(settings));
            Status = outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges
                ? outcome.Status == SaveStatus.Saved ? outcome.Message : "Bez zmian."
                : $"{outcome.Message} {string.Join("; ", outcome.Issues.Where(i => i.Level == CheckLevel.Error).Take(3).Select(i => i.Message))}";
            if (outcome.Status == SaveStatus.Saved)
                Saved?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Zapis ustawień kontroli");
            Status = $"Zapis nieudany: {ex.Message}";
        }
    }
}
