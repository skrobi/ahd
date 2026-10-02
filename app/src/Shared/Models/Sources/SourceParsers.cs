namespace PzlEv.Shared.Models.Sources;

/// <summary>
/// Parser definicji źródła: kod parsera z meta.Parser (pola i tabela danych kanonicznych – Administracja → Parsery)
/// albo brak – zapisywane są tylko wiersze surowe (docs/zrodla-danych.md, rozdz. 2).
/// </summary>
public static class SourceParsers
{
    /// <summary>Brak parsera – zapisywane są tylko wiersze surowe.</summary>
    public const string None = "";
}
