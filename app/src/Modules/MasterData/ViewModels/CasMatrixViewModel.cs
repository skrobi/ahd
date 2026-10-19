using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>Wynik edytora formuły: klocki (Zatwierdź), null w Tokens z Remove – pula nienaliczana, null – anulowano.</summary>
public sealed record FormulaEdit(IReadOnlyList<CasFormula.Token>? Tokens, bool Remove);

/// <summary>
/// Okno „Formuły narzutów CAS” (docs/slowniki.md, rozdz. 6.1): macierz – wiersze to grupy narzutowe, kolumny to pule
/// (nazwy ze słowników – zmiana nazwy widoczna od razu). Zaznaczone pole = pula naliczana w grupie; kliknięcie pola
/// otwiera edytor formuły z klocków (nowa pula – formuła koszt × wskaźnik). Zmiany są zbierane i zapisywane razem
/// („Zapisz”) w słowniku „Formuły narzutów CAS” – historia wersji, walidacja i wykrycie równoczesnej zmiany.
/// </summary>
public sealed class CasMatrixViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "master-data");

    private readonly DictionaryService _service;
    private readonly DictionarySpec _spec = GlobalDictionaries.Get(GlobalDictionaries.CasBurdenFormulas);
    private readonly Dictionary<(long Group, long Pool), CasBurden.Change> _changes = [];
    private string _status = "";

    public CasMatrixViewModel(DictionaryService service)
    {
        _service = service;
        Save = new AsyncRelayCommand(DoSave, () => _changes.Count > 0 && !Busy.IsBusy);
        Discard = new AsyncRelayCommand(() => Load("Zmiany odrzucone."), () => _changes.Count > 0 && !Busy.IsBusy);
        Open = new RelayCommand(p => { if (p is CasMatrixCell cell) OpenCell(cell); }, _ => !Busy.IsBusy);
    }

    public BusyState Busy { get; } = new();

    public IReadOnlyList<CasBurden.Pool> Pools { get; private set; } = [];

    public ObservableCollection<CasMatrixRow> Rows { get; } = [];

    public bool Saved { get; private set; }

    public bool HasChanges => _changes.Count > 0;

    public ICommand Save { get; }
    public ICommand Discard { get; }
    public ICommand Open { get; }

    /// <summary>Okno edytora formuły (ustawia widok): grupa, pula, pule, bieżące klocki (null – nowa) → wynik albo null.</summary>
    public Func<CasFormulaBuilderViewModel, FormulaEdit?> EditFormula { get; set; } = _ => null;

    /// <summary>Po wczytaniu – widok buduje kolumny pul.</summary>
    public event Action? Loaded;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public async Task Load(string status = "")
    {
        try
        {
            var (pools, groups) = await Busy.Run("Wczytywanie kategorii, grup i formuł narzutów CAS…", () =>
                (CasBurden.PoolsFrom(_service.Load(GlobalDictionaries.Get(GlobalDictionaries.CasPools))),
                 CasBurden.GroupsFrom(_service.Load(GlobalDictionaries.Get(GlobalDictionaries.CasBurdenGroups)), _service.Load(_spec))));
            _changes.Clear();
            Pools = pools;
            Rows.Clear();
            foreach (var group in groups.OrderBy(g => g.Name, StringComparer.CurrentCulture))
                Rows.Add(new CasMatrixRow(group, pools));
            Loaded?.Invoke();
            Status = status.Length > 0 ? status
                : groups.Count == 0 ? "Brak grup – dodaj grupy w słowniku „Grupy narzutowe CAS”."
                : pools.Count == 0 ? "Brak kategorii – dodaj kategorie w słowniku „Kategorie narzutu CAS”." : "";
            OnPropertyChanged(nameof(HasChanges));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Formuły narzutów CAS – odczyt");
            Status = $"Nie udało się wczytać słowników: {ex.Message}";
        }
    }

    private void OpenCell(CasMatrixCell cell)
    {
        var builder = new CasFormulaBuilderViewModel(cell.Row.Name, cell.Pool, Pools, cell.Tokens);
        if (EditFormula(builder) is not { } edit)
            return;
        var tokens = edit.Remove ? null : edit.Tokens;
        if (!edit.Remove && tokens is null)
            return;
        cell.Set(tokens);
        _changes[(cell.Row.Id, cell.Pool.Id)] = new CasBurden.Change(cell.Row.Id, cell.Pool.Id, tokens);
        cell.Row.CheckCycles();
        OnPropertyChanged(nameof(HasChanges));
        Status = "Niezapisane zmiany formuł – „Zapisz”, aby zapisać (zmienione pola – pogrubione).";
    }

    private async Task DoSave()
    {
        if (Rows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Problem.Length > 0) is { } bad)
        {
            Status = $"Grupa {bad.Row.Name}, kategoria {bad.Pool.Name}: {bad.Problem} – popraw formułę przed zapisem.";
            return;
        }
        try
        {
            var outcome = await Busy.Run("Zapisywanie formuł narzutów CAS…", () =>
            {
                var (working, removed) = CasBurden.Apply(_service.Load(_spec), _changes.Values);
                return _service.Save(_spec, working, removed, confirmWarnings: true);
            });
            if (outcome.Status is SaveStatus.Saved or SaveStatus.NoChanges)
            {
                Saved |= outcome.Status == SaveStatus.Saved;
                await Load(outcome.Message);
                return;
            }
            var first = outcome.Issues.Where(i => i.Level == CheckLevel.Error).Take(3).Select(i => i.Message);
            Status = $"{outcome.Message} {string.Join("; ", first)}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Formuły narzutów CAS – zapis");
            Status = $"Zapis nieudany: {ex.Message}";
        }
    }
}

