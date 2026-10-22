using PzlEv.Shared.Models;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Estymacja CAM WP programu w cyklu (okres RRRR-MM; status – CamStatuses; ToDo – bez zapisanej estymacji, wartości z propozycji systemu): CAM,
/// wymagana w cyklu (WP z CAM-em, niezakończony), ETC godzin i materiałów (waluta programu), prognozowany koniec, uzasadnienie CAM,
/// uwaga analityka, kto i kiedy wysłał i ocenił; Version – znacznik wersji zapisanej estymacji do decyzji (kontrola zmiany w międzyczasie).
/// </summary>
public sealed record CamReviewItem(string Project, string Wp, string Period, string Name, string Cam, bool Required, string Status, decimal EtcHours, decimal Material,
    string? Currency, DateOnly? ForecastFinish, string? Justification, string? ReviewNote, DateTimeOffset? SubmittedAt, string? SubmittedBy,
    DateTimeOffset? ReviewedAt, string? ReviewedBy, DateTimeOffset? Version);

/// <summary>Estymacje CAM programu w cyklu z licznikami wymaganych WP: zatwierdzone, wysłane (czekają na finansistę), u CAM-a (szkic, zwrócone), brak.</summary>
public sealed record CamReview(CamCycle Cycle, IReadOnlyList<CamReviewItem> Items)
{
    public int Required => Items.Count(i => i.Required);

    public int Approved => Count(CamStatuses.Approved);

    public int Sent => Count(CamStatuses.Sent);

    public int AtCam => Count(CamStatuses.Draft) + Count(CamStatuses.Returned);

    public int Missing => Count(CamStatuses.ToDo);

    /// <summary>Termin cyklu minął (koniec tygodnia zamykającego okres).</summary>
    public bool Overdue(DateOnly today) => Cycle.Deadline is { } deadline && today > deadline;

    public string Summary =>
        $"Estymacje CAM {Cycle.Period}: zatwierdzone {Approved} z {Required} wymaganych · wysłane {Sent} · u CAM-a {AtCam} · brak {Missing}" +
        (Cycle.Deadline is { } d ? $" · termin {d:yyyy-MM-dd}" : "");

    private int Count(string status) => Items.Count(i => i.Required && i.Status == status);
}

/// <summary>Wynik decyzji finansisty o estymacji: powodzenie i komunikat.</summary>
public sealed record CamDecision(bool Ok, string Message);

/// <summary>
/// Estymacje CAM dla przebiegu (etap „Estymacje CAM” – decyzje finansisty) i struktury programu (kolory WP). Rejestruje moduł CAM
/// (AppServices.Contracts), używają moduły Przebiegi i Projekty (docs/architektura.md, rozdz. 5.3).
/// </summary>
public interface ICamReview
{
    /// <summary>Cykl dnia (okres kalendarza okresów i termin); poza kalendarzem – miesiąc kalendarzowy bez terminu.</summary>
    CamCycle Cycle(DateOnly day);

    /// <summary>Cykl okresu RRRR-MM (np. okres tygodnia przebiegu) z terminem z kalendarza okresów.</summary>
    CamCycle CycleOf(string period);

    /// <summary>Estymacje programu w cyklu: wymagane WP (z CAM-em, niezakończone – także bez estymacji) i zapisane estymacje pozostałych WP.</summary>
    CamReview Program(string project, CamCycle cycle);

    /// <summary>Zapisane estymacje programu w cyklu (bez listy wymaganych WP – Required false) – kolory struktury programu.</summary>
    IReadOnlyList<CamReviewItem> Saved(string project, string period);

    /// <summary>Zatwierdzenie wysłanej estymacji („Harmonogram i budżet” programu); estymacja zmieniona od wczytania – odmowa.</summary>
    CamDecision Approve(CamReviewItem item);

    /// <summary>Zwrot wysłanej estymacji do CAM-a z uwagą (min. 10 znaków).</summary>
    CamDecision Return(CamReviewItem item, string note);

    /// <summary>Ponowne otwarcie zatwierdzonej estymacji z komentarzem (min. 10 znaków) – wraca do CAM-a jako zwrócona.</summary>
    CamDecision Reopen(CamReviewItem item, string comment);
}
