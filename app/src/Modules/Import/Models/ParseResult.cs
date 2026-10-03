using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Modules.Import.Models;

/// <summary>Wiersz danych kanonicznych: numer wiersza danych w pliku i wartości pól (w kolejności Fields wyniku).</summary>
public sealed record CanonicalRow(int RowNumber, object?[] Values);

/// <summary>
/// Wynik parsowania pliku w pamięci (małe pliki, testy): pola czytane z pliku, wiersze (puste przy błędach), problemy,
/// liczba błędów, kolumny parsera, których nie ma w pliku (błąd układu), i kolumny pliku, których parser nie czyta
/// (nie są zapisywane). Import czyta strumieniowo (RowMapper).
/// </summary>
public sealed record ParseResult(
    IReadOnlyList<ParserField> Fields,
    IReadOnlyList<CanonicalRow> Rows,
    IReadOnlyList<Issue> Issues,
    int ErrorCount,
    IReadOnlyList<string> MissingColumns,
    IReadOnlyList<string> ExtraColumns);
