using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Tabela struktury projektu (ekran Projekt, zakładka Struktura): drzewo spłaszczone do wierszy tabeli – rozwinięcie
/// wstawia wiersze potomków, zwinięcie je usuwa. Stan rozwinięcia, zaznaczenie i niezapisane zmiany wierszy trwają po
/// odświeżeniu danych (po identyfikatorze wiersza). Węzły nakładki są domyślnie rozwinięte, elementy P1S – zwinięte.
/// Edycja jak w Excelu: wklejenie bloku, wyczyszczenie i wypełnienie w dół zaznaczonych komórek (SetCells) – każdy
/// zmieniony wiersz trafia do zapisu (Commit, kolejka ekranu projektu). Ctrl+Z (Undo) cofa ostatnie takie operacje –
/// zapisuje w komórkach wartości sprzed nich (zmiany są już w bazie, więc cofnięcie to kolejny zapis).
/// </summary>
public sealed class StructureViewModel : ObservableObject
{
    private readonly HashSet<string> _expanded = [];
    private readonly HashSet<string> _known = [];
    private List<StructureRowViewModel> _all = [];
    private Dictionary<string, int> _index = [];
    private StructureRowViewModel? _selected;
    private StructureSummary _summary = ProjectStructure.Empty.Summary;
    private string _notice = "";
    private const int UndoLimit = 20;
    private readonly List<List<(string Id, string Column, string? Value)>> _undo = [];

    public StructureViewModel()
    {
        ExpandAll = new RelayCommand(_ => SetAll(true), _ => _all.Count > 0);
        CollapseAll = new RelayCommand(_ => SetAll(false), _ => _all.Count > 0);
        Undo = new RelayCommand(_ => DoUndo(), _ => _undo.Count > 0);
        RevertChanges = new RelayCommand(_ => { foreach (var row in _all.Where(r => r.Changes.Count > 0)) row.Revert(); OnPropertyChanged(nameof(HasChanges)); },
            _ => HasChanges);
    }

    /// <summary>Zapis zmian wiersza po jego zatwierdzeniu w tabeli (ustawia ekran projektu).</summary>
    public Func<StructureRowViewModel, Task>? Commit { get; set; }

    /// <summary>Czy wolno teraz edytować (np. nie trwa edycja Performance Objectives).</summary>
    public Func<bool> CanEditNow { get; set; } = () => true;

    /// <summary>Widok zatwierdza komórkę i wiersz w trakcie edycji (przed odświeżeniem, zwinięciem, wyjściem z ekranu).</summary>
    public event Action? EndEditRequested;

    /// <summary>Widok zapamiętuje bieżącą komórkę przed wymianą wierszy (Load) i przywraca ją po niej.</summary>
    public event Action? RowsReplacing;

    public event Action? RowsReplaced;

    public BulkObservableCollection<StructureRowViewModel> Rows { get; } = [];

    /// <summary>Lista wyboru CAM (USRID → imię i nazwisko).</summary>
    public BulkObservableCollection<LookupOption> Persons { get; } = [];

    public StructureRowViewModel? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public StructureSummary Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public string SummaryText => _all.Count == 0
        ? "brak struktury"
        : $"{_all.Count(r => r.IsObjective && !r.IsVirtual)} elementów CES · {Summary.Wps} WP · {Summary.Cams} CAM";

    public bool HasChanges => _all.Any(r => r.Changes.Count > 0);

    /// <summary>Wynik ostatniej operacji na komórkach (wklejenie, wyczyszczenie) – pod tabelą.</summary>
    public string Notice { get => _notice; private set => SetProperty(ref _notice, value); }

    public ICommand ExpandAll { get; }
    public ICommand CollapseAll { get; }
    public ICommand RevertChanges { get; }

    /// <summary>Cofnięcie ostatniego wklejenia, wyczyszczenia (Delete) albo wypełnienia w dół (Ctrl+D).</summary>
    public ICommand Undo { get; }

