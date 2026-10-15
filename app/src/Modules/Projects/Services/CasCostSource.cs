using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Projects.Services;

/// <summary>Kontrakt ICasCostSource: kalkulator kosztu CAS projektu CAS (ProjectService.CasCalculator) w walucie projektu.</summary>
public sealed class CasCostSource(ProjectService service) : ICasCostSource
{
    public CasCosts? For(string project) =>
        service.Find(project)?.Type == ProjectTypes.Cas ? service.CasCalculator(project, service.CasCurrency(project, service.ActualsAmount(project))) : null;
}
