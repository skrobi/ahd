namespace PzlEv.Shared.Models.Sources;

/// <summary>
/// Pole danych kanonicznych parsera = kolumna tabeli parsera (CAN_&lt;Tabela&gt;). Field – nazwa kolumny w bazie;
/// Label – nazwa kolumny w pliku źródła podpowiadana przy mapowaniu („Mapuj po nazwach”); Type – FieldTypes;
/// Length – najwięcej znaków tekstu; PadDigits – tekst złożony z samych cyfr uzupełniany zerami do tej długości
/// (np. numer elementu kosztowego do 10 znaków).
/// </summary>
public sealed record ParserField(string Field, string Label, string Type, int? Length = null, int? PadDigits = null);
