namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Plik widziany w lokalizacji podczas sprawdzenia źródeł (bez importu): jak zostałby potraktowany;
/// Matches – nazwa pasuje do aktywnej definicji źródła (prefiks).
/// </summary>
public sealed record FileCheck(string Location, string FileName, long Size, DateTimeOffset Modified, string Recognition, string Note, bool Matches)
{
    /// <summary>Data modyfikacji w czasie lokalnym (do wyświetlenia).</summary>
    public DateTime ModifiedLocal => Modified.LocalDateTime;
}
