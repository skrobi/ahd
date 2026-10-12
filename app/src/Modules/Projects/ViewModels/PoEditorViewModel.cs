using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Edycja nakładki Performance Objectives (kreator, krok 2; ekran Projekt): wczytanie eksportu SAP, elementy CES
/// i węzły wirtualne, zmiana nazw, przenoszenie (przeciąganie, strzałki, na początek / koniec, poziom wyżej,
/// „Przenieś do” i grupowanie kilku zaznaczonych), zwijanie drzewa, usuwanie.
/// </summary>
public sealed class PoEditorViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly IFileDialogs _dialogs;
    private readonly BusyState _busy;
    private readonly Func<string, PoImportResult> _read;
    private readonly Func<PoTree, List<Issue>> _validate;
    private readonly Func<PoTree, PoTree, PoTree> _fromImport;
    private PoTree _tree = new();
    private PoRowViewModel? _selected;
    private string _editName = "";
    private string _editWbs = "";
    private string _editLegacy = "";
    private string _status = "";
    private bool _isDirty;
    private string _mappingInfo = "";
    private readonly HashSet<long> _collapsed = [];   // węzły zwinięte (dzieci ukryte w tabeli)
    private IReadOnlyList<PoRowViewModel> _selectedRows = [];

    /// <param name="validate">Kontrola nakładki w pamięci (bez bazy – wywoływana po każdej zmianie).</param>
    /// <param name="fromImport">Nakładka po wczytaniu pliku: (bieżąca, wczytana) → wynik (kreator – wczytana; projekt – odświeżenie).</param>
    public PoEditorViewModel(IFileDialogs dialogs, BusyState busy, Func<string, PoImportResult> read, Func<PoTree, List<Issue>> validate, Func<PoTree, PoTree, PoTree> fromImport)
    {
        _dialogs = dialogs;
        _busy = busy;
        _read = read;
        _validate = validate;
        _fromImport = fromImport;
        ImportExcel = new AsyncRelayCommand(DoImport, () => !_busy.IsBusy);
        AddVirtual = new RelayCommand(_ => AddNode(_tree.AddVirtual(_selected?.Key, "Nowy węzeł"), "Dodano węzeł wirtualny – wpisz nazwę i kliknij „Zmień zaznaczony”."), _ => !_busy.IsBusy);
        AddElement = new RelayCommand(_ => AddNode(_tree.AddElement(_selected?.Key, "", "Nowy element", null),
            "Dodano element CES – wpisz WBS element, nazwę i Legacy WBS, potem „Zmień zaznaczony”."), _ => !_busy.IsBusy);
        ApplyEdit = new RelayCommand(_ => DoApplyEdit(), _ => (_selected is not null) && !_busy.IsBusy);
        Up = new RelayCommand(_ => Change(() => _tree.Shift(_selected!.Key, -1), null), _ => (_selected is not null) && !_busy.IsBusy);
        Down = new RelayCommand(_ => Change(() => _tree.Shift(_selected!.Key, 1), null), _ => (_selected is not null) && !_busy.IsBusy);
        Top = new RelayCommand(_ => Change(() => _tree.ShiftToEdge(_selected!.Key, first: true), null), _ => (_selected is not null) && !_busy.IsBusy);
        Bottom = new RelayCommand(_ => Change(() => _tree.ShiftToEdge(_selected!.Key, first: false), null), _ => (_selected is not null) && !_busy.IsBusy);
        GroupSelected = new RelayCommand(_ => DoGroup(), _ => SelectedKeys.Count > 0 && !_busy.IsBusy);
        CollapseAll = new RelayCommand(_ => { _collapsed.UnionWith(_tree.Nodes.Where(n => _tree.Children(n.Key).Count > 0).Select(n => n.Key)); Rebuild(_selected?.Key); },
            _ => _tree.Nodes.Count > 0);
        ExpandAll = new RelayCommand(_ => { _collapsed.Clear(); Rebuild(_selected?.Key); }, _ => _collapsed.Count > 0);
        Outdent = new RelayCommand(_ => Change(() => _tree.Outdent(_selected!.Key), null), _ => (_selected?.Node.ParentKey is not null) && !_busy.IsBusy);
        Remove = new RelayCommand(_ => Change(() => _tree.Remove(_selected!.Key), "Usunięto węzeł – jego elementy przeszły poziom wyżej."), _ => (_selected is not null) && !_busy.IsBusy);
        Clear = new RelayCommand(_ => Change(_tree.Clear, "Wyczyszczono nakładkę."), _ => (_tree.Nodes.Count > 0) && !_busy.IsBusy);
        Unselect = new RelayCommand(_ => Selected = null, _ => (_selected is not null) && !_busy.IsBusy);
    }

    /// <summary>Zmiana drzewa (zapis, odświeżenie kontekstu słowników).</summary>
    public event Action? Changed;

    public PoTree Tree => _tree;

    /// <summary>Strona P1S z mapowania dla elementów nakładki (ustawia ekran nadrzędny; domyślnie – brak).</summary>

    /// <summary>Źródło mapowania (raport, korekty, PZLPROD) do pokazania nad tabelą.</summary>
    public string MappingInfo { get => _mappingInfo; set => SetProperty(ref _mappingInfo, value); }

    public ObservableCollection<PoRowViewModel> Rows { get; } = [];

    public ObservableCollection<Issue> Issues { get; } = [];

    public ObservableCollection<Issue> ImportIssues { get; } = [];

    public PoRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            EditName = value?.Node.Name ?? "";
            EditWbs = value is { IsVirtual: false } ? value.Node.WbsElement ?? "" : "";
            EditLegacy = value?.Node.LegacyWbs ?? "";   // węzeł wirtualny – kod wydzielenia ELEMENT_P1S.NAZWA.CPLGR
            OnPropertyChanged(nameof(SelectionText));
        }
    }

    public string SelectionText => _selectedRows.Count > 1
        ? $"Zaznaczono {_selectedRows.Count} – prawy przycisk myszy: „Przenieś do” albo „Grupuj w nowy węzeł”."
        : _selected is null
        ? "Nic nie zaznaczono – nowe węzły trafią do korzenia."
        : $"Zaznaczony: {_selected.Name}{(_selected.IsVirtual ? " (węzeł wirtualny)" : $" ({_selected.WbsElement})")} – nowe węzły trafią pod niego.";

    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

    public string EditWbs { get => _editWbs; set => SetProperty(ref _editWbs, value); }

    public string EditLegacy { get => _editLegacy; set => SetProperty(ref _editLegacy, value); }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool IsDirty { get => _isDirty; private set => SetProperty(ref _isDirty, value); }

    public string Summary => _tree.Nodes.Count == 0 ? "brak struktury" : $"{_tree.ElementCount} elementów CES · {_tree.VirtualCount} węzłów wirtualnych";

    public bool IsEmpty => _tree.Nodes.Count == 0;

    public bool HasIssues => Issues.Count > 0;

    public bool HasImportIssues => ImportIssues.Count > 0;

    public ICommand ImportExcel { get; }
    public ICommand AddVirtual { get; }
    public ICommand AddElement { get; }
    public ICommand ApplyEdit { get; }
    public ICommand Up { get; }
    public ICommand Down { get; }
    public ICommand Top { get; }
    public ICommand Bottom { get; }
    public ICommand GroupSelected { get; }
    public ICommand CollapseAll { get; }
    public ICommand ExpandAll { get; }

    /// <summary>Wiersze zaznaczone w tabeli (Ctrl / Shift – kilka); przenoszenie „Przenieś do” i grupowanie.</summary>
    public IReadOnlyList<PoRowViewModel> SelectedRows
    {
        get => _selectedRows;
        set { _selectedRows = value; OnPropertyChanged(nameof(SelectionText)); }
    }

    /// <summary>Klucze zaznaczonych wierszy (bez zaznaczenia wielu – wiersz wybrany).</summary>
    public IReadOnlyList<long> SelectedKeys =>
        _selectedRows.Count > 0 ? _selectedRows.Select(r => r.Key).ToList() : _selected is null ? [] : [_selected.Key];

    /// <summary>
    /// Cele „Przenieś do”: najwyższy poziom (null) i węzły wirtualne (z wcięciem), pod które można przenieść
    /// wszystkie zaznaczone wiersze.
    /// </summary>
    public IReadOnlyList<(long? Key, string Label)> MoveTargets()
    {
        var keys = SelectedKeys;
        var targets = new List<(long?, string)> { (null, "Najwyższy poziom") };
        targets.AddRange(_tree.Flatten()
            .Where(x => x.Node.IsVirtual && keys.All(k => _tree.CanMove(k, x.Node.Key)))
            .Select(x => ((long?)x.Node.Key, new string(' ', x.Depth * 4) + x.Node.Name)));
        return targets;
    }

    /// <summary>„Przenieś do”: zaznaczone wiersze (z poddrzewami) na koniec dzieci węzła (null – najwyższy poziom).</summary>
    public void MoveSelectedTo(long? parentKey)
    {
        if (_busy.IsBusy)
            return;
        var keys = SelectedKeys;
        var target = parentKey is { } k ? _tree.Find(k)?.Name : "najwyższy poziom";
        var moved = 0;
        Change(() => moved = _tree.MoveAll(keys, parentKey), null);
        Status = moved == 0 ? $"Nic nie przeniesiono – zaznaczone są już w „{target}”." : $"Przeniesiono {moved} do „{target}”.";
    }

    /// <summary>Zwinięcie / rozwinięcie dzieci węzła.</summary>
    public void Toggle(PoRowViewModel row)
    {
        if (!_collapsed.Remove(row.Key))
            _collapsed.Add(row.Key);
        Rebuild(_selected?.Key);
    }
    public ICommand Outdent { get; }
    public ICommand Remove { get; }
    public ICommand Clear { get; }
    public ICommand Unselect { get; }

    /// <summary>Ustawia nakładkę (wczytaną z bazy albo pustą) – bez zmian do zapisania.</summary>
    public void Load(PoTree tree)
    {
        _tree = tree;
        ImportIssues.Clear();
        OnPropertyChanged(nameof(HasImportIssues));
        IsDirty = false;
        Rebuild(null);
        Status = "";
    }

    /// <summary>Przeniesienie przeciągnięciem: węzeł na inny węzeł (staje się jego dzieckiem) albo na korzeń (null).</summary>
    public void Move(long key, long? newParentKey)
    {
        if (_busy.IsBusy)
            return;
        if (!_tree.CanMove(key, newParentKey))
        {
            Status = "Nie można przenieść węzła pod samego siebie ani pod jego element.";
            return;
        }
        Change(() => _tree.Move(key, newParentKey), null);
    }

    /// <summary>Ponowne wyliczenie wierszy (np. po wczytaniu danych mapowania albo listy elementów innych projektów).</summary>
    public void Refresh() => Rebuild(_selected?.Key);

    private async Task DoImport()
    {
        var path = _dialogs.OpenExcel("Wczytaj Performance Objectives – eksport struktury WBS z SAP");
        if (path is null)
            return;
        try
        {
            var result = await _busy.Run($"Wczytywanie pliku {Path.GetFileName(path)}…", () => _read(path));
            ImportIssues.Clear();
            foreach (var issue in result.Issues.Take(50))
                ImportIssues.Add(issue);
            OnPropertyChanged(nameof(HasImportIssues));
            if (result.HasErrors)
            {
                Status = $"Plik {result.FileName} ma błędy (ERROR) – nakładka bez zmian. Popraw plik i wczytaj ponownie.";
                return;
            }
            _tree = _fromImport(_tree, result.Tree);
            IsDirty = true;
            Rebuild(null);
            Status = $"Wczytano {result.FileName}: {_tree.ElementCount} elementów CES.";
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException or InvalidDataException)
        {
            Logger.Error(ex, "Wczytanie Performance Objectives");
            Status = $"Nie udało się wczytać pliku: {ex.Message}";
        }
    }

    /// <summary>Nowy węzeł wirtualny w miejscu pierwszego zaznaczonego, z zaznaczonymi jako dziećmi; zaznaczony – do nadania nazwy.</summary>
    private void DoGroup()
    {
        var keys = SelectedKeys;
        PoNode? group = null;
        Change(() => group = _tree.Group(keys, "Nowy węzeł"), null);
        if (group is null)
            return;
        Rebuild(group.Key);
        Status = $"Zgrupowano {_tree.Children(group.Key).Count} w nowym węźle – wpisz nazwę i kliknij „Zmień zaznaczony”.";
    }

    private void DoApplyEdit()
    {
        var node = _selected!.Node;
        if (EditName.Trim().Length == 0)
        {
            Status = "Podaj nazwę węzła.";
            return;
        }
        Change(() =>
        {
            node.Name = EditName.Trim();
            if (!node.IsVirtual)
                node.WbsElement = EditWbs.Trim().Length > 0 ? EditWbs.Trim() : null;
            node.LegacyWbs = EditLegacy.Trim().Length > 0 ? EditLegacy.Trim() : null;
        }, "Zmieniono węzeł.");
    }

    private void AddNode(PoNode node, string status)
    {
        IsDirty = true;
        Rebuild(node.Key);
        Status = status;
        Changed?.Invoke();
    }

    private void Change(Action action, string? status)
    {
        var key = _selected?.Key;
        action();
        IsDirty = true;
        Rebuild(key);
        if (status is not null)
            Status = status;
        Changed?.Invoke();
    }

    private void Rebuild(long? selectKey)
    {
        Rows.Clear();
        _collapsed.RemoveWhere(k => _tree.Find(k) is null);
        var children = _tree.Nodes.Where(n => n.ParentKey is not null).GroupBy(n => n.ParentKey!.Value).ToDictionary(g => g.Key, g => g.Count());
        var hiddenBelow = int.MaxValue;   // głębokość zwiniętego przodka – głębsze wiersze ukryte
        foreach (var (node, depth) in _tree.Flatten())
        {
            if (depth > hiddenBelow)
                continue;
            var collapsed = _collapsed.Contains(node.Key) && children.ContainsKey(node.Key);
            hiddenBelow = collapsed ? depth : int.MaxValue;
            Rows.Add(new PoRowViewModel(node, depth, children.GetValueOrDefault(node.Key), collapsed));
        }
        _selectedRows = [];
        Selected = Rows.FirstOrDefault(r => r.Key == selectKey);
        Validate();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Validate()
    {
        Issues.Clear();
        foreach (var issue in _validate(_tree))
            Issues.Add(issue);
        OnPropertyChanged(nameof(HasIssues));
    }
}
