namespace PzlEv.Shared.Models.Sources;

/// <summary>
/// Pole parsera zasilane kolumną pliku. Field – nazwa pola (w zapytaniach i widokach); Column – nazwa kolumny w pliku
/// źródła (pusta – pole nie jest czytane z pliku, zostaje puste); Type – FieldTypes; Length – najwięcej znaków tekstu;
/// PadDigits – tekst z samych cyfr uzupełniany zerami do tej długości (np. numer elementu kosztowego); Required – wartość
/// musi być w każdym wierszu pliku; Slot – kolumna stałej tabeli danych kanonicznych CAN_Row (np. T02, N01), przydzielana
/// przy zapisie parsera na stałe (CanonicalSlots).
/// </summary>
public sealed record ParserField(string Field, string Column, string Type, int? Length = null, int? PadDigits = null, bool Required = false, string? Slot = null);
