using PzlEv.Modules.Mapping.Models;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Mapping.ViewModels;

/// <summary>
/// Węzeł drzewa ekranu Mapowanie: poziom kategorii, element P1S albo grupa („Nieprzypisane”, projekt CES, „Cel spoza
/// LOG.WBS”). Dzieci: węzły P1S i elementy CES przypisane do elementu P1S (CesTreeItem).
/// </summary>
public sealed class P1sTreeItem(string title, P1sElement? element = null, bool isGreyed = false) : ObservableObject
{
    private bool _isExpanded;

    public string Title { get; } = title;

    public P1sElement? Element { get; } = element;

    public bool IsGreyed { get; } = isGreyed;

    public List<object> Children { get; } = [];

    /// <summary>Liczba elementów CES w poddrzewie.</summary>
    public int CesCount { get; set; }

    public string CesCountText => CesCount == 0 ? "" : $"CES {CesCount}";

    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
}

/// <summary>Element CES w drzewie – pod elementem P1S, do którego jest przypisany, albo w „Nieprzypisane”.</summary>
public sealed class CesTreeItem(MappingResult result)
{
    public MappingResult Result { get; } = result;

    public string Title => $"{Result.CesElement}  ·  {Result.Status}{(Result.IsNew ? "  ·  nowy" : "")}";
}
