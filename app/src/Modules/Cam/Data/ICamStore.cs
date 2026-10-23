using PzlEv.Modules.Cam.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Cam.Data;

/// <summary>Zapis estymacji w cyklu zmieniony w międzyczasie przez inną osobę (UpdatedAt inny niż wczytany).</summary>
public sealed class CamConflictException(string message) : Exception(message);

/// <summary>Estymacje CAM w bazie (migracja 056: CAM_Estimate, CAM_EstimateLine, CAM_EstimateEvent) i cykl z kalendarza okresów.</summary>
public interface ICamStore
{
    /// <summary>
    /// Cykl dnia z kalendarza okresów (dict.Calendar, globalny): miesięczny – okres i koniec jego tygodnia zamykającego; tygodniowy – tydzień
    /// RRRR-Tnn i jego koniec; null – dzień poza kalendarzem.
    /// </summary>
    CamCycle? Cycle(DateOnly day, bool weekly = false);

    /// <summary>Termin cyklu: okres – koniec jego tygodnia zamykającego, tydzień RRRR-Tnn – koniec tygodnia; null – brak w kalendarzu.</summary>
    DateOnly? Deadline(string code);

    /// <summary>Estymacje cyklu z wierszami lat (cam null – wszystkich CAM-ów, project null – wszystkich programów).</summary>
    IReadOnlyList<CamEstimate> Estimates(string cycle, string? cam = null, string? project = null);

    /// <summary>Estymacja WP programu w cyklu; null – brak.</summary>
    CamEstimate? Estimate(string project, string wp, string cycle);

    /// <summary>Ostatnio zatwierdzona estymacja WP z innego cyklu (punkt odniesienia, start tygodniowej estymacji); null – brak.</summary>
    CamEstimate? LastApproved(string project, string wp, string beforeCycle);

    /// <summary>Powiadomienia CAM programu w cyklu, od najnowszego.</summary>
    IReadOnlyList<CamNotification> Notifications(string project, string cycle);

    /// <summary>Zapis powiadomienia CAM (pinId – uruchomienie przebiegu; null – wysłane z panelu „Wyślij ponownie” poza wykonaniem etapu).</summary>
    void AddNotification(string project, string cycle, CamNotification notification, long? pinId);

    IReadOnlyList<CamEstimateEvent> Events(long estimateId);

    /// <summary>
    /// Zapis estymacji (nagłówek, wiersze lat w całości) i zdarzenia historii w jednej transakcji; nowa – wstawiana, istniejąca –
    /// tylko gdy UpdatedAt w bazie jest jak w estimate (inaczej CamConflictException). Zwraca zapisaną estymację.
    /// </summary>
    CamEstimate Save(CamEstimate estimate, CamEstimateEvent change);
}
