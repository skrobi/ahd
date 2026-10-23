using PzlEv.Modules.Cam.Data;
using PzlEv.Modules.Cam.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Mail;

namespace PzlEv.Modules.Cam.Services;

/// <summary>WP na liście zadań cyklu: dane WP, estymacja (zapisana albo nowa z propozycji) i czego brakuje do wysłania.</summary>
public sealed record CamWorkItem(CamWorkPackage Wp, CamEstimate Estimate, IReadOnlyList<(string Item, bool Done)> Checklist);

/// <summary>Wynik operacji na estymacji: powodzenie, komunikat, zapisana estymacja, błędy walidacji.</summary>
public sealed record CamOutcome(bool Ok, string Message, CamEstimate? Estimate = null, IReadOnlyList<string>? Errors = null);

/// <summary>
/// Moduł CAM (docs/funkcjonalnosc.md; tasks/F11.2–F11.4): lista zadań cyklu, szkic i wysyłka estymacji (walidacja CamEstimates),
/// zatwierdzenie – „Harmonogram i budżet” programu przez kontrakt ICamWorkPackages – albo zwrot z uwagą; historia i dziennik.
/// </summary>
public sealed class CamService(ICamStore store, Func<ICamWorkPackages?> packages, AppServices services, IMailSender? mail = null)
{
    private const string Area = "CAM";

    private DateOnly Today => DateOnly.FromDateTime(services.Clock.Now.LocalDateTime);

    /// <summary>Moduł Projekty niedostępny – brak WP (komunikat na ekranie).</summary>
    public bool Available => packages() is not null;

    /// <summary>Cykl dnia stanu; poza kalendarzem okresów – miesiąc kalendarzowy bez terminu.</summary>
    public CamCycle Cycle(bool weekly = false) => Cycle(Today, weekly);

