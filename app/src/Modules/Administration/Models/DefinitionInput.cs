namespace PzlEv.Modules.Administration.Models;

/// <summary>Definicja źródła w edycji. DefinitionId / Version – edytowana wersja (null – nowa definicja).</summary>
public sealed record DefinitionInput(
    long? DefinitionId,
    int? Version,
    string Code,
    string Prefix,
    string ReportType,
    IReadOnlyList<string> Columns,
    string Parser,
    bool Active);
