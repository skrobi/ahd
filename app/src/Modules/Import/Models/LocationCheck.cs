namespace PzlEv.Modules.Import.Models;

/// <summary>Wynik sprawdzenia lokalizacji: czy dostępna, ile plików i podfolderów, błąd dostępu.</summary>
public sealed record LocationCheck(string Name, string ConfiguredPath, string Path, bool Accessible, string Status);
