namespace PzlEv.Shared.Models.Sources;

/// <summary>
/// Parser definicji źródła: kod parsera z meta.Parser (pola i tabela danych kanonicznych – Administracja → Parsery)
/// albo brak – zapisywana jest tylko treść pliku (docs/zrodla-danych.md, rozdz. 2).
/// </summary>
public static class SourceParsers
{
    /// <summary>Brak parsera – zapisywana jest tylko treść pliku.</summary>
    public const string None = "";
}
