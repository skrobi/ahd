namespace PzlEv.Shared.Models.Dictionaries;

/// <summary>
/// Kolumna słownika. Key – część klucza; PadNumericTo – numer złożony z cyfr uzupełniany zerami do tej długości
/// (np. element kosztowy: 51105550 → 0051105550); CheckSimilar – ostrzeżenie o wartościach podobnych
/// (np. „Engineering” i „engineering ”). Aliases – dawne nagłówki rozpoznawane przy wczytaniu z Excela
/// (np. „Planowany Start” dla „Baseline Start”).
/// </summary>
public sealed record DictColumn(
    string Name,
    ColumnType Type,
    bool Key = false,
    bool Required = false,
    int? PadNumericTo = null,
    IReadOnlyList<string>? Choices = null,
    bool CheckSimilar = false,
    IReadOnlyList<string>? Aliases = null);
