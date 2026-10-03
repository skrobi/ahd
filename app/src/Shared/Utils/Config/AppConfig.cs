using System.IO;

namespace PzlEv.Shared.Utils.Config;

/// <summary>
/// Ustawienia środowiska z pliku pzl-ev.json obok PZL-EV.exe (docs/architektura.md, rozdz. 7–8): przełącznik Env
/// (TEST / PROD) wybiera sekcję Environments.&lt;Env&gt; – korzeń folderów, baza MS SQL i baza PZLPROD (struktura P1S,
/// tylko odczyt; null – sekcja PzlProd nieustawiona, mapowanie pokazuje komunikat).
/// </summary>
public sealed record AppConfig(string Environment, string NetworkRoot, SqlSettings Sql, SqlSettings? PzlProd = null)
{
    /// <summary>Folder wspólny RABIT: Do_importu i blokada importu (import.lock).</summary>
    public string RabitFolder => Path.Combine(NetworkRoot, "00_Global", "RABIT");

    /// <summary>Foldery projektów: Projekty\&lt;Projekt&gt;\… (docs/architektura.md, rozdz. 7).</summary>
    public string ProjectsFolder => Path.Combine(NetworkRoot, "Projekty");

    /// <summary>Folder plików RABIT pobranych ręcznie (docs/zrodla-danych.md, rozdz. 3).</summary>
    public string ImportFolder => Path.Combine(RabitFolder, "Do_importu");
}
