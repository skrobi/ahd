using PzlEv.Shared.Models.Mapping;

namespace PzlEv.Shared.Utils.Mapping;

/// <summary>
/// Dane mapowania w bazie PZL-EV: raport mapowań (słownik globalny „Raport mapowań CES ↔ P1S”), elementy CES
/// z danych kanonicznych ACTUALS i korekty z historią (dict.MappingCorrection).
/// </summary>
public interface IMappingStore
{
    /// <summary>Bieżąca zawartość słownika raportu mapowań; null – słownik jest pusty.</summary>
    ReportInfo? Report();

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
