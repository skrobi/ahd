namespace PzlEv.Modules.Import.Models;

/// <summary>Wynik importu jednego pliku (wiersz ekranu Import).</summary>
public sealed record FileResult(string Location, string FileName, string Decision, string? SourceCode, int? Rows, string Description);
