using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Projects.Services;

/// <summary>Kontrakt ICostAssignment: przypisanie kosztów elementów CES do WP według struktury programu (ProjectService.CostTargets).</summary>
public sealed class CostAssignmentSource(ProjectService service) : ICostAssignment
{
    public CostAssignment Assign(string project, IReadOnlyDictionary<string, decimal> costs) => service.CostTargets(project, costs);
}
