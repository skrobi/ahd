namespace PzlEv.Modules.Administration.Models;

/// <summary>Lokalizacja RABIT w edycji. LocationId / Version – edytowana wersja (null – nowa lokalizacja).</summary>
public sealed record LocationInput(long? LocationId, int? Version, string Name, string Path, bool Active);
