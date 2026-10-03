using System.Windows;
using PzlEv.Modules.Projects.Models;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Wiersz tabeli nakładki: węzeł z wcięciem według głębokości.</summary>
public sealed class PoRowViewModel(PoNode node, int depth)
{
    public long Key => node.Key;

    public PoNode Node => node;

    public int Depth => depth;

    public Thickness Indent => new(depth * 18, 0, 0, 0);

    public string Name => node.Name;

    public string Kind => node.IsVirtual ? "węzeł" : "";

    public bool IsVirtual => node.IsVirtual;

    public string WbsElement => node.IsVirtual ? "—" : node.WbsElement ?? "";

    public string LegacyWbs => node.IsVirtual ? "—" : node.LegacyWbs ?? "";

    public string PerformanceObligation => node.PerformanceObligation ?? "";
}
