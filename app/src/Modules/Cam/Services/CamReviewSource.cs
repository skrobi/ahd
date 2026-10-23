using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Cam.Services;

/// <summary>
/// Kontrakt ICamReview (etap przebiegu „Estymacje CAM”, kolory struktury programu): estymacje programu w cyklu i decyzje finansisty
/// przez CamService – zatwierdzenie, zwrot, ponowne otwarcie; estymacja zmieniona od wczytania listy – odmowa (CamService.Decide).
/// </summary>
public sealed class CamReviewSource(CamService service) : ICamReview
{
    public CamCycle Cycle(DateOnly day, bool weekly = false) => service.Cycle(day, weekly);

    public CamCycle CycleOf(string code) => service.CycleOf(code);

    public CamNotifyResult Notify(string project, CamCycle cycle, long? pinId, bool resend) => service.Notify(project, cycle, pinId, resend);

    public IReadOnlyList<CamNotification> Notifications(string project, string cycle) => service.Notifications(project, cycle);

    public CamReview Program(string project, CamCycle cycle) => service.Program(project, cycle);

    public IReadOnlyList<CamReviewItem> Saved(string project, string period) => service.Saved(project, period);

    public CamDecision Approve(CamReviewItem item) => Decision(item, service.Decide(item, service.Approve));

    public CamDecision Return(CamReviewItem item, string note) => Decision(item, service.Decide(item, e => service.Return(e, note)));

    public CamDecision Reopen(CamReviewItem item, string comment) => Decision(item, service.Decide(item, e => service.Reopen(e, comment)));

    private static CamDecision Decision(CamReviewItem item, CamOutcome outcome) =>
        new(outcome.Ok, outcome.Message, outcome.Estimate is { } saved ? CamService.Item(saved, item.Name, item.Required) : null);
}
