using PzlEv.Shared.Models.Mapping;

namespace PzlEv.Shared.Utils.Mapping;

/// <summary>
/// Dane mapowania w bazie PZL-EV: raport mapowań (najnowszy zaimportowany plik parsera MAPOWANIA), elementy CES
/// z danych kanonicznych ACTUALS i korekty z historią (dict.MappingCorrection).
/// </summary>
public interface IMappingStore
{
    /// <summary>Najnowszy plik raportu mapowań z danymi kanonicznymi; null – raportu jeszcze nie zaimportowano.</summary>
    ReportInfo? LatestReport();

    IReadOnlyList<CesElement> CesElements();

    /// <summary>Korekty obowiązujące (bieżąca wersja, bez daty zamknięcia).</summary>
    IReadOnlyList<CorrectionRow> ActiveCorrections();

    /// <summary>Wszystkie wersje korekt elementu albo projektu CES (od najstarszej).</summary>
    IReadOnlyList<CorrectionRow> History(string kind, string cesKey);

    /// <summary>Nowa korekta albo nowa wersja bieżącej; null – zapisano, inaczej powód odrzucenia (zmiana innej osoby).</summary>
    string? SaveCorrection(CorrectionInput input, string targetWbs, string? previousTarget);

    /// <summary>Usunięcie korekty: nowa wersja z datą zamknięcia (ValidTo); null – zapisano, inaczej powód odrzucenia.</summary>
    string? CloseCorrection(long rowId, int expectedVersion);
}
