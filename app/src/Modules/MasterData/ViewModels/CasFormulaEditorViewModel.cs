using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>
/// Edytor formuł grupy narzutowej CAS (docs/slowniki.md, rozdz. 6.1): macierz – wiersze to pule („Pule narzutu CAS”),
/// kolumny to składniki podstawy (Base cost elements i pule). „Naliczana” – pula ma formułę w grupie; zaznaczone
/// składniki – podstawa: narzut = (suma składników) × wskaźnik puli. Zapis – słownik „Formuły narzutów CAS” (historia,
/// walidacja, wykrycie równoczesnej zmiany jak w tabeli słownika); zapętlenie formuł blokuje zapis.
/// </summary>
public sealed class CasFormulaEditorViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "master-data");

    private readonly DictionaryService _service;
    private readonly DictionarySpec _formulaSpec = GlobalDictionaries.Get(GlobalDictionaries.CasBurdenFormulas);
    private IReadOnlyList<DictRow> _formulaRows = [];
    private IReadOnlyList<CasBurden.Group> _groups = [];
    private string? _selectedGroup;
    private string _status = "";
    private bool _isDirty;

    public CasFormulaEditorViewModel(DictionaryService service, string? group)
    {
        _service = service;
        Save = new AsyncRelayCommand(DoSave, () => _selectedGroup is not null && _isDirty && !Busy.IsBusy);
        Discard = new RelayCommand(_ => ShowGroup("Zmiany odrzucone."), _ => _isDirty && !Busy.IsBusy);
        _selectedGroup = group;
    }

    public BusyState Busy { get; } = new();

    /// <summary>Kolumny macierzy: Base cost elements i pule w kolejności słownika pul.</summary>
    public IReadOnlyList<string> Components { get; private set; } = [];

    public ObservableCollection<string> Groups { get; } = [];

    public ObservableCollection<CasFormulaRowViewModel> Rows { get; } = [];

    /// <summary>Zapisano co najmniej raz – ekran Słowniki odświeża tabelę po zamknięciu okna.</summary>
    public bool Saved { get; private set; }

    public ICommand Save { get; }
    public ICommand Discard { get; }

    /// <summary>Po wczytaniu słowników – okno buduje kolumny macierzy (Components).</summary>
    public event Action? Loaded;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Description => _groups.FirstOrDefault(g => Same(g.Name, _selectedGroup))?.Description ?? "";

    public string? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (value is null || Same(value, _selectedGroup))
                return;
            if (_isDirty)
            {
                Status = "Masz niezapisane zmiany – zapisz albo odrzuć je przed zmianą grupy.";
                OnPropertyChanged();
                return;
            }
            _selectedGroup = value;
            OnPropertyChanged();
            ShowGroup("");
        }
    }

    public async Task Load()
    {
        try
        {
            var (pools, groups, formulas) = await Busy.Run("Wczytywanie pul, grup i formuł narzutów CAS…", () =>
                (CasBurden.PoolsFrom(_service.Load(GlobalDictionaries.Get(GlobalDictionaries.CasPools))),
                 _service.Load(GlobalDictionaries.Get(GlobalDictionaries.CasBurdenGroups)),
                 _service.Load(_formulaSpec)));
            _formulaRows = formulas;
            _groups = CasBurden.GroupsFrom(groups, formulas);
            Components = [CasBurden.Base, .. pools.Select(p => p.Name)];
            Groups.Clear();
            foreach (var name in _groups.Select(g => g.Name))
                Groups.Add(name);
            _selectedGroup = Groups.FirstOrDefault(g => Same(g, _selectedGroup)) ?? Groups.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedGroup));
            Loaded?.Invoke();
            ShowGroup(Groups.Count == 0 ? "Brak grup – dodaj grupę w słowniku „Grupy narzutowe CAS”." : "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Edytor formuł narzutów CAS – odczyt");
            Status = $"Nie udało się wczytać słowników: {ex.Message}";
        }
    }

    /// <summary>Macierz wybranej grupy ze stanu słownika (pule spoza słownika pul z formułą – na końcu).</summary>
    private void ShowGroup(string status)
    {
        Rows.Clear();
        var formulas = _groups.FirstOrDefault(g => Same(g.Name, _selectedGroup))?.Formulas ?? new Dictionary<string, IReadOnlyList<string>>();
        var pools = Components.Skip(1).ToList();
        pools.AddRange(formulas.Keys.Where(k => !pools.Contains(k, StringComparer.OrdinalIgnoreCase)));
        foreach (var pool in pools)
        {
            var row = new CasFormulaRowViewModel(pool, Components, formulas.GetValueOrDefault(pool), Components.Contains(pool, StringComparer.OrdinalIgnoreCase));
            row.Changed += OnChanged;
            Rows.Add(row);
        }
        _isDirty = false;
        Check();
        OnPropertyChanged(nameof(Description));
        Status = status;
    }

    private void OnChanged()
    {
        _isDirty = true;
        Check();
        Status = "Niezapisane zmiany – „Zapisz”, aby zapisać formuły grupy.";
    }

    /// <summary>Zapętlenia i pule naliczane bez składnika – oznaczone w wierszach.</summary>
    private Dictionary<string, IReadOnlyList<string>> Check()
    {
        var formulas = Rows.Where(r => r.Applied).ToDictionary(r => r.Pool, r => r.Basis(), StringComparer.OrdinalIgnoreCase);
        var cycles = CasBurden.Cycles(formulas);
        foreach (var row in Rows)
            row.Problem = !row.Applied ? ""
                : row.Basis().Count == 0 ? "wskaż składnik podstawy"
                : cycles.Contains(row.Pool) ? "zapętlenie – pula zależy od samej siebie"
                : !row.Known ? "pula spoza słownika pul" : "";
        return formulas;
    }

    private async Task DoSave()
    {
        var group = _selectedGroup!;
        var formulas = Check();
        if (Rows.Any(r => r.Applied && r.Problem.Length > 0 && r.Known))
        {
            Status = "Formuły mają błędy (kolumna „Uwagi”) – popraw je przed zapisem.";
            return;
        }
        try
        {
            var outcome = await Busy.Run($"Zapisywanie formuł grupy „{group}”…", () =>
            {
                var (working, removed) = CasBurden.ReplaceGroup(_service.Load(_formulaSpec), group, formulas);
                return _service.Save(_formulaSpec, working, removed, confirmWarnings: true);
            });
            if (outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges)
            {
                Saved |= outcome.Status == SaveStatus.Saved;
                _isDirty = false;
                await Load();
                Status = outcome.Message;
            }
            else
            {
                var first = outcome.Issues.Where(i => i.Level == CheckLevel.Error).Take(3).Select(i => i.Element is null ? i.Message : $"{i.Message} ({i.Element})");
                Status = $"{outcome.Message} {string.Join("; ", first)}";
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Edytor formuł narzutów CAS – zapis {Group}", group);
            Status = $"Zapis nieudany: {ex.Message}";
        }
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Wiersz macierzy: pula, „Naliczana”, składniki podstawy (komórki), podgląd formuły i uwagi.</summary>
public sealed class CasFormulaRowViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _components;
    private readonly List<string> _order;
    private bool _applied;
    private string _problem = "";

    public CasFormulaRowViewModel(string pool, IReadOnlyList<string> components, IReadOnlyList<string>? basis, bool known)
    {
        Pool = pool;
        Known = known;
        _components = components;
        _applied = basis is not null;
        _order = basis?.ToList() ?? [];
        Cells = components.Select((c, i) => new CasFormulaCellViewModel(this, c,
            basis?.Contains(c, StringComparer.OrdinalIgnoreCase) == true, !string.Equals(c, pool, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public string Pool { get; }

    /// <summary>Pula jest w słowniku „Pule narzutu CAS” (inaczej – formuła z danych, tylko do usunięcia).</summary>
    public bool Known { get; }

    public IReadOnlyList<CasFormulaCellViewModel> Cells { get; }

    public event Action? Changed;

    public bool Applied
    {
        get => _applied;
        set
        {
            if (!SetProperty(ref _applied, value))
                return;
            if (value && Basis().Count == 0 && Cells.Count > 0)
                Cells[0].IsOn = true;   // nowa pula – domyślnie od kosztu bazowego
            foreach (var cell in Cells)
                cell.Refresh();
            OnPropertyChanged(nameof(Preview));
            Changed?.Invoke();
        }
    }

    public string Problem
    {
        get => _problem;
        set => SetProperty(ref _problem, value);
    }

    /// <summary>Formuła jak w raporcie: (składniki) × wskaźnik puli.</summary>
    public string Preview
    {
        get
        {
            if (!_applied)
                return "";
            var basis = Basis();
            var sum = basis.Count == 1 ? basis[0] : $"({string.Join(" + ", basis)})";
            return basis.Count == 0 ? "" : $"{sum} × wskaźnik {Pool}";
        }
    }

    /// <summary>
    /// Składniki podstawy: kolejność zapisana w słowniku dla składników pozostawionych, nowe – w kolejności kolumn;
    /// składniki spoza macierzy (pula usunięta ze słownika pul) zostają.
    /// </summary>
    public IReadOnlyList<string> Basis()
    {
        var on = Cells.Where(c => c.IsOn).Select(c => c.Component).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = _order.Where(c => on.Contains(c) || !_components.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        kept.AddRange(Cells.Where(c => c.IsOn && !kept.Contains(c.Component, StringComparer.OrdinalIgnoreCase)).Select(c => c.Component));
        return kept;
    }

    internal void CellChanged()
    {
        OnPropertyChanged(nameof(Preview));
        Changed?.Invoke();
    }
}

/// <summary>Komórka macierzy: składnik w podstawie puli; nieaktywna, gdy pula nienaliczana albo składnik to ta sama pula.</summary>
public sealed class CasFormulaCellViewModel(CasFormulaRowViewModel row, string component, bool isOn, bool allowed) : ObservableObject
{
    private bool _isOn = isOn;

    public string Component { get; } = component;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetProperty(ref _isOn, value))
                row.CellChanged();
        }
    }

    public bool IsEnabled => row.Applied && allowed;

    internal void Refresh() => OnPropertyChanged(nameof(IsEnabled));
}
