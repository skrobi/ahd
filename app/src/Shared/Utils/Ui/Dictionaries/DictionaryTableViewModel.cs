using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Shared.Utils.Ui.Dictionaries;

/// <summary>
/// Tabela słownika do edycji w komórkach (ekran Słowniki, zakładka „Słowniki projektu”; widok – DictionaryGrid):
/// wiersze z opisu słownika, filtr, dodawanie i usuwanie, edycja jak w Excelu (wklejanie bloku, czyszczenie
/// i wypełnianie w dół zaznaczonych komórek, Ctrl+Z), listy wyboru wartości słowników powiązanych (DictColumn.Lookup),
/// stan do zapisu (DictionaryService.Save) i wynik walidacji. Zapis i odczyt wykonuje ekran nadrzędny – przed odczytem
/// stanu wywołuje CommitEdits (komórka w trakcie edycji trafia do wiersza dopiero przy zatwierdzeniu).
/// </summary>
public sealed class DictionaryTableViewModel : ObservableObject
{
    private const int UndoLimit = 100;

    private readonly List<DictRow> _removed = [];
    private readonly List<List<Action>> _undo = [];
    private List<Action>? _batch;
    private bool _undoing;
    private DictionarySpec? _spec;
    private DictRowViewModel? _selectedRow;
    private IReadOnlyList<DictRowViewModel> _selectedRows = [];
    private string _filter = "";
    private string _notice = "";
    private bool _needsConfirmation;
    private IReadOnlyDictionary<string, IReadOnlyList<LookupOption>> _lookups = new Dictionary<string, IReadOnlyList<LookupOption>>();

