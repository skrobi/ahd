using System.Windows.Input;
using PzlEv.Modules.MasterData.Models;
using PzlEv.Modules.MasterData.Services;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>
/// Struktura P1S z PZLPROD (LOG.WBS) – podgląd tylko do odczytu w Słownikach (docs/zrodla-danych.md, rozdz. 5.3):
/// Z_KAT_ZBIORCZA → Z_KATEGORIA → Z_OPIS → PROJORG → elementy. Wczytywana przy pierwszym otwarciu.
/// </summary>
public sealed class P1sStructureViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "master-data");

    private readonly IPzlProdSource? _source;
    private IReadOnlyList<P1sElement> _elements = [];
    private IReadOnlyList<P1sTreeItem> _tree = [];
    private IReadOnlyList<P1sElement> _matches = [];
    private string _search = "";
    private string _status = "";
    private bool _loaded;

    public P1sStructureViewModel(IPzlProdSource? source)
    {
        _source = source;
        Refresh = new AsyncRelayCommand(Load, () => !Busy.IsBusy);
    }

    public BusyState Busy { get; } = new();

    public ICommand Refresh { get; }

    public IReadOnlyList<P1sTreeItem> Tree { get => _tree; private set => SetProperty(ref _tree, value); }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Szukanie po kodzie i opisie (od 2 znaków) – lista trafień nad drzewem.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
                Matches = Find(value);
        }
    }

    public IReadOnlyList<P1sElement> Matches
    {
        get => _matches;
        private set
        {
            if (SetProperty(ref _matches, value))
                OnPropertyChanged(nameof(HasMatches));
        }
    }

    public bool HasMatches => Matches.Count > 0;

    /// <summary>Pierwsze otwarcie – odczyt z PZLPROD.</summary>
    public void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        _ = Load();
    }

    private async Task Load()
    {
        if (_source is null)
        {
            Status = "Brak połączenia z PZLPROD – dodaj sekcję PzlProd w pzl-ev.json (Environments.<Env>.PzlProd: Server, Database, Schema).";
            return;
        }
        try
        {
            var (elements, tree) = await Busy.Run("Wczytywanie struktury P1S (PZLPROD)…", () =>
            {
                var read = _source.Elements();
                return (read, P1sTreeBuilder.Build(read));
            });
            _elements = elements;
            Tree = tree.Select(Convert).ToList();
            Matches = Find(Search);
            Status = $"{elements.Count} elementów LOG.WBS";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Struktura P1S – odczyt PZLPROD nieudany");
            Status = $"Odczyt PZLPROD (LOG.WBS) nieudany: {ex.Message}";
        }
    }

    private IReadOnlyList<P1sElement> Find(string text)
    {
        text = text.Trim();
        if (text.Length < 2)
            return [];
        return _elements
            .Where(e => e.WbsElement.Contains(text, StringComparison.OrdinalIgnoreCase) || e.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.WbsElement, StringComparer.Ordinal)
            .Take(200)
            .ToList();
    }

    private static P1sTreeItem Convert(P1sNode node)
    {
        var item = new P1sTreeItem(node.Title, node.IsGreyed);
        item.Children.AddRange(node.Children.Select(Convert));
        return item;
    }
}

/// <summary>Węzeł drzewa P1S: poziom kategorii, grupa albo element WBS (nieaktywny/usunięty – wyszarzony).</summary>
public sealed class P1sTreeItem(string title, bool isGreyed) : ObservableObject
{
    private bool _isExpanded;

    public string Title { get; } = title;

    public bool IsGreyed { get; } = isGreyed;

    public List<P1sTreeItem> Children { get; } = [];

    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
}
