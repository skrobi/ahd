namespace PzlEv.Modules.Import.Models;

/// <summary>Miejsce, z którego import czyta pliki: aktywna lokalizacja RABIT albo folder Do_importu.</summary>
public sealed record ImportLocation(string Name, string Path, bool IsManualFolder);
