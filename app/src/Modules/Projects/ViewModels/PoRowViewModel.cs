using System.Windows;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Mapping;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Wiersz tabeli nakładki: węzeł z wcięciem według głębokości i stroną P1S z mapowania CES ↔ P1S.</summary>
public sealed class PoRowViewModel(PoNode node, int depth, MappingResult? mapping)
{
    public long Key => node.Key;

    public PoNode Node => node;

    public int Depth => depth;

    public Thickness Indent => new(depth * 18, 0, 0, 0);

    public string Name => node.Name;

    public bool IsVirtual => node.IsVirtual;

    public string WbsElement => node.IsVirtual ? "—" : node.WbsElement ?? "";

    public string LegacyWbs => node.IsVirtual ? "—" : node.LegacyWbs ?? "";

    /// <summary>Status mapowania elementu CES (REPORT, OVERRIDE, INHERITED, UNMAPPED); węzeł wirtualny – brak.</summary>
    public Pill? MappingStatus => mapping is null ? null : new Pill(mapping.StatusColor, mapping.Status);

    public string MappingTarget => mapping is null ? "" : mapping.IsMapped ? mapping.Target : mapping.Proposal.Length > 0 ? $"propozycja: {mapping.Proposal}" : "";

    public string MappingOrigin => mapping?.Origin ?? "";

    public string PerformanceObligation => node.PerformanceObligation ?? "";
}
