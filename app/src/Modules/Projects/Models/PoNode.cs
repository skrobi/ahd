namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Węzeł nakładki Performance Objectives (META_PerformanceObjective; docs/performance-objectives.md): element CES
/// (WbsElement – klucz) albo węzeł wirtualny dodany w PZL-EV. Key – identyfikator w edycji: NodeId węzła zapisanego,
/// liczba ujemna dla nowego. NodeId / Version – wersja, na której pracuje użytkownik (null – nowy węzeł).
/// </summary>
public sealed class PoNode
{
    public long Key { get; init; }

    public long? NodeId { get; set; }

    public int? Version { get; set; }

    public long? ParentKey { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Level z Excela (1 = korzeń); po zmianach w aplikacji – głębokość w drzewie.</summary>
    public int Level { get; set; } = 1;

    public bool IsVirtual { get; init; }

    public string? ProjectDefinition { get; set; }

    public string? WbsElement { get; set; }

    public string Name { get; set; } = "";

    public string? PersonResponsible { get; set; }

    public string? ProfitCenter { get; set; }

    /// <summary>Odpowiednik P1S (np. AC-CAB.6.38) – strona P1S w drzewie i powiązanie z WP (słownik „WP i CAM”).</summary>
    public string? LegacyWbs { get; set; }

    public string? PerformanceObligation { get; set; }

    public string? SacObjNumber { get; set; }

    public bool IsStatistical { get; set; }

    public bool IsAcctAsstElement { get; set; }

    public PoNode Copy() => (PoNode)MemberwiseClone();

    /// <summary>Te same dane (bez identyfikatorów i wersji) – czy zapis ma utworzyć nową wersję.</summary>
    public bool SameContent(PoNode other) =>
        ParentKey == other.ParentKey && SortOrder == other.SortOrder && Level == other.Level && IsVirtual == other.IsVirtual
        && ProjectDefinition == other.ProjectDefinition && WbsElement == other.WbsElement && Name == other.Name
        && PersonResponsible == other.PersonResponsible && ProfitCenter == other.ProfitCenter && LegacyWbs == other.LegacyWbs
        && PerformanceObligation == other.PerformanceObligation && SacObjNumber == other.SacObjNumber
        && IsStatistical == other.IsStatistical && IsAcctAsstElement == other.IsAcctAsstElement;
}
