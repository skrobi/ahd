using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Edycja nakładki Performance Objectives (kreator, krok 2; ekran Projekt): wczytanie eksportu SAP, elementy CES
/// i węzły wirtualne, zmiana nazw, przenoszenie (przeciąganie, strzałki, poziom wyżej), usuwanie.
/// </summary>
public sealed class PoEditorViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    private readonly IFileDialogs _dialogs;
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

    /// <param name="fromImport">Nakładka po wczytaniu pliku: (bieżąca, wczytana) → wynik (kreator – wczytana; projekt – odświeżenie).</param>
    public PoEditorViewModel(IFileDialogs dialogs, Func<string, PoImportResult> read, Func<PoTree, List<Issue>> validate, Func<PoTree, PoTree, PoTree> fromImport)
    {
        _dialogs = dialogs;
        _read = read;
        _validate = validate;
        _fromImport = fromImport;
        ImportExcel = new RelayCommand(_ => DoImport());
        AddVirtual = new RelayCommand(_ => AddNode(_tree.AddVirtual(_selected?.Key, "Nowy węzeł"), "Dodano węzeł wirtualny – wpisz nazwę i kliknij „Zmień zaznaczony”."));
        AddElement = new RelayCommand(_ => AddNode(_tree.AddElement(_selected?.Key, "", "Nowy element", null),
            "Dodano element CES – wpisz WBS element, nazwę i Legacy WBS, potem „Zmień zaznaczony”."));
        ApplyEdit = new RelayCommand(_ => DoApplyEdit(), _ => _selected is not null);
        Up = new RelayCommand(_ => Change(() => _tree.Shift(_selected!.Key, -1), null), _ => _selected is not null);
        Down = new RelayCommand(_ => Change(() => _tree.Shift(_selected!.Key, 1), null), _ => _selected is not null);
        Outdent = new RelayCommand(_ => Change(() => _tree.Outdent(_selected!.Key), null), _ => _selected?.Node.ParentKey is not null);
        Remove = new RelayCommand(_ => Change(() => _tree.Remove(_selected!.Key), "Usunięto węzeł – jego elementy przeszły poziom wyżej."), _ => _selected is not null);
        Clear = new RelayCommand(_ => Change(_tree.Clear, "Wyczyszczono nakładkę."), _ => _tree.Nodes.Count > 0);
        Unselect = new RelayCommand(_ => Selected = null, _ => _selected is not null);
    }

    /// <summary>Zmiana drzewa (zapis, odświeżenie kontekstu słowników).</summary>
    public event Action? Changed;

    public PoTree Tree => _tree;

    /// <summary>Strona P1S z mapowania dla elementów nakładki (ustawia ekran nadrzędny; domyślnie – brak).</summary>
    public Func<PoTree, IReadOnlyDictionary<long, MappingResult>> ResolveMapping { get; set; } = _ => new Dictionary<long, MappingResult>();

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
            EditLegacy = value is { IsVirtual: false } ? value.Node.LegacyWbs ?? "" : "";
            OnPropertyChanged(nameof(SelectionText));
        }
    }

    public string SelectionText => _selected is null
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
        if (!_tree.CanMove(key, newParentKey))
        {
            Status = "Nie można przenieść węzła pod samego siebie ani pod jego element.";
            return;
        }
        Change(() => _tree.Move(key, newParentKey), null);
    }

    private void DoImport()
    {
        var path = _dialogs.OpenExcel("Wczytaj Performance Objectives – eksport struktury WBS z SAP");
        if (path is null)
            return;
        try
        {
            var result = _read(path);
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
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Logger.Error(ex, "Wczytanie Performance Objectives");
            Status = $"Nie udało się wczytać pliku: {ex.Message}";
        }
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
            {
                node.WbsElement = EditWbs.Trim().Length > 0 ? EditWbs.Trim() : null;
                node.LegacyWbs = EditLegacy.Trim().Length > 0 ? EditLegacy.Trim() : null;
            }
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
        var mapping = ResolveMapping(_tree);
        foreach (var (node, depth) in _tree.Flatten())
            Rows.Add(new PoRowViewModel(node, depth, mapping.GetValueOrDefault(node.Key)));
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
