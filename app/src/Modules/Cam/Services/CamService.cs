using PzlEv.Modules.Cam.Data;
using PzlEv.Modules.Cam.Models;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Cam.Services;

/// <summary>WP na liście zadań cyklu: dane WP, estymacja (zapisana albo nowa z propozycji) i czego brakuje do wysłania.</summary>
public sealed record CamWorkItem(CamWorkPackage Wp, CamEstimate Estimate, IReadOnlyList<(string Item, bool Done)> Checklist);

/// <summary>Wynik operacji na estymacji: powodzenie, komunikat, zapisana estymacja, błędy walidacji.</summary>
public sealed record CamOutcome(bool Ok, string Message, CamEstimate? Estimate = null, IReadOnlyList<string>? Errors = null);

/// <summary>
/// Moduł CAM (docs/funkcjonalnosc.md; tasks/F11.2–F11.4): lista zadań cyklu, szkic i wysyłka estymacji (walidacja CamEstimates),
/// zatwierdzenie – „Harmonogram i budżet” programu przez kontrakt ICamWorkPackages – albo zwrot z uwagą; historia i dziennik.
/// </summary>
public sealed class CamService(ICamStore store, Func<ICamWorkPackages?> packages, AppServices services)
{
    private const string Area = "CAM";

    private DateOnly Today => DateOnly.FromDateTime(services.Clock.Now.LocalDateTime);

    /// <summary>Moduł Projekty niedostępny – brak WP (komunikat na ekranie).</summary>
    public bool Available => packages() is not null;

    /// <summary>Cykl dnia stanu; poza kalendarzem okresów – miesiąc kalendarzowy bez terminu.</summary>
    public CamCycle Cycle() => store.Cycle(Today) ?? new CamCycle(Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), null);

    public IReadOnlyList<CamPerson> Cams() => packages()?.Cams() ?? [];

    public IReadOnlyList<CamWorkPackage> WorkPackages(string cam) => packages()?.WorkPackages(cam) ?? [];

    /// <summary>Lista zadań cyklu: WP CAM-a z estymacją cyklu (bez zapisanej – nowa z propozycji), od wymagających działania.</summary>
    public IReadOnlyList<CamWorkItem> Worklist(IReadOnlyList<CamWorkPackage> wps, CamCycle cycle)
    {
        var saved = store.Estimates(cycle.Period).ToDictionary(e => (e.Project.ToUpperInvariant(), e.Wp.ToUpperInvariant()));
        return wps.Select(wp =>
            {
                var estimate = saved.GetValueOrDefault((wp.Project.ToUpperInvariant(), wp.Wp.ToUpperInvariant())) ?? CamEstimates.New(wp, cycle.Period, Today);
                return new CamWorkItem(wp, estimate, CamEstimates.Checklist(wp, estimate));
            })
            .OrderBy(i => CamStatuses.Order(i.Estimate.Status)).ThenBy(i => i.Wp.Project, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Wp.Wp, StringComparer.OrdinalIgnoreCase).ToList();
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
        if (source.ApplyEstimate(estimate.Project, estimate.Wp, hours, material, estimate.Currency, estimate.ForecastStart, estimate.ForecastFinish) is { } error)
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
