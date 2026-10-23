using PzlEv.Shared.Models;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Estymacja CAM WP programu w cyklu (kod cyklu – okres RRRR-MM albo tydzień RRRR-Tnn; status – CamStatuses; ToDo – bez zapisanej estymacji): CAM,
/// wymagana w cyklu (WP z CAM-em, niezakończony), ETC godzin i materiałów (waluta programu), prognozowany koniec, uzasadnienie CAM,
/// uwaga analityka, kto i kiedy wysłał i ocenił (ToDo – wartości 0); Version – znacznik wersji zapisanej estymacji do decyzji (kontrola zmiany w międzyczasie).
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

/// <summary>Wynik powiadomienia CAM: wysłano, brak adresu (słownik Osoby, kolumna E-mail), błąd wysyłki.</summary>
public static class CamNotificationResults
{
    public const string Sent = "wysłano";
    public const string NoEmail = "brak adresu";
    public const string Failed = "błąd";
}

/// <summary>Powiadomienie CAM w cyklu: CAM, nazwisko, adres, kiedy i kto wysłał, WP w wiadomości, wynik (CamNotificationResults), błąd.</summary>
public sealed record CamNotification(string Cam, string Name, string? Email, DateTimeOffset SentAt, string SentBy, string Wps, string Result, string? Message);

/// <summary>Wynik etapu powiadomień: powiadomienia tej akcji i CAM-owie z zaległościami pominięci (powiadomieni wcześniej w cyklu).</summary>
public sealed record CamNotifyResult(IReadOnlyList<CamNotification> Notifications, IReadOnlyList<string> AlreadyNotified);

/// <summary>Wynik decyzji finansisty o estymacji: powodzenie, komunikat i pozycja po decyzji (lista bez ponownego odczytu programu).</summary>
public sealed record CamDecision(bool Ok, string Message, CamReviewItem? Item = null);

/// <summary>
/// Estymacje CAM dla przebiegu (etap „Estymacje CAM” – decyzje finansisty) i struktury programu (kolory WP). Rejestruje moduł CAM
/// (AppServices.Contracts), używają moduły Przebiegi i Projekty (docs/architektura.md, rozdz. 5.3).
/// </summary>
public interface ICamReview
{
    /// <summary>
    /// Cykl dnia: miesięczny – okres kalendarza okresów i termin (koniec tygodnia zamykającego), tygodniowy – tydzień kalendarza i jego koniec;
    /// poza kalendarzem – miesiąc kalendarzowy bez terminu.
    /// </summary>
    CamCycle Cycle(DateOnly day, bool weekly = false);

    /// <summary>Cykl kodu RRRR-MM albo RRRR-Tnn (okres albo tydzień przebiegu) z terminem z kalendarza okresów.</summary>
    CamCycle CycleOf(string code);

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

    /// <summary>
    /// E-mail do CAM-ów z zaległymi wymaganymi estymacjami programu w cyklu (brak, szkic, zwrócona): jedna wiadomość na CAM-a z listą WP,
    /// terminem i uwagami; CAM powiadomiony w cyklu – pominięty, chyba że resend; zapis każdego powiadomienia (pinId – uruchomienie przebiegu).
    /// </summary>
    CamNotifyResult Notify(string project, CamCycle cycle, long? pinId, bool resend);

    /// <summary>Powiadomienia programu w cyklu, od najnowszego.</summary>
    IReadOnlyList<CamNotification> Notifications(string project, string cycle);
}
