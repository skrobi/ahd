using PzlEv.Modules.Administration.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Administration.Data;

/// <summary>
/// Magazyn konfiguracji importu: definicje źródeł, parsery i lokalizacje RABIT z historią (osi technicznej) –
/// kontrakt przyszłych procedur meta.* (F10). Zapis zmienionej w międzyczasie wersji = konflikt (komunikat).
/// </summary>
public interface ISourceConfigStore
{
    IReadOnlyList<SourceDefinitionRow> Definitions();

    IReadOnlyList<SourceDefinitionRow> DefinitionHistory(long definitionId);

    /// <summary>Zapisuje definicję; null = sukces, tekst = konflikt.</summary>
    string? SaveDefinition(DefinitionInput input);

    /// <summary>Usuwa definicję – zamyka bieżącą wersję bez następnej (historia zostaje); null = sukces, tekst = konflikt.</summary>
    string? DeleteDefinition(long definitionId, int version);

    /// <summary>Bieżące wersje parserów (także nieaktywne).</summary>
    IReadOnlyList<ParserRow> Parsers();

    IReadOnlyList<ParserRow> ParserHistory(long parserId);

    /// <summary>Nowa wersja parsera (pola ze slotami); null – zapisano, inaczej powód odrzucenia (zmiana innej osoby, zajęty kod).</summary>
    string? SaveParser(ParserInput input, string table);

    IReadOnlyList<SourceLocationRow> Locations();

    string? SaveLocation(LocationInput input);
}
