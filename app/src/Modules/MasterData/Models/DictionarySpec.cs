using PzlEv.Shared.Models;

namespace PzlEv.Modules.MasterData.Models;

/// <summary>
/// Opis słownika: kolumny i reguły szczegółowe (docs/slowniki.md, rozdz. 5). Ten sam opis obsługuje zapis,
/// walidację, ekran i wymianę przez Excel. Validity – kolumny okresu obowiązywania (Od, Do), jeśli słownik go ma.
/// </summary>
public sealed class DictionarySpec
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<DictColumn> Columns { get; init; }

    public (string From, string To)? Validity { get; init; }

    /// <summary>Reguły szczegółowe na całym zestawie wierszy (po normalizacji wartości).</summary>
    public Func<IReadOnlyList<DictRow>, IEnumerable<Issue>>? Rules { get; init; }

    public IEnumerable<DictColumn> KeyColumns => Columns.Where(c => c.Key);

    public DictColumn? Column(string name) => Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public string KeyOf(IReadOnlyDictionary<string, string?> values) =>
        string.Join(" | ", KeyColumns.Select(c => values.TryGetValue(c.Name, out var v) ? v ?? "" : ""));
}
