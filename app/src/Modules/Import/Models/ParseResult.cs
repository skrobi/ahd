using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Modules.Import.Models;

/// <summary>Wiersz danych kanonicznych: numer wiersza danych w pliku i wartości pól (w kolejności Fields wyniku).</summary>
public sealed record CanonicalRow(int RowNumber, object?[] Values);

/// <summary>Wynik parsowania pliku według mapowania: zmapowane pola, wiersze (puste przy błędach), problemy, liczba błędów.</summary>
public sealed record ParseResult(IReadOnlyList<ParserField> Fields, IReadOnlyList<CanonicalRow> Rows, IReadOnlyList<Issue> Issues, int ErrorCount);
