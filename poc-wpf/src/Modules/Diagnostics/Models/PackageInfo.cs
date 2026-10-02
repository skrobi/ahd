namespace PzlEv.Modules.Diagnostics.Models;

/// <summary>Wiersz panelu „Diagnostyka środowiska” – pakiet NuGet wczytany w runtime.</summary>
public sealed record PackageInfo(string Name, string Version, string Location);
