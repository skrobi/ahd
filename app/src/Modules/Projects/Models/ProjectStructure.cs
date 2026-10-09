namespace PzlEv.Modules.Projects.Models;

/// <summary>Struktura projektu: wiersze w kolejności drzewa (w głąb) i sumy.</summary>
public sealed record ProjectStructure(IReadOnlyList<StructureRow> Rows, StructureSummary Summary)
{
    public static ProjectStructure Empty { get; } = new([], new StructureSummary(0, 0, 0, 0, null, null, 0, 0));
}
