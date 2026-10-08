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
/// wiersze z opisu słownika, filtr, dodawanie i usuwanie, stan do zapisu (DictionaryService.Save) i wynik walidacji.
/// Zapis i odczyt wykonuje ekran nadrzędny.
/// </summary>
public sealed class DictionaryTableViewModel : ObservableObject
{
    private readonly List<DictRow> _removed = [];
    private DictionarySpec? _spec;
    private DictRowViewModel? _selectedRow;
    private string _filter = "";
    private bool _needsConfirmation;

    public DictionaryTableViewModel()
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => _filter.Length == 0 || o is DictRowViewModel row && row.Matches(_filter);
    }

    /// <summary>Widok przebudowuje kolumny tabeli po zmianie słownika.</summary>
    public event Action? ColumnsChanged;

    public DictionarySpec? Spec => _spec;

    public ObservableCollection<DictRowViewModel> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public ObservableCollection<Issue> Issues { get; } = [];

    public bool HasIssues => Issues.Count > 0;

    public DictRowViewModel? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

    public string Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value.Trim()))
                return;
            if (RowsView is IEditableCollectionView editable)
            {
                if (editable.IsEditingItem)
                    editable.CommitEdit();
                if (editable.IsAddingNew)
                    editable.CommitNew();
            }
            RowsView.Refresh();
        }
    }

    public bool NeedsConfirmation { get => _needsConfirmation; private set => SetProperty(ref _needsConfirmation, value); }

    /// <summary>Zapis mimo ostrzeżeń (przycisk przy wyniku walidacji) – ustawia ekran nadrzędny.</summary>
    public ICommand? SaveWithWarnings { get; set; }

    public bool HasPendingChanges => _removed.Count > 0 || Rows.Any(r => r.IsNew || r.IsModified);

    /// <summary>Nowa zawartość tabeli (niezapisane zmiany i wynik walidacji przepadają); inherited – wiersz dziedziczony.</summary>
    public void Load(DictionarySpec? spec, IEnumerable<(DictRow Row, bool Inherited)> rows)
    {
        var columns = !ReferenceEquals(spec, _spec);
        _spec = spec;
        Rows.Clear();
        _removed.Clear();
        SelectedRow = null;
        SetIssues([]);
        if (spec is not null)
        {
            foreach (var (row, inherited) in rows)
                Rows.Add(new DictRowViewModel(row.RowId, row.Version, spec.Columns.Select(c => (string?)ValueFormat.Display(c, row[c.Name])).ToArray(), inherited));
        }
        OnPropertyChanged(nameof(Spec));
        if (columns)
            ColumnsChanged?.Invoke();
    }

    public void Load(DictionarySpec? spec, IEnumerable<DictRow> rows) => Load(spec, rows.Select(r => (r, false)));

    public void AddRow()
    {
        var row = new DictRowViewModel(null, null, new string?[_spec!.Columns.Count]);
        Rows.Add(row);
        SelectedRow = row;
    }

    /// <summary>Usuwa zaznaczony wiersz z tabeli (wiersza dziedziczonego nie – false).</summary>
    public bool RemoveSelected()
    {
        if (_selectedRow is not { } row || row.IsInherited)
            return false;
        if (!row.IsNew)
            _removed.Add(row.ToDictRow(_spec!));
        Rows.Remove(row);
        return true;
    }

    /// <summary>Stan do zapisu: wiersze słownika po edycji (bez niezmienionych dziedziczonych) i usunięte.</summary>
    public (List<DictRow> Working, List<DictRow> Removed) State() =>
        (Rows.Where(r => !r.IsInherited || r.IsModified).Select(r => r.ToDictRow(_spec!)).ToList(), _removed.ToList());

    /// <summary>Wynik walidacji zapisu; NeedsConfirmation – ostrzeżenia do potwierdzenia.</summary>
    public void SetIssues(IReadOnlyList<Issue> issues, bool needsConfirmation = false)
    {
        Issues.Clear();
        foreach (var issue in issues)
            Issues.Add(issue);
        NeedsConfirmation = needsConfirmation;
        OnPropertyChanged(nameof(HasIssues));
    }
}
