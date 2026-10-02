using PzlEv.Modules.MasterData.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.MasterData.Data;

/// <summary>
/// Magazyn słowników – kontrakt przyszłych procedur i widoków dict.* (F10). Zapis jest jedną operacją:
/// wszystkie zmiany albo żadna; zmiana / usunięcie wiersza zmienionego w międzyczasie = konflikt.
/// Dane nie są usuwane – zmiana tworzy nową wersję, usunięcie zamyka bieżącą.
/// </summary>
public interface IDictionaryStore
{
    IReadOnlyList<DictionaryEntryRow> Current(string dictionary, string? project = null);

    /// <summary>Stan słownika w danym momencie (oś techniczna) – odczyt przebiegu na znacznik stanu.</summary>
    IReadOnlyList<DictionaryEntryRow> AsOf(string dictionary, DateTimeOffset moment, string? project = null);

    /// <summary>Wszystkie wersje wiersza logicznego, od najstarszej.</summary>
    IReadOnlyList<DictionaryEntryRow> History(long rowId);

    StoreResult Save(string dictionary, string? project, IReadOnlyList<RowChange> changes);
}
