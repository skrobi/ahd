namespace PzlEv.Modules.Import.Models;

/// <summary>Plik widziany w lokalizacji podczas sprawdzenia źródeł (bez importu): jak zostałby potraktowany.</summary>
public sealed record FileCheck(string Location, string FileName, long Size, DateTimeOffset Modified, string Recognition, string Note);
