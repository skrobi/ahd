using System.Windows;
using PzlEv.Modules.Projects.Models;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Wiersz tabeli nakładki: węzeł z wcięciem według głębokości, stroną P1S (Legacy WBS) i zwinięciem dzieci. Nazwa,
/// Element CES (nie w węźle wirtualnym) i P1S zmienia się w komórkach tabeli – edited dostaje opis zmiany.
/// </summary>
public sealed class PoRowViewModel(PoNode node, int depth, int children = 0, bool collapsed = false, Action<string>? edited = null)
{
    /// <summary>Liczba dzieci węzła (0 – bez przycisku zwijania).</summary>
    public int Children => children;

    public bool HasChildren => children > 0;

    public bool IsCollapsed => collapsed;

    /// <summary>▸ zwinięty, ▾ rozwinięty, pusty – bez dzieci.</summary>
    public string Expander => children == 0 ? "" : collapsed ? "▸" : "▾";

    public string ExpanderHint => collapsed ? $"Rozwiń ({children})" : "Zwiń";

    /// <summary>Liczba ukrytych dzieci przy zwiniętym węźle, np. „(12)”.</summary>
    public string CollapsedInfo => collapsed ? $"({children})" : "";

    public long Key => node.Key;

    public PoNode Node => node;

    public int Depth => depth;

    public Thickness Indent => new(depth * 18, 0, 0, 0);

    /// <summary>Nazwa węzła; pusta – bez zmiany.</summary>
    public string Name
    {
        get => node.Name;
        set
        {
            var name = value?.Trim() ?? "";
            if (name.Length == 0)
                edited?.Invoke("Nazwa nie może być pusta – bez zmiany.");
            else if (name != node.Name)
            {
                node.Name = name;
                edited?.Invoke($"Zmieniono nazwę: {name}.");
            }
        }
    }

    public bool IsVirtual => node.IsVirtual;

    /// <summary>WBS element CES; węzeł wirtualny – „—” (bez edycji).</summary>
    public string WbsElement
    {
        get => node.IsVirtual ? "—" : node.WbsElement ?? "";
        set
        {
            if (node.IsVirtual || Text(value) == node.WbsElement)
                return;
            node.WbsElement = Text(value);
            edited?.Invoke($"Zmieniono Element CES: {node.WbsElement ?? "(pusty)"}.");
        }
    }

    /// <summary>Legacy WBS (element P1S); węzeł wirtualny – kod wydzielenia ELEMENT_P1S.NAZWA.CPLGR albo pusty.</summary>
    public string LegacyWbs
    {
        get => node.LegacyWbs ?? "";
        set
        {
            if (Text(value) == node.LegacyWbs)
                return;
            node.LegacyWbs = Text(value);
            edited?.Invoke($"Zmieniono P1S: {node.LegacyWbs ?? "(pusty)"}.");
        }
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public string PerformanceObligation => node.PerformanceObligation ?? "";
}
