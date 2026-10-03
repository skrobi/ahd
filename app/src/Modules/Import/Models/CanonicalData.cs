using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Dane kanoniczne pliku do zapisu w CAN_Row w jednym przebiegu pliku: parser, pola czytane z pliku (sloty), wiersze
/// (wartości w kolejności Fields) czytane w trakcie zapisu i Totals – wywoływane po przesłaniu wierszy: liczba wierszy
/// i sumy pól liczbowych odczytane z pliku albo ContentRejectedException (błędy wartości, niezgodna kontrola przepływu
/// pliku) – zapis jest wtedy wycofany. Po zapisie baza liczy wiersze i sumy ponownie (kontrola przepływu w bazie).
/// </summary>
public sealed record CanonicalData(
    ParserRow Parser,
    IReadOnlyList<ParserField> Fields,
    IEnumerable<CanonicalRow> Rows,
    Func<CanonicalTotals> Totals);

/// <summary>Liczba wierszy i sumy pól liczbowych pliku odczytane w przebiegu pliku (oczekiwane w bazie).</summary>
public sealed record CanonicalTotals(int Rows, IReadOnlyDictionary<string, decimal> Sums);

/// <summary>Zapisana wersja pliku: identyfikator, liczba wierszy danych kanonicznych i ich sumy policzone w bazie.</summary>
public sealed record StoredFile(long FileId, int CanonicalRows, IReadOnlyDictionary<string, decimal> Sums);

/// <summary>Kontrola przepływu w bazie: dane kanoniczne po zapisie niezgodne z odczytanymi z pliku – zapis wycofany.</summary>
public sealed class CanonicalFlowException(string message) : Exception(message);

/// <summary>Treść pliku odrzucona po odczycie (błędy wartości, niezgodne sumy) – zapis wycofany; szczegóły zna wywołujący.</summary>
public sealed class ContentRejectedException(string message) : Exception(message);
