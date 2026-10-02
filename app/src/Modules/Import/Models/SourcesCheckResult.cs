namespace PzlEv.Modules.Import.Models;

/// <summary>Sprawdzenie źródeł: stan lokalizacji i pliki, które import by zobaczył.</summary>
public sealed record SourcesCheckResult(IReadOnlyList<LocationCheck> Locations, IReadOnlyList<FileCheck> Files);