    /// <summary>Zatwierdza komórkę w trakcie edycji (wartość trafia do wiersza, wiersz – do zapisu).</summary>
    public void CommitEdits() => EndEditRequested?.Invoke();

    /// <summary>
    /// Nowe dane struktury – zachowuje rozwinięcie, zaznaczenie i niezapisane zmiany wierszy (np. po nieudanym zapisie);
    /// saved – wiersze zapisane w tej chwili (ich zmiany są już w danych).
    /// </summary>
    public void Load(ProjectStructure structure, IReadOnlyList<LookupOption> persons, IReadOnlySet<string>? saved = null)
    {
        CommitEdits();
        RowsReplacing?.Invoke();
        if (!Persons.SequenceEqual(persons))
            Persons.ReplaceAll(persons);
        var pending = _all.Where(r => r.Changes.Count > 0 && saved?.Contains(r.Id) != true)
            .ToDictionary(r => r.Id, r => r.Changes.ToDictionary(c => c.Key, c => c.Value));
        var selected = _selected?.Id;
        _all = structure.Rows.Select(r => new StructureRowViewModel(r)).ToList();
        _index = _all.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
        foreach (var row in _all)
        {
            if (_known.Add(row.Id) && row.IsObjective)
                _expanded.Add(row.Id);
            row.IsExpanded = _expanded.Contains(row.Id);
            if (pending.TryGetValue(row.Id, out var changes))
            {
                foreach (var (column, value) in changes)
                    row[column] = value;   // indeksator pomija komórki zablokowane i wartości równe danym
            }
        }
        Summary = structure.Summary;
        Rows.ReplaceAll(Visible(0, _all.Count));
        Selected = Rows.FirstOrDefault(r => r.Id == selected);
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasChanges));
        RowsReplaced?.Invoke();
    }

    public void Toggle(StructureRowViewModel row)
    {
        if (!row.HasChildren)
            return;
        CommitEdits();
        var position = Rows.IndexOf(row);
        row.IsExpanded = !row.IsExpanded;
        if (row.IsExpanded)
            _expanded.Add(row.Id);
        else
            _expanded.Remove(row.Id);
        if (position < 0)
            return;
        if (row.IsExpanded)
        {
            var start = _index[row.Id] + 1;
            var insert = position + 1;
            foreach (var child in Visible(start, End(start, row.Depth)))
                Rows.Insert(insert++, child);
        }
        else
        {
            while (position + 1 < Rows.Count && Rows[position + 1].Depth > row.Depth)
                Rows.RemoveAt(position + 1);
        }
    }

    /// <summary>Zatwierdzenie wiersza w tabeli: zapis, jeśli wiersz ma zmiany.</summary>
    public async Task CommitRow(StructureRowViewModel row)
    {
        OnPropertyChanged(nameof(HasChanges));
        if (row.Changes.Count > 0 && Commit is not null)
            await Commit(row);
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>
    /// Wpis do komórek (wklejenie z Excela, Delete, Ctrl+D): komórki zablokowane w danym wierszu są pomijane, CAM
    /// wpisany imieniem i nazwiskiem – zamieniany na USRID, WP – znacznik (tak / x / 1 – zaznaczony, pusty – nie).
    /// Zmienione wiersze trafiają do zapisu. Zwraca liczbę wpisanych i pominiętych komórek.
    /// </summary>
    public (int Set, int Skipped) SetCells(IEnumerable<(StructureRowViewModel Row, string Column, string? Text)> cells)
    {
        var changed = new List<StructureRowViewModel>();
        var undo = new List<(string Id, string Column, string? Value)>();
        var (set, skipped) = (0, 0);
        foreach (var (row, column, text) in cells)
        {
            if (!row.CanEdit(column))
            {
                skipped++;
                continue;
            }
            var before = row[column];
            row[column] = column switch
            {
                StructureEdits.Cam => DictionaryCells.Resolve(text, Persons),
                StructureEdits.Wp => IsYes(text) ? "true" : "false",
                _ => ValueFormat.Clean(text),
            };
            if ((before ?? "") != (row[column] ?? ""))
                undo.Add((row.Id, column, before));
            set++;
            if (!changed.Contains(row))
                changed.Add(row);
        }
        if (undo.Count > 0)
        {
            _undo.Add(undo);
            if (_undo.Count > UndoLimit)
                _undo.RemoveAt(0);
        }
        var saving = changed.Where(r => r.Changes.Count > 0).ToList();
        foreach (var row in saving)
            _ = CommitRow(row);
        Notice = $"Wpisano komórek: {set}" + (skipped > 0 ? $" · pominięto komórek, których nie można zmienić w tym wierszu: {skipped}" : "")
            + (saving.Count > 0 ? $" · zapis wierszy: {saving.Count}" : "")
            + (undo.Count > 0 ? " · Ctrl+Z cofa" : "");
        OnPropertyChanged(nameof(HasChanges));
        return (set, skipped);
    }

    /// <summary>
    /// Cofnięcie ostatniego wklejenia / Delete / Ctrl+D: komórki dostają wartości sprzed operacji (wiersze po Id – także
    /// po odświeżeniu) i wiersze są zapisywane jak po zwykłej edycji.
    /// </summary>
    private void DoUndo()
    {
        if (_undo.Count == 0)
            return;
        CommitEdits();
        var cells = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var changed = new List<StructureRowViewModel>();
        var missing = 0;
        foreach (var (id, column, value) in cells)
        {
            if (!_index.TryGetValue(id, out var position) || !_all[position].CanEdit(column))
            {
                missing++;
                continue;
            }
            var row = _all[position];
            row[column] = value;
            if (!changed.Contains(row))
                changed.Add(row);
        }
        var saving = changed.Where(r => r.Changes.Count > 0).ToList();
        foreach (var row in saving)
            _ = CommitRow(row);
        Notice = $"Cofnięto zmianę komórek: {cells.Count - missing}" + (saving.Count > 0 ? $" · zapis wierszy: {saving.Count}" : "")
            + (missing > 0 ? $" · pominięto komórek, których już nie ma w strukturze: {missing}" : "")
            + (_undo.Count > 0 ? $" · można cofnąć jeszcze {_undo.Count}" : "");
        OnPropertyChanged(nameof(HasChanges));
    }

    private static bool IsYes(string? text) =>
        StructureEdits.IsChecked(text) || ValueFormat.TryNormalize(new DictColumn("WP", ColumnType.Boolean), text, out var canonical, out _) && canonical == "tak";

    private void SetAll(bool expanded)
    {
        CommitEdits();
        foreach (var row in _all.Where(r => r.HasChildren))
        {
            row.IsExpanded = expanded;
            if (expanded)
                _expanded.Add(row.Id);
            else
                _expanded.Remove(row.Id);
        }
        var selected = _selected?.Id;
        Rows.ReplaceAll(Visible(0, _all.Count));
        Selected = Rows.FirstOrDefault(r => r.Id == selected);
    }

    /// <summary>Koniec poddrzewa wiersza: pierwszy kolejny wiersz o głębokości nie większej niż jego.</summary>
    private int End(int start, int depth)
    {
        var end = start;
        while (end < _all.Count && _all[end].Depth > depth)
            end++;
        return end;
    }

    /// <summary>Wiersze widoczne w zakresie: potomki zwiniętego wiersza są pomijane.</summary>
    private IEnumerable<StructureRowViewModel> Visible(int start, int end)
    {
        var i = start;
        while (i < end)
        {
            var row = _all[i];
            yield return row;
            i = row.IsExpanded || !row.HasChildren ? i + 1 : End(i + 1, row.Depth);
        }
    }
}
