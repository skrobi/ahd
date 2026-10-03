using PzlEv.Shared.Models.Sources;

namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Wersja parsera – meta.Parser (docs/zrodla-danych.md, rozdz. 2): układ pliku (kolumny → pola z typem i slotem w CAN_Row);
/// definicje źródeł wskazują parser kodem. ParserId – parser logiczny (stały między wersjami, klucz danych w CAN_Row);
/// Table – nazwa logiczna danych parsera. Zmiana pól tworzy nową wersję; tabele się nie zmieniają.
/// </summary>
public sealed record ParserRow(
    long Id,
    long ParserId,
    int Version,
    string Code,
    string Name,
    string Table,
    IReadOnlyList<ParserField> Fields,
    bool Active,
    DateTimeOffset RecordedAt,
    string RecordedBy,
    DateTimeOffset? SupersededAt,
    string? SupersededBy)
{
    /// <summary>Zajęte sloty CAN_Row, np. „T 19/40 · N 4/20 · I 2/10 · D 1/10”.</summary>
    public string SlotUsage => CanonicalSlots.Usage(Fields);
}
