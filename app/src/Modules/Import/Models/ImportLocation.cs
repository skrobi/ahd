namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Miejsce, z którego import czyta pliki: aktywna lokalizacja RABIT albo folder Do_importu.
/// Path – ścieżka faktycznie czytana (link SharePoint zamieniony na WebDAV); ConfiguredPath – jak zapisano.
/// </summary>
public sealed record ImportLocation(string Name, string Path, bool IsManualFolder, string ConfiguredPath);
