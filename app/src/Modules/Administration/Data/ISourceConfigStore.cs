using PzlEv.Modules.Administration.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Administration.Data;

/// <summary>
/// Magazyn konfiguracji importu: definicje źródeł i lokalizacje RABIT z historią (osi technicznej) –
/// kontrakt przyszłych procedur meta.* (F10). Zapis zmienionej w międzyczasie wersji = konflikt (komunikat).
/// </summary>
public interface ISourceConfigStore
{
    IReadOnlyList<SourceDefinitionRow> Definitions();

    IReadOnlyList<SourceDefinitionRow> DefinitionHistory(long definitionId);

    /// <summary>Zapisuje definicję (signature – sygnatura kolumn); null = sukces, tekst = konflikt.</summary>
    string? SaveDefinition(DefinitionInput input, string signature, int parserVersion);

    /// <summary>Usuwa definicję – zamyka bieżącą wersję bez następnej (historia zostaje); null = sukces, tekst = konflikt.</summary>
    string? DeleteDefinition(long definitionId, int version);

    IReadOnlyList<SourceLocationRow> Locations();

    string? SaveLocation(LocationInput input);
}
