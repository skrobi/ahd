using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Modules.Mapping.Models;

/// <summary>
/// Węzeł drzewa P1S (docs/zrodla-danych.md, rozdz. 5.3): poziom kategorii (Z_KAT_ZBIORCZA, Z_KATEGORIA, Z_OPIS),
/// grupa „Bez kategorii w WBS” albo element WBS (PROJORG i elementy wg PARENT).
/// </summary>
public sealed class P1sNode(string title, P1sElement? element = null)
{
    public string Title { get; } = title;

    public P1sElement? Element { get; } = element;

    public List<P1sNode> Children { get; } = [];

    public bool IsGreyed => Element?.IsGreyed ?? false;

    /// <summary>Liczba elementów WBS w poddrzewie (z tym węzłem).</summary>
    public int ElementCount => (Element is null ? 0 : 1) + Children.Sum(c => c.ElementCount);

    public IEnumerable<P1sNode> Descendants() => Children.SelectMany(c => c.Descendants().Prepend(c));
}
