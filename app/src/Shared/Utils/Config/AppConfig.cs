using System.IO;

namespace PzlEv.Shared.Utils.Config;

/// <summary>
/// Ustawienia środowiska z pliku pzl-ev.json obok PZL-EV.exe (docs/architektura.md, rozdz. 7–8): przełącznik Env
/// (TEST / PROD) wybiera sekcję Environments.&lt;Env&gt;. Brak pliku = wartości domyślne (AppConfigLoader).
/// </summary>
public sealed record AppConfig(
    string Environment,
    string NetworkRoot,
    DataMode DataMode,
    string InMemoryStatePath,
    SqlSettings? Sql)
{
    /// <summary>Folder wspólny RABIT: Do_importu i blokada importu (import.lock).</summary>
    public string RabitFolder => Path.Combine(NetworkRoot, "00_Global", "RABIT");

    /// <summary>Folder plików RABIT pobranych ręcznie (docs/zrodla-danych.md, rozdz. 3).</summary>
    public string ImportFolder => Path.Combine(RabitFolder, "Do_importu");
}