    public DictionaryTableViewModel()
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => _filter.Length == 0 || o is DictRowViewModel row && row.Matches(_filter);
        Undo = new RelayCommand(_ => DoUndo(), _ => _undo.Count > 0);
    }

    /// <summary>Widok przebudowuje kolumny tabeli po zmianie słownika.</summary>
    public event Action? ColumnsChanged;

    /// <summary>Widok zatwierdza (true) albo anuluje (false) komórkę w trakcie edycji.</summary>
    public event Action<bool>? EndEditRequested;

    public DictionarySpec? Spec => _spec;

    public ObservableCollection<DictRowViewModel> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public ObservableCollection<Issue> Issues { get; } = [];

    public bool HasIssues => Issues.Count > 0;

    public DictRowViewModel? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

    /// <summary>Wiersze zaznaczonych komórek (ustawia widok) – „Usuń wiersz” usuwa je wszystkie.</summary>
    public IReadOnlyList<DictRowViewModel> SelectedRows { get => _selectedRows; set => _selectedRows = value; }

    public string Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value.Trim()))
                return;
            CommitEdits();
            RowsView.Refresh();
        }
    }

    /// <summary>Wynik ostatniej operacji w tabeli (wklejenie, usunięcie, cofnięcie) – pod tabelą.</summary>
    public string Notice { get => _notice; private set => SetProperty(ref _notice, value); }

    public bool NeedsConfirmation { get => _needsConfirmation; private set => SetProperty(ref _needsConfirmation, value); }

    /// <summary>Zapis mimo ostrzeżeń (przycisk przy wyniku walidacji) – ustawia ekran nadrzędny.</summary>
    public ICommand? SaveWithWarnings { get; set; }

    /// <summary>Cofnięcie ostatniej zmiany tabeli (Ctrl+Z): wpis, wklejenie, wyczyszczenie, dodanie, usunięcie.</summary>
    public ICommand Undo { get; }

    public bool HasPendingChanges => _removed.Count > 0 || Rows.Any(r => r.IsNew || r.IsModified);

    /// <summary>Zatwierdza komórkę w trakcie edycji (przed zapisem, zmianą słownika, wczytaniem z pliku).</summary>
    public void CommitEdits() => EndEditRequested?.Invoke(true);

    /// <summary>Anuluje komórkę w trakcie edycji (przed odrzuceniem zmian).</summary>
    public void CancelEdits() => EndEditRequested?.Invoke(false);

    /// <summary>
    /// Nowa zawartość tabeli (niezapisane zmiany, historia Ctrl+Z i wynik walidacji przepadają); inherited – wiersz
    /// dziedziczony; lookups – wartości słowników powiązanych według kodu słownika (DictColumn.Lookup).
    /// </summary>
    public void Load(DictionarySpec? spec, IEnumerable<(DictRow Row, bool Inherited)> rows,
        IReadOnlyDictionary<string, IReadOnlyList<LookupOption>>? lookups = null)
    {
        CancelEdits();
        var columns = !ReferenceEquals(spec, _spec);
        _spec = spec;
        if (lookups is not null)
            _lookups = lookups;
        Rows.Clear();
        _removed.Clear();
        _undo.Clear();
        SelectedRow = null;
        _selectedRows = [];
        Notice = "";
        SetIssues([]);
        if (spec is not null)
        {
            foreach (var (row, inherited) in rows)
            {
                var view = new DictRowViewModel(row.RowId, row.Version, spec.Columns.Select(c => (string?)ValueFormat.Display(c, row[c.Name])).ToArray(), inherited,
                    spec, Options);
                view.Edited = OnEdited;
                Rows.Add(view);
            }
        }
        OnPropertyChanged(nameof(Spec));
        if (columns)
            ColumnsChanged?.Invoke();
    }

    public void Load(DictionarySpec? spec, IEnumerable<DictRow> rows) => Load(spec, rows.Select(r => (r, false)));

    /// <summary>Wartości do wyboru w kolumnie powiązanej z innym słownikiem; null – kolumna bez powiązania.</summary>
    public IReadOnlyList<LookupOption>? Options(DictColumn column) =>
        column.Lookup is { } code && _lookups.TryGetValue(code, out var options) ? options : null;

    public DictRowViewModel AddRow()
    {
        DictRowViewModel row = null!;
        Batch(() => row = NewRow());
        SelectedRow = row;
        return row;
    }

    /// <summary>
    /// Usuwa z tabeli wiersze zaznaczonych komórek (albo bieżący wiersz). Wiersze dziedziczone (pozycje słownika
    /// globalnego) zostają. Zwraca liczbę usuniętych i pominiętych dziedziczonych.
    /// </summary>
    public (int Removed, int Inherited) RemoveSelected()
    {
        var rows = (_selectedRows.Count > 0 ? _selectedRows : _selectedRow is null ? [] : [_selectedRow]).Distinct().ToList();
        var removable = rows.Where(r => !r.IsInherited).ToList();
        Batch(() =>
        {
            foreach (var row in removable)
            {
                var position = Rows.IndexOf(row);
                if (position < 0)
                    continue;
                var persisted = row.IsNew ? null : row.ToDictRow(_spec!);
                if (persisted is not null)
                    _removed.Add(persisted);
                Rows.RemoveAt(position);
                Record(() =>
                {
                    Rows.Insert(Math.Min(position, Rows.Count), row);
                    if (persisted is not null)
                        _removed.Remove(persisted);
                });
            }
        });
        _selectedRows = [];
        var inherited = rows.Count - removable.Count;
        Notice = (removable.Count == 0 ? "" : $"Usunięto z tabeli wierszy: {removable.Count} – zapisz, aby zamknąć ich obowiązywanie (Ctrl+Z cofa).")
            + (inherited == 0 ? "" : $" Pominięto pozycje słownika globalnego: {inherited} – w projekcie można je zmienić (zapis tworzy zmianę projektu), usuwa się je na ekranie Słowniki.");
        return (removable.Count, inherited);
    }

    /// <summary>Wpis do komórek (Delete – null, wypełnianie w dół); w kolumnie powiązanej opis zamieniany na wartość.</summary>
    public void SetCells(IEnumerable<(DictRowViewModel Row, int Column, string? Text)> cells) =>
        Batch(() =>
        {
            foreach (var (row, column, text) in cells)
                row[column] = DictionaryCells.Resolve(text, Options(_spec!.Columns[column]));
        });

    /// <summary>
    /// Wklejenie bloku komórek (Excel) od wiersza start (null – na końcu) i kolumny column w kolejności widocznych
    /// wierszy; brakujące wiersze są dopisywane. Jak w Excelu: jedna skopiowana komórka wypełnia wszystkie zaznaczone
    /// (selection); pierwszy wiersz bloku równy nagłówkom kolumn jest pomijany; kolumny poza słownikiem – pominięte
    /// (w Notice). Zwraca liczbę wklejonych wierszy.
    /// </summary>
    public int Paste(DictRowViewModel? start, int column, IReadOnlyList<string[]> block,
        IReadOnlyList<(DictRowViewModel Row, int Column)>? selection = null)
    {
        if (_spec is not { } spec || block.Count == 0)
            return 0;
        if (block is [[var single]] && selection is { Count: > 1 })
        {
            SetCells(selection.Select(c => (c.Row, c.Column, (string?)single)));
            Notice = $"Wklejono wartość do {selection.Count} zaznaczonych komórek.";
            return selection.Select(c => c.Row).Distinct().Count();
        }
        var header = DictionaryCells.IsHeaderRow(spec, column, block[0]);
        var data = header ? block.Skip(1).ToList() : block.ToList();
        var visible = RowsView.Cast<DictRowViewModel>().ToList();
        var at = start is null ? visible.Count : Math.Max(0, visible.IndexOf(start));
        var added = 0;
        var skipped = data.Count == 0 ? 0 : data.Max(r => r.Length) - Math.Max(0, spec.Columns.Count - column);
        Batch(() =>
        {
            for (var r = 0; r < data.Count; r++)
            {
                DictRowViewModel row;
                if (at + r < visible.Count)
                    row = visible[at + r];
                else
                {
                    row = NewRow();
                    added++;
                }
                foreach (var (text, c) in data[r].Select((text, c) => (text, column + c)).Where(x => x.Item2 < spec.Columns.Count))
                    row[c] = DictionaryCells.Resolve(text, Options(spec.Columns[c]));
            }
        });
        Notice = $"Wklejono wierszy: {data.Count}{(added > 0 ? $" (nowe: {added})" : "")}"
            + (header ? " · pominięto wiersz nagłówków" : "")
            + (skipped > 0 ? $" · pominięto kolumn poza słownikiem: {skipped}" : "")
            + " · Ctrl+Z cofa.";
        return data.Count;
    }

    /// <summary>Wynik walidacji zapisu; NeedsConfirmation – ostrzeżenia do potwierdzenia.</summary>
    public void SetIssues(IReadOnlyList<Issue> issues, bool needsConfirmation = false)
    {
        Issues.Clear();
        foreach (var issue in issues)
            Issues.Add(issue);
        NeedsConfirmation = needsConfirmation;
        OnPropertyChanged(nameof(HasIssues));
    }

    /// <summary>Stan do zapisu: wiersze słownika po edycji (bez niezmienionych dziedziczonych) i usunięte.</summary>
    public (List<DictRow> Working, List<DictRow> Removed) State() =>
        (Rows.Where(r => !r.IsInherited || r.IsModified).Select(r => r.ToDictRow(_spec!)).ToList(), _removed.ToList());

    private DictRowViewModel NewRow()
    {
        var row = new DictRowViewModel(null, null, new string?[_spec!.Columns.Count], spec: _spec, options: Options) { Edited = OnEdited };
        Rows.Add(row);
        Record(() => Rows.Remove(row));
        return row;
    }

    private void OnEdited(DictRowViewModel row, int column, string? old) => Record(() => row[column] = old);

    /// <summary>Operacje w action tworzą jeden krok Ctrl+Z.</summary>
    private void Batch(Action action)
    {
        if (_batch is not null)
        {
            action();
            return;
        }
        _batch = [];
        try
        {
            action();
        }
        finally
        {
            var steps = _batch;
            _batch = null;
            if (steps.Count > 0)
                Push(steps);
        }
    }

    private void Record(Action revert)
    {
        if (_undoing)
            return;
        if (_batch is not null)
            _batch.Add(revert);
        else
            Push([revert]);
    }

    private void Push(List<Action> steps)
    {
        _undo.Add(steps);
        if (_undo.Count > UndoLimit)
            _undo.RemoveAt(0);
    }

    private void DoUndo()
    {
        CancelEdits();
        if (_undo.Count == 0)
            return;
        var steps = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _undoing = true;
        try
        {
            for (var i = steps.Count - 1; i >= 0; i--)
                steps[i]();
        }
        finally
        {
            _undoing = false;
        }
        Notice = $"Cofnięto ostatnią zmianę{(_undo.Count > 0 ? $" (można cofnąć jeszcze {_undo.Count})" : "")}.";
    }
}
