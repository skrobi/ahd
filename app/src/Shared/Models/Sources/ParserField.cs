namespace PzlEv.Shared.Models.Sources;

/// <summary>
/// Pole parsera = kolumna tabeli danych kanonicznych (CAN_&lt;Tabela&gt;) zasilana kolumną pliku. Field – nazwa kolumny
/// w bazie; Column – nazwa kolumny w pliku źródła (pusta – pole nie jest czytane z pliku, zostaje puste); Type –
/// FieldTypes; Length – najwięcej znaków tekstu; PadDigits – tekst z samych cyfr uzupełniany zerami do tej długości
/// (np. numer elementu kosztowego); Required – wartość musi być w każdym wierszu pliku.
/// </summary>
public sealed record ParserField(string Field, string Column, string Type, int? Length = null, int? PadDigits = null, bool Required = false);
