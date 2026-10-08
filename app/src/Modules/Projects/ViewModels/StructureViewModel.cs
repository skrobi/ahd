using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Tabela struktury projektu (ekran Projekt, zakładka Struktura): drzewo spłaszczone do wierszy tabeli – rozwinięcie
/// wstawia wiersze potomków, zwinięcie je usuwa. Stan rozwinięcia i zaznaczenie trwają po odświeżeniu danych (po
/// identyfikatorze wiersza). Węzły nakładki są domyślnie rozwinięte, elementy P1S – zwinięte.
/// </summary>
public sealed class StructureViewModel : ObservableObject
{
    private readonly HashSet<string> _expanded = [];
    private readonly HashSet<string> _known = [];
    private List<StructureRowViewModel> _all = [];
    private Dictionary<string, int> _index = [];
    private StructureRowViewModel? _selected;
    private StructureSummary _summary = ProjectStructure.Empty.Summary;

    public StructureViewModel()
    {
        ExpandAll = new RelayCommand(_ => SetAll(true), _ => _all.Count > 0);
        CollapseAll = new RelayCommand(_ => SetAll(false), _ => _all.Count > 0);
        RevertChanges = new RelayCommand(_ => { foreach (var row in _all.Where(r => r.Changes.Count > 0)) row.Revert(); OnPropertyChanged(nameof(HasChanges)); },
            _ => HasChanges);
    }

    /// <summary>Zapis zmian wiersza po jego zatwierdzeniu w tabeli (ustawia ekran projektu).</summary>
    public Func<StructureRowViewModel, Task>? Commit { get; set; }

    /// <summary>Czy wolno teraz edytować (np. brak zapisu w toku).</summary>
    public Func<bool> CanEditNow { get; set; } = () => true;

    public ObservableCollection<StructureRowViewModel> Rows { get; } = [];

    /// <summary>Lista wyboru CAM (USRID → imię i nazwisko).</summary>
    public ObservableCollection<LookupOption> Persons { get; } = [];

    public StructureRowViewModel? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public StructureSummary Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public string SummaryText => _all.Count == 0
        ? "brak struktury"
        : $"{_all.Count(r => r.IsObjective && !r.IsVirtual)} elementów CES · {Summary.Wps} WP · {Summary.Cams} CAM";

    public bool HasChanges => _all.Any(r => r.Changes.Count > 0);

    public ICommand ExpandAll { get; }
    public ICommand CollapseAll { get; }
    public ICommand RevertChanges { get; }

    /// <summary>Nowe dane struktury – zachowuje rozwinięcie i zaznaczenie; niezapisane zmiany wierszy przepadają.</summary>
    public void Load(ProjectStructure structure, IReadOnlyList<LookupOption> persons)
    {
        if (!Persons.SequenceEqual(persons))
        {
            Persons.Clear();
            foreach (var person in persons)
                Persons.Add(person);
        }
        var selected = _selected?.Id;
        _all = structure.Rows.Select(r => new StructureRowViewModel(r)).ToList();
        _index = _all.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
        foreach (var row in _all)
        {
            if (_known.Add(row.Id) && row.IsObjective)
                _expanded.Add(row.Id);
            row.IsExpanded = _expanded.Contains(row.Id);
        }
        Summary = structure.Summary;
        Rows.Clear();
        foreach (var row in Visible(0, _all.Count))
            Rows.Add(row);
        Selected = Rows.FirstOrDefault(r => r.Id == selected);
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasChanges));
    }

    public void Toggle(StructureRowViewModel row)
    {
        if (!row.HasChildren)
            return;
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

    private void SetAll(bool expanded)
    {
        foreach (var row in _all.Where(r => r.HasChildren))
        {
            row.IsExpanded = expanded;
            if (expanded)
                _expanded.Add(row.Id);
            else
                _expanded.Remove(row.Id);
        }
        var selected = _selected?.Id;
        Rows.Clear();
        foreach (var row in Visible(0, _all.Count))
            Rows.Add(row);
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
