using System.IO;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Plik zablokowany przez inny program (np. otwarty w Excelu) – zapis albo odczyt nieudany z naruszeniem udostępniania
/// (ERROR_SHARING_VIOLATION 32, ERROR_LOCK_VIOLATION 33). Zamiast komunikatu systemu – wskazówka dla użytkownika.
/// </summary>
public static class FileInUse
{
    public static bool Is(Exception ex) => ex is IOException io && (io.HResult & 0xFFFF) is 32 or 33;

    /// <summary>Komunikat dla użytkownika: plik z wyjątku jest otwarty w innym programie.</summary>
    public static string Describe(Exception ex) =>
        $"Plik jest otwarty w innym programie (np. w Excelu) – zamknij go albo wybierz inną nazwę i spróbuj ponownie. ({ex.Message})";
}