/// <summary>Wiersz macierzy: grupa narzutowa i pole na każdą pulę.</summary>
public sealed class CasMatrixRow
{
    public CasMatrixRow(CasBurden.Group group, IReadOnlyList<CasBurden.Pool> pools)
    {
        Id = group.Id;
        Name = group.Name;
        Description = group.Description ?? "";
        Names = pools.ToDictionary(p => p.Id, p => p.Name);
        Cells = pools.Select(p => new CasMatrixCell(this, p, group.Formulas.GetValueOrDefault(p.Id))).ToList();
        CheckCycles();
    }

    public long Id { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<CasMatrixCell> Cells { get; }
    internal IReadOnlyDictionary<long, string> Names { get; }

    /// <summary>Zapętlenia formuł grupy po zmianie – uwaga w polach pul w zapętleniu.</summary>
    public void CheckCycles()
    {
        var formulas = Cells.Where(c => c.Tokens is not null).ToDictionary(c => c.Pool.Id, c => c.Tokens!);
        var cycles = CasBurden.Cycles(formulas);
        foreach (var cell in Cells)
            cell.Problem = cell.Tokens is null ? ""
                : cycles.Contains(cell.Pool.Id) ? "narzut zależy od samego siebie (zapętlenie formuł)"
                : CasFormula.Error(cell.Tokens, Names.Keys.ToHashSet()) ?? "";
    }
}

/// <summary>Pole macierzy: pula naliczana w grupie (formuła), podpowiedź z formułą do czytania, zmiana, problem.</summary>
public sealed class CasMatrixCell(CasMatrixRow row, CasBurden.Pool pool, IReadOnlyList<CasFormula.Token>? tokens) : ObservableObject
{
    private IReadOnlyList<CasFormula.Token>? _tokens = tokens;
    private bool _changed;
    private string _problem = "";

    public CasMatrixRow Row { get; } = row;
    public CasBurden.Pool Pool { get; } = pool;

    public IReadOnlyList<CasFormula.Token>? Tokens => _tokens;

    public bool IsApplied => _tokens is not null;

    public bool IsChanged => _changed;

    public bool HasProblem => _problem.Length > 0;

    public string Problem
    {
        get => _problem;
        set
        {
            if (!SetProperty(ref _problem, value))
                return;
            OnPropertyChanged(nameof(Tip));
            OnPropertyChanged(nameof(HasProblem));
        }
    }

    public string Tip => _tokens is null
        ? $"{Pool.Name} – nienaliczana w grupie {Row.Name}. Kliknij, aby zbudować formułę."
        : $"{Pool.Name} = {CasFormula.Display(_tokens, id => Row.Names.TryGetValue(id, out var n) ? n : "(pula usunięta)")}"
          + (_problem.Length > 0 ? $"\n⚠ {_problem}" : "") + "\nKliknij, aby zmienić.";

    public void Set(IReadOnlyList<CasFormula.Token>? tokens)
    {
        _tokens = tokens;
        _changed = true;
        OnPropertyChanged(nameof(Tokens));
        OnPropertyChanged(nameof(IsApplied));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(Tip));
    }
}
