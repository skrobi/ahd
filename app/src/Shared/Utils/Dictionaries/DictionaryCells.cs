using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Komórka tabeli słownika przy edycji (wspólna tabela słownika): sprawdzenie wartości od razu po wpisaniu – typ,
/// pole wymagane, lista wyboru, wartość spoza słownika powiązanego – oraz zamiana wpisanego opisu na wartość
/// (np. „Anna Nowak” → USRID). Pełna walidacja (duplikaty, reguły słownika) – przy zapisie (DictionaryValidator).
/// </summary>
public static class DictionaryCells
{
    /// <summary>Problem komórki albo null; options – wartości słownika powiązanego (null – nie wczytano).</summary>
    public static Issue? Check(DictionarySpec spec, DictColumn column, string? text, IReadOnlyList<LookupOption>? options)
    {
        if (!ValueFormat.TryNormalize(column, text, out var canonical, out var error))
            return Issue.Error(error!);
        if (canonical is null)
            return column.Required || (column.Key && !spec.EmptyKeyPartsAllowed) ? Issue.Error("pole wymagane") : null;
        if (column.Lookup is not null && options is not null && !options.Any(o => string.Equals(o.Value, canonical, StringComparison.OrdinalIgnoreCase)))
            return Issue.Warning($"'{canonical}' – brak w słowniku {LookupName(column.Lookup)}");
        return null;
    }

    /// <summary>
    /// Wartość do zapisania w komórce z wpisanego lub wklejonego tekstu: w kolumnie powiązanej – wartość pozycji,
    /// której wartość, opis albo tekst listy („Anna Nowak (e123456)”) jest równy tekstowi; inaczej tekst bez zmian.
    /// </summary>
    public static string? Resolve(string? text, IReadOnlyList<LookupOption>? options)
    {
        var cleaned = ValueFormat.Clean(text);
        if (cleaned is null || options is null)
            return cleaned;
        var match = options.FirstOrDefault(o => string.Equals(o.Value, cleaned, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault(o => string.Equals(o.Text, cleaned, StringComparison.OrdinalIgnoreCase))
            ?? Single(options.Where(o => string.Equals(o.Label, cleaned, StringComparison.OrdinalIgnoreCase)));
        return match?.Value ?? cleaned;
    }

    /// <summary>Tekst komórki: w kolumnie powiązanej – opis pozycji (imię i nazwisko), inaczej wartość.</summary>
    public static string Display(string? value, IReadOnlyList<LookupOption>? options) =>
        value is null ? "" : options?.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase))?.Label ?? value;

    /// <summary>
    /// Wiersz wklejanego bloku to nagłówki kolumn słownika od kolumny column (skopiowane z Excela razem z danymi):
    /// każda niepusta komórka równa nazwie albo dawnej nazwie kolumny (bez spacji, wielkości liter i „*”).
    /// </summary>
    public static bool IsHeaderRow(DictionarySpec spec, int column, IReadOnlyList<string> cells)
    {
        static string Norm(string s) => new string(s.Where(ch => !char.IsWhiteSpace(ch) && ch != '*').ToArray()).ToLowerInvariant();
        var matched = cells.Select((text, i) => (text, i: column + i))
            .Where(x => x.i < spec.Columns.Count && x.text.Trim().Length > 0)
            .ToList();
        return matched.Count > 0 && matched.All(x =>
            new[] { spec.Columns[x.i].Name }.Concat(spec.Columns[x.i].Aliases ?? []).Any(n => Norm(n) == Norm(x.text)));
    }

    private static LookupOption? Single(IEnumerable<LookupOption> options)
    {
        var list = options.Take(2).ToList();
        return list.Count == 1 ? list[0] : null;   // dwie osoby o tym samym nazwisku – bez zgadywania
    }

    private static string LookupName(string code) => GlobalDictionaries.All.FirstOrDefault(s => s.Code == code)?.Name ?? code;
}
