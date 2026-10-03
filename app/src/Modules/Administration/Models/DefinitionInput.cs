namespace PzlEv.Modules.Administration.Models;

/// <summary>
/// Definicja źródła w edycji. DefinitionId / Version – edytowana wersja (null – nowa definicja); Parser – kod parsera
/// (pusty – tylko treść pliku).
/// </summary>
public sealed record DefinitionInput(
    long? DefinitionId,
    int? Version,
    string Code,
    string Prefix,
    string ReportType,
    string Parser,
    bool Active);
