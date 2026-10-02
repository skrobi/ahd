using PzlEv.Shared.Models.Sources;

namespace PzlEv.Shared.Models.Db;

/// <summary>
/// Wersja parsera – meta.Parser (docs/zrodla-danych.md, rozdz. 2). Parser = tabela danych kanonicznych
/// (can.&lt;Table&gt; → CAN_&lt;Table&gt;) i jej pola; definicje źródeł wskazują parser kodem i mapują na jego pola kolumny pliku.
/// ParserId – parser logiczny (stały między wersjami); zmiana pól tworzy nową wersję i dokłada kolumny tabeli.
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
    /// <summary>Nazwa logiczna tabeli parsera (SqlDatabase.Table).</summary>
    public string LogicalTable => $"can.{Table}";
}
