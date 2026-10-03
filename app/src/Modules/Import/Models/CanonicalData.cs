using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Dane kanoniczne pliku do zapisu w CAN_Row (strumieniowo): parser, pola czytane z pliku (sloty), wiersze (wartości
/// w kolejności Fields) oraz oczekiwana liczba wierszy i sumy pól liczbowych z pierwszego przebiegu – po zapisie baza
/// liczy je ponownie (kontrola przepływu), niezgodność wycofuje zapis pliku.
/// </summary>
public sealed record CanonicalData(
    ParserRow Parser,
    IReadOnlyList<ParserField> Fields,
    IEnumerable<CanonicalRow> Rows,
    int ExpectedRows,
    IReadOnlyDictionary<string, decimal> ExpectedSums);

/// <summary>Zapisana wersja pliku: identyfikator, liczba wierszy danych kanonicznych i ich sumy policzone w bazie.</summary>
public sealed record StoredFile(long FileId, int CanonicalRows, IReadOnlyDictionary<string, decimal> Sums);

/// <summary>Kontrola przepływu w bazie: dane kanoniczne po zapisie niezgodne z odczytanymi z pliku – zapis wycofany.</summary>
public sealed class CanonicalFlowException(string message) : Exception(message);
