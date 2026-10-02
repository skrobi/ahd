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

    IReadOnlyList<SourceLocationRow> Locations();

    string? SaveLocation(LocationInput input);
}
