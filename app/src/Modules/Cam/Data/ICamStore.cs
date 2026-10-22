using PzlEv.Modules.Cam.Models;
using PzlEv.Shared.Models;

namespace PzlEv.Modules.Cam.Data;

/// <summary>Zapis estymacji w cyklu zmieniony w międzyczasie przez inną osobę (UpdatedAt inny niż wczytany).</summary>
public sealed class CamConflictException(string message) : Exception(message);

/// <summary>Estymacje CAM w bazie (migracja 056: CAM_Estimate, CAM_EstimateLine, CAM_EstimateEvent) i cykl z kalendarza okresów.</summary>
public interface ICamStore
{
    /// <summary>Cykl dnia: okres kalendarza okresów (dict.Calendar, globalny) i koniec jego tygodnia zamykającego; null – dzień poza kalendarzem.</summary>
    CamCycle? Cycle(DateOnly day);

    /// <summary>Termin cyklu okresu: koniec jego tygodnia zamykającego w kalendarzu okresów; null – brak.</summary>
    DateOnly? Deadline(string period);

    /// <summary>Estymacje cyklu z wierszami lat (cam null – wszystkich CAM-ów).</summary>
    IReadOnlyList<CamEstimate> Estimates(string cycle, string? cam = null);

    /// <summary>Estymacja WP programu w cyklu; null – brak.</summary>
    CamEstimate? Estimate(string project, string wp, string cycle);

    /// <summary>Ostatnia zatwierdzona estymacja WP z wcześniejszego cyklu (punkt odniesienia); null – brak.</summary>
    CamEstimate? LastApproved(string project, string wp, string beforeCycle);

    IReadOnlyList<CamEstimateEvent> Events(long estimateId);

    /// <summary>
    /// Zapis estymacji (nagłówek, wiersze lat w całości) i zdarzenia historii w jednej transakcji; nowa – wstawiana, istniejąca –
    /// tylko gdy UpdatedAt w bazie jest jak w estimate (inaczej CamConflictException). Zwraca zapisaną estymację.
    /// </summary>
    CamEstimate Save(CamEstimate estimate, CamEstimateEvent change);
}
