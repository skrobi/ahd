namespace PzlEv.Shared.Models.Dictionaries;

/// <summary>
/// Wartość do wyboru w kolumnie powiązanej z innym słownikiem (DictColumn.Lookup), np. osoba: USRID (zapisywany)
/// → imię i nazwisko (wyświetlane).
/// </summary>
public sealed record LookupOption(string Value, string Label)
{
    /// <summary>Pozycja listy wyboru: „Anna Nowak (e123456)”.</summary>
    public string Text => string.Equals(Label, Value, StringComparison.OrdinalIgnoreCase) ? Value : $"{Label} ({Value})";

    /// <summary>Wyszukiwanie w liście: fragment wartości albo opisu, bez względu na wielkość liter.</summary>
    public bool Matches(string search) =>
        search.Length == 0 || Value.Contains(search, StringComparison.OrdinalIgnoreCase) || Label.Contains(search, StringComparison.OrdinalIgnoreCase);
}
