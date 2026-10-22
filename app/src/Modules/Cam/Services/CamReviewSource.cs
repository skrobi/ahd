using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Cam.Services;

/// <summary>
/// Kontrakt ICamReview (etap przebiegu „Estymacje CAM”, kolory struktury programu): estymacje programu w cyklu i decyzje finansisty
/// przez CamService – zatwierdzenie, zwrot, ponowne otwarcie; estymacja zmieniona od wczytania listy – odmowa (CamService.Decide).
/// </summary>
public sealed class CamReviewSource(CamService service) : ICamReview
{
    public CamCycle Cycle(DateOnly day) => service.Cycle(day);

    public CamCycle CycleOf(string period) => service.CycleOf(period);

    public CamReview Program(string project, CamCycle cycle) => service.Program(project, cycle);

    public IReadOnlyList<CamReviewItem> Saved(string project, string period) => service.Saved(project, period);

    public CamDecision Approve(CamReviewItem item) => Decision(service.Decide(item, service.Approve));

    public CamDecision Return(CamReviewItem item, string note) => Decision(service.Decide(item, e => service.Return(e, note)));

    public CamDecision Reopen(CamReviewItem item, string comment) => Decision(service.Decide(item, e => service.Reopen(e, comment)));

    private static CamDecision Decision(CamOutcome outcome) => new(outcome.Ok, outcome.Message);
}