    /// <summary>Cykl dnia (miesięczny albo tygodniowy); poza kalendarzem okresów – miesiąc kalendarzowy bez terminu.</summary>
    public CamCycle Cycle(DateOnly day, bool weekly = false) =>
        store.Cycle(day, weekly) ?? new CamCycle(day.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), null);

    /// <summary>Cykl kodu RRRR-MM albo RRRR-Tnn (okres albo tydzień przebiegu) z terminem z kalendarza okresów.</summary>
    public CamCycle CycleOf(string code) => new(code, store.Deadline(code));

    public IReadOnlyList<CamPerson> Cams() => packages()?.Cams() ?? [];

    public IReadOnlyList<CamWorkPackage> WorkPackages(string cam) => packages()?.WorkPackages(cam) ?? [];

    /// <summary>
    /// Lista zadań: WP CAM-a z estymacją cyklu programu – miesięczny (monthly) albo tygodniowy (weekly; program estymujący co tydzień);
    /// bez zapisanej – nowa z propozycji, a w cyklu tygodniowym od ostatnio zatwierdzonej (CamEstimates.Continue); od wymagających działania.
    /// </summary>
    public IReadOnlyList<CamWorkItem> Worklist(IReadOnlyList<CamWorkPackage> wps, CamCycle monthly, CamCycle? weekly = null)
    {
        weekly ??= wps.Any(w => w.WeeklyCycle) ? Cycle(true) : null;
        var saved = new[] { monthly, weekly }.OfType<CamCycle>().Select(c => c.Period).Distinct()
            .SelectMany(c => store.Estimates(c)).ToDictionary(e => (e.Project.ToUpperInvariant(), e.Wp.ToUpperInvariant(), e.Cycle));
        return wps.Select(wp =>
            {
                var cycle = wp.WeeklyCycle ? weekly! : monthly;
                var estimate = saved.GetValueOrDefault((wp.Project.ToUpperInvariant(), wp.Wp.ToUpperInvariant(), cycle.Period)) ?? Fresh(wp, cycle);
                return new CamWorkItem(wp, estimate, CamEstimates.Checklist(wp, estimate));
            })
            .OrderBy(i => CamStatuses.Order(i.Estimate.Status)).ThenBy(i => i.Wp.Project, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Wp.Wp, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Nowa estymacja cyklu: z propozycji systemu, w cyklu tygodniowym – od ostatnio zatwierdzonej (wartości, daty, uzasadnienie).</summary>
    private CamEstimate Fresh(CamWorkPackage wp, CamCycle cycle)
    {
        var fresh = CamEstimates.New(wp, cycle.Period, Today);
        return cycle.Weekly && store.LastApproved(wp.Project, wp.Wp, cycle.Period) is { } last ? CamEstimates.Continue(fresh, last, Today) : fresh;
    }

    /// <summary>Zapis szkicu (submit false) albo wysyłka do zatwierdzenia (walidacja CamEstimates.Validate); estymacja wysłana lub zatwierdzona – bez zmian.</summary>
    public CamOutcome Save(CamWorkPackage wp, CamEstimate estimate, bool submit)
    {
        if (!CamStatuses.Editable(estimate.Status))
            return new CamOutcome(false, $"Estymacja jest {estimate.Status.ToLowerInvariant()} – edycja zablokowana do decyzji analityka.");
        if (submit && CamEstimates.Validate(wp, estimate) is { Count: > 0 } errors)
            return new CamOutcome(false, $"Nie wysłano – uzupełnij: {string.Join("; ", errors)}.", Errors: errors);
        var now = services.Clock.Now;
        var user = services.User.Account;
        var status = submit ? CamStatuses.Sent : CamStatuses.Draft;
        var previous = estimate.EstimateId is null ? null : store.Estimate(estimate.Project, estimate.Wp, estimate.Cycle);
        var next = estimate with
        {
            Status = status, Currency = wp.Currency ?? estimate.Currency,
            SubmittedAt = submit ? now : estimate.SubmittedAt, SubmittedBy = submit ? user : estimate.SubmittedBy,
        };
        try
        {
            var summary = CamEstimates.Describe(next, previous);
            var saved = store.Save(next, new CamEstimateEvent(now, user, status, summary, next.Justification));
            if (submit)
                services.Journal.Add(Area, $"Estymacja WP {wp.Wp} ({wp.Project}, cykl {estimate.Cycle}) wysłana do zatwierdzenia: {summary}", wp.Project);
            return new CamOutcome(true, submit ? $"Wysłano do zatwierdzenia ({now.LocalDateTime:yyyy-MM-dd HH:mm})." : $"Szkic zapisany ({now.LocalDateTime:HH:mm}).", saved);
        }
        catch (CamConflictException ex)
        {
            return new CamOutcome(false, ex.Message);
        }
    }

    /// <summary>Estymacje cyklu czekające na analityka (wszyscy CAM-owie).</summary>
    public IReadOnlyList<CamEstimate> Pending(CamCycle cycle) =>
        store.Estimates(cycle.Period).Where(e => e.Status == CamStatuses.Sent).OrderBy(e => e.SubmittedAt).ToList();

    /// <summary>
    /// Zatwierdzenie: „Harmonogram i budżet” programu (ETC HOURS, ETC MATERIAL, Forecast Start / Koniec – ICamWorkPackages.ApplyEstimate),
    /// potem status Zatwierdzona i historia; błąd zapisu słownika – estymacja zostaje wysłana. Estymacja zmieniona od wczytania
    /// (np. zwrócona i wysłana ponownie) – odmowa przed zapisem słownika, żeby do struktury nie trafiły nieaktualne wartości.
    /// </summary>
    public CamOutcome Approve(CamEstimate estimate)
    {
        if (estimate.Status != CamStatuses.Sent)
            return new CamOutcome(false, "Zatwierdzić można tylko estymację wysłaną przez CAM-a.");
        if (store.Estimate(estimate.Project, estimate.Wp, estimate.Cycle) is not { Status: CamStatuses.Sent } current || current.UpdatedAt != estimate.UpdatedAt)
            return new CamOutcome(false, $"Estymację WP {estimate.Wp} zmieniono w międzyczasie – odśwież listę i sprawdź ją ponownie.");
        if (packages() is not { } source)
            return new CamOutcome(false, "Moduł Projekty niedostępny – nie można zapisać estymacji w „Harmonogram i budżet”.");
        var hours = estimate.Total(CamLineKinds.Hours);
        var material = estimate.Total(CamLineKinds.Material);
        if (source.ApplyEstimate(estimate.Project, estimate.Wp, hours, material, estimate.Currency, estimate.ForecastStart, estimate.ForecastFinish,
                estimate.ActualStart, estimate.ActualFinish) is { } error)
            return new CamOutcome(false, $"Nie zatwierdzono – {error}");
        return Review(estimate, CamStatuses.Approved, null,
            $"zatwierdzona – „Harmonogram i budżet”: ETC HOURS {hours:0.##}, ETC MATERIAL {material:0.##} {estimate.Currency}");
    }

    /// <summary>Zwrot do CAM-a z wymaganą uwagą analityka (min. CamEstimates.JustificationLength znaków).</summary>
    public CamOutcome Return(CamEstimate estimate, string note)
    {
        if (estimate.Status != CamStatuses.Sent)
            return new CamOutcome(false, "Zwrócić można tylko estymację wysłaną przez CAM-a.");
        if (note.Trim().Length < CamEstimates.JustificationLength)
            return new CamOutcome(false, $"Podaj uwagę dla CAM-a (min. {CamEstimates.JustificationLength} znaków).");
        return Review(estimate, CamStatuses.Returned, note.Trim(), "zwrócona do CAM-a");
    }

    /// <summary>
    /// Estymacje programu w cyklu (etap przebiegu „Estymacje CAM”): wymagane WP – z CAM-em, niezakończone (bez zapisanej estymacji –
    /// Do uzupełnienia, bez wartości) i zapisane estymacje pozostałych WP (Required false).
    /// </summary>
    public CamReview Program(string project, CamCycle cycle) => Review(packages()?.ProjectWorkPackages(project) ?? [], cycle, project);

    /// <summary>Postęp estymacji cyklu we wszystkich programach (monitoring finansisty w module CAM): wymagane WP wobec zatwierdzonych.</summary>
    public CamReview Progress(CamCycle monthly, CamCycle? weekly = null)
    {
        var wps = packages()?.ProjectWorkPackages(null) ?? [];
        var byCycle = wps.GroupBy(w => w.WeeklyCycle).ToList();
        weekly ??= byCycle.Any(g => g.Key) ? Cycle(true) : null;
        return new CamReview(monthly, [.. byCycle.SelectMany(g => Review([.. g], g.Key ? weekly! : monthly, null).Items)]);
    }

    /// <summary>
    /// Powiadomienia CAM (etap przebiegu „Powiadomienie CAM”): CAM-owie z zaległymi wymaganymi estymacjami programu w cyklu (brak, szkic,
    /// zwrócona) – jedna wiadomość na CAM-a z listą WP, terminem i uwagami do zwróconych; CAM powiadomiony w cyklu – pominięty (resend –
    /// ponownie); adres ze słownika Osoby; każde powiadomienie zapisane (także brak adresu i błąd wysyłki), dziennik.
    /// </summary>
    public CamNotifyResult Notify(string project, CamCycle cycle, long? pinId, bool resend)
    {
        var review = Program(project, cycle);
        var pending = review.Items.Where(i => i.Required && i.Status is CamStatuses.ToDo or CamStatuses.Draft or CamStatuses.Returned)
            .GroupBy(i => i.Cam, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var notified = resend ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : store.Notifications(project, cycle.Period).Where(n => n.Result == CamNotificationResults.Sent).Select(n => n.Cam).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var people = Cams().ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var sent = new List<CamNotification>();
        foreach (var group in pending.Where(g => !notified.Contains(g.Key)))
        {
            var person = people.GetValueOrDefault(group.Key) ?? new CamPerson(group.Key, group.Key);
            var (subject, body) = CamEstimates.Notification(project, review.Cycle, person, [.. group]);
            var (result, message) = (CamNotificationResults.Sent, (string?)null);
            if (person.Email is null)
                (result, message) = (CamNotificationResults.NoEmail, "brak adresu e-mail w słowniku Osoby");
            else
            {
                try
                {
                    (mail ?? throw new InvalidOperationException("wysyłka e-mail niedostępna w tej aplikacji")).Send(person.Email, subject, body);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    (result, message) = (CamNotificationResults.Failed, ex.Message);
                }
            }
            var notification = new CamNotification(person.Code, person.Name, person.Email, services.Clock.Now, services.User.Account,
                string.Join(", ", group.Select(i => i.Wp)), result, message);
            store.AddNotification(project, cycle.Period, notification, pinId);
            sent.Add(notification);
        }
        if (sent.Count > 0)
            services.Journal.Add(Area, $"Powiadomienia CAM {project} (cykl {cycle.Period}): " +
                string.Join("; ", sent.Select(n => $"{n.Name} – {n.Result}{(n.Message is null ? "" : $" ({n.Message})")}")), project);
        return new CamNotifyResult(sent, [.. pending.Select(g => g.Key).Where(notified.Contains)]);
    }

    /// <summary>Powiadomienia programu w cyklu z nazwiskami CAM-ów, od najnowszego.</summary>
    public IReadOnlyList<CamNotification> Notifications(string project, string cycle)
    {
        var people = Cams().ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        return [.. store.Notifications(project, cycle).Select(n => n with { Name = people.GetValueOrDefault(n.Cam)?.Name ?? n.Cam })];
    }

    /// <summary>Zapisane estymacje programu w cyklu (kolory struktury programu) – bez listy wymaganych WP.</summary>
    public IReadOnlyList<CamReviewItem> Saved(string project, string period) =>
        store.Estimates(period, project: project).Select(e => Item(e, e.Wp, false)).ToList();

    /// <summary>Decyzja o estymacji z listy przebiegu: estymacja z bazy, a zmieniona od wczytania listy (Version) – odmowa.</summary>
    public CamOutcome Decide(CamReviewItem item, Func<CamEstimate, CamOutcome> decision) =>
        store.Estimate(item.Project, item.Wp, item.Period) is { } current && item.Version is not null && current.UpdatedAt == item.Version
            ? decision(current)
            : new CamOutcome(false, $"Estymację WP {item.Wp} zmieniono w międzyczasie albo jej nie zapisano – odśwież listę i sprawdź ją ponownie.");

    private CamReview Review(IReadOnlyList<CamWorkPackage> wps, CamCycle cycle, string? project)
    {
        var saved = store.Estimates(cycle.Period, project: project).ToDictionary(e => (e.Project.ToUpperInvariant(), e.Wp.ToUpperInvariant()));
        var items = new List<CamReviewItem>();
        foreach (var wp in wps)
        {
            var key = (wp.Project.ToUpperInvariant(), wp.Wp.ToUpperInvariant());
            items.Add(saved.Remove(key, out var stored) ? Item(stored, wp.Name, !wp.Finished)
                : new CamReviewItem(wp.Project, wp.Wp, cycle.Period, wp.Name, wp.Cam, !wp.Finished, CamStatuses.ToDo, 0, 0, wp.Currency, null, null, null,
                    null, null, null, null, null));
        }
        items.AddRange(saved.Values.Select(e => Item(e, e.Wp, false)));
        return new CamReview(cycle, items.OrderBy(i => i.Project, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Wp, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Pozycja przeglądu z zapisanej estymacji (po decyzji – z wymagalnością i nazwą pozycji z listy).</summary>
    public static CamReviewItem Item(CamEstimate e, string name, bool required) =>
        new(e.Project, e.Wp, e.Cycle, name, e.Cam, required, e.Status, e.Total(CamLineKinds.Hours), e.Total(CamLineKinds.Material), e.Currency,
            e.ForecastFinish, e.Justification, e.ReviewNote, e.SubmittedAt, e.SubmittedBy, e.ReviewedAt, e.ReviewedBy, e.EstimateId is null ? null : e.UpdatedAt);

    /// <summary>Zatwierdzone estymacje cyklu (wszyscy CAM-owie) – do ponownego otwarcia.</summary>
    public IReadOnlyList<CamEstimate> Approved(CamCycle cycle) =>
        store.Estimates(cycle.Period).Where(e => e.Status == CamStatuses.Approved).OrderByDescending(e => e.ReviewedAt).ToList();

    /// <summary>
    /// Ponowne otwarcie zatwierdzonej estymacji z wymaganym komentarzem (min. CamEstimates.JustificationLength znaków): wraca do CAM-a
    /// jako zwrócona z komentarzem jako uwagą analityka; „Harmonogram i budżet” bez zmian – zostają wartości ostatniego zatwierdzenia
    /// do ponownego zatwierdzenia poprawionej estymacji.
    /// </summary>
    public CamOutcome Reopen(CamEstimate estimate, string comment)
    {
        if (estimate.Status != CamStatuses.Approved)
            return new CamOutcome(false, "Otworzyć ponownie można tylko zatwierdzoną estymację.");
        if (comment.Trim().Length < CamEstimates.JustificationLength)
            return new CamOutcome(false, $"Podaj powód ponownego otwarcia (min. {CamEstimates.JustificationLength} znaków).");
        return Review(estimate, CamStatuses.Returned, comment.Trim(),
            "otwarta ponownie po zatwierdzeniu – „Harmonogram i budżet” bez zmian do ponownego zatwierdzenia");
    }

    public IReadOnlyList<CamEstimateEvent> History(CamEstimate estimate) => estimate.EstimateId is { } id ? store.Events(id) : [];

    /// <summary>Ostatnia zatwierdzona estymacja WP z wcześniejszego cyklu (punkt odniesienia w formularzu).</summary>
    public CamEstimate? LastApproved(CamEstimate estimate) => store.LastApproved(estimate.Project, estimate.Wp, estimate.Cycle);

    private CamOutcome Review(CamEstimate estimate, string status, string? note, string summary)
    {
        var now = services.Clock.Now;
        var user = services.User.Account;
        try
        {
            var saved = store.Save(estimate with { Status = status, ReviewNote = note ?? estimate.ReviewNote, ReviewedAt = now, ReviewedBy = user },
                new CamEstimateEvent(now, user, status, summary, note));
            services.Journal.Add(Area, $"Estymacja WP {estimate.Wp} ({estimate.Project}, CAM {estimate.Cam}): {summary}", estimate.Project);
            return new CamOutcome(true, $"Estymacja WP {estimate.Wp}: {summary}.", saved);
        }
        catch (CamConflictException ex)
        {
            return new CamOutcome(false, ex.Message);
        }
    }
}
