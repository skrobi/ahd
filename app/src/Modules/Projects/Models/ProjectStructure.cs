namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Struktura projektu: wiersze w kolejności drzewa (w głąb) i sumy; CostTargets – przypisanie kosztu każdego elementu CES z kosztów
/// do WP albo powód braku (bez kosztów – puste; CAM i Cost Category uzupełnia ProjectService.CostTargets).
/// </summary>
public sealed record ProjectStructure(IReadOnlyList<StructureRow> Rows, StructureSummary Summary, IReadOnlyList<PzlEv.Shared.Utils.Data.CostTarget>? CostTargets = null)
{
    public static ProjectStructure Empty { get; } = new([], new StructureSummary(0, 0, 0, 0, null, null, 0, 0));
}
