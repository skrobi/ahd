namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Wersja wiersza słownika – dict.Entry (docs/slowniki.md, rozdz. 1; docs/model-danych.md, rozdz. 3).
/// RowId to wiersz logiczny (stały między wersjami); zmiana tworzy nową wersję, a poprzednia dostaje
/// SupersededAt / SupersededBy. Wersja zastąpiona bez następcy = wiersz usunięty. Values: kolumna → wartość
/// w zapisie kanonicznym (liczby z kropką, daty RRRR-MM-DD, tak / nie).
/// </summary>
public sealed record DictionaryEntryRow(
    long Id,
    long RowId,
    int Version,
    string Dictionary,
    string? Project,
    string Key,
    IReadOnlyDictionary<string, string?> Values,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    DateTimeOffset? SupersededAt,
    string? SupersededBy)
{
    public bool IsCurrent => SupersededAt is null;

    /// <summary>Czy wersja obowiązywała w danym momencie (oś techniczna – odczyt przebiegu na znacznik stanu).</summary>
    public bool ValidAt(DateTimeOffset moment) => RecordedAt <= moment && (SupersededAt is null || SupersededAt > moment);
}
