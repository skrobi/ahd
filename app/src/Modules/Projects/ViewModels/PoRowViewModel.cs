using System.Windows;
using PzlEv.Modules.Projects.Models;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Wiersz tabeli nakładki: węzeł z wcięciem według głębokości, stroną P1S (Legacy WBS) i zwinięciem dzieci.</summary>
public sealed class PoRowViewModel(PoNode node, int depth, int children = 0, bool collapsed = false)
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

    public string Name => node.Name;

    public bool IsVirtual => node.IsVirtual;

    public string WbsElement => node.IsVirtual ? "—" : node.WbsElement ?? "";

    public string LegacyWbs => node.IsVirtual ? "—" : node.LegacyWbs ?? "";

    public string PerformanceObligation => node.PerformanceObligation ?? "";
}
