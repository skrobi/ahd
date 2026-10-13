namespace PzlEv.Shared.Models.Dictionaries;

/// <summary>
/// Kolumna słownika. Key – część klucza; PadNumericTo – numer złożony z cyfr uzupełniany zerami do tej długości
/// (np. element kosztowy: 51105550 → 0051105550); CheckSimilar – ostrzeżenie o wartościach podobnych
/// (np. „Engineering” i „engineering ”). Aliases – dawne nagłówki rozpoznawane przy wczytaniu z Excela
/// (np. „Planowany Start” dla „Baseline Start”). Lookup – kod słownika, z którego wybiera się wartość w tabeli
/// (np. CAM – słownik Osoby: zapisywany USRID, wyświetlane imię i nazwisko). IdLookup – zapisywany identyfikator
/// wiersza słownika powiązanego (RowId, np. pula narzutu CAS): w tabeli, historii i w Excelu widać tylko nazwę.
/// Decimals – miejsca dziesiętne liczby (null – 8, jak DECIMAL(28,8); np. wskaźnik narzutu CAS – 18).
/// </summary>
public sealed record DictColumn(
    string Name,
    ColumnType Type,
    bool Key = false,
    bool Required = false,
    int? PadNumericTo = null,
    IReadOnlyList<string>? Choices = null,
    bool CheckSimilar = false,
    IReadOnlyList<string>? Aliases = null,
    string? Lookup = null,
    bool IdLookup = false,
    int? Decimals = null);
