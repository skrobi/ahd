using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Process;

namespace PzlEv.Modules.Projects.Services;

/// <summary>Kontrakt IProjectProcessSource: proces projektu z fabryki ProjectService.Process.</summary>
public sealed class ProjectProcessSource(ProjectService service) : IProjectProcessSource
{
    public ProjectProcess For(string project) => service.Process(project);
}
