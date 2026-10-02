using System.IO;

namespace PzlEv.Shared.Utils.Config;

/// <summary>
/// Ustawienia środowiska z pliku pzl-ev.json obok PZL-EV.exe (docs/architektura.md, rozdz. 7–8).
/// Brak pliku = wartości domyślne (AppConfigLoader).
/// </summary>
public sealed record AppConfig(
    string Environment,
    string NetworkRoot,
    DataMode DataMode,
    string InMemoryStatePath,
    string? ConnectionString)
{
    /// <summary>Folder plików RABIT pobranych ręcznie (docs/zrodla-danych.md, rozdz. 3).</summary>
    public string ImportFolder => Path.Combine(NetworkRoot, "00_Global", "RABIT", "Do_importu");
}
