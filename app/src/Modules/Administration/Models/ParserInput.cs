using PzlEv.Shared.Models.Sources;

namespace PzlEv.Modules.Administration.Models;

/// <summary>Parser w edycji. ParserId / Version – edytowana wersja (null – nowy parser; tabela CAN_&lt;Kod&gt;).</summary>
public sealed record ParserInput(
    long? ParserId,
    int? Version,
    string Code,
    string Name,
    IReadOnlyList<ParserField> Fields,
    bool Active);
