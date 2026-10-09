using System.Globalization;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Walidacja przy zapisie (docs/slowniki.md, rozdz. 5.1) – wspólna dla edycji w aplikacji i wczytania z Excela:
/// typy i wartości wymagane (ERROR), duplikat klucza (ERROR), wartości podobne (WARNING), okresy obowiązywania
/// (ERROR) i reguły szczegółowe słownika.
/// </summary>
public static class DictionaryValidator
{
    /// <summary>Normalizuje wiersze; błędy typów trafiają do issues. Element: „wiersz N, kolumna”.</summary>
    public static List<DictRow> Normalize(DictionarySpec spec, IReadOnlyList<DictRow> rows, List<Issue> issues)
    {
        var result = new List<DictRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var values = new Dictionary<string, string?>();
            foreach (var column in spec.Columns)
            {
                if (!ValueFormat.TryNormalize(column, rows[i][column.Name], out var canonical, out var error))
                {
                    issues.Add(Issue.Error($"{column.Name}: {error}", RowElement(i)));
                    canonical = ValueFormat.Clean(rows[i][column.Name]);
                }
                values[column.Name] = canonical;
            }
            result.Add(rows[i] with { Values = values });
        }
        return result;
    }

    public static List<Issue> Validate(DictionarySpec spec, IReadOnlyList<DictRow> rows)
    {
        var issues = new List<Issue>();

        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var column in spec.Columns.Where(c => (c.Required || (c.Key && !spec.EmptyKeyPartsAllowed)) && rows[i][c.Name] is null))
                issues.Add(Issue.Error($"{column.Name}: pole wymagane", RowElement(i)));
        }

        // Klucz bez względu na wielkość liter: „e123” i „E123” to ten sam wiersz.
        foreach (var group in rows.Select((r, i) => (Key: spec.KeyOf(r.Values), Index: i)).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var lines = string.Join(", ", group.Select(x => x.Index + 1));
            issues.Add(Issue.Error($"Duplikat klucza {group.Key} (wiersze {lines})", "klucz"));
        }

        foreach (var column in spec.Columns.Where(c => c.CheckSimilar || c.Key))
        {
            var similar = rows
                .Select(r => r[column.Name])
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .GroupBy(Similarity)
                .Where(g => g.Count() > 1);
            foreach (var group in similar)
                issues.Add(Issue.Warning($"Wartości podobne: {string.Join(", ", group.Select(v => $"'{v}'"))} – czy to ta sama wartość?", column.Name));
        }

        if (spec.Validity is { } validity)
            issues.AddRange(ValidateValidity(spec, rows, validity.From, validity.To));

        if (spec.Rules is not null)
            issues.AddRange(spec.Rules(rows));

        return issues;
    }

    public static string RowElement(int index) => $"wiersz {index + 1}";

    private static IEnumerable<Issue> ValidateValidity(DictionarySpec spec, IReadOnlyList<DictRow> rows, string fromColumn, string toColumn)
    {
        var periods = new List<(string Key, DateOnly From, DateOnly To, int Index)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var from = Parse(rows[i][fromColumn]) ?? DateOnly.MinValue;
            var to = Parse(rows[i][toColumn]) ?? DateOnly.MaxValue;
            if (from > to)
                yield return Issue.Error($"{fromColumn} jest późniejsze niż {toColumn}", RowElement(i));
            else
                periods.Add((KeyWithoutValidity(spec, rows[i], fromColumn), from, to, i));
        }
        foreach (var group in periods.GroupBy(p => p.Key))
        {
            var ordered = group.OrderBy(p => p.From).ToList();
            for (var j = 1; j < ordered.Count; j++)
            {
                if (ordered[j].From <= ordered[j - 1].To)
                    yield return Issue.Error($"Okres obowiązywania nakłada się z wierszem {ordered[j - 1].Index + 1}", RowElement(ordered[j].Index));
            }
        }
    }

    private static string KeyWithoutValidity(DictionarySpec spec, DictRow row, string fromColumn) =>
        string.Join(" | ", spec.KeyColumns.Where(c => c.Name != fromColumn).Select(c => row[c.Name] ?? ""));

    private static DateOnly? Parse(string? canonical) =>
        canonical is not null && DateOnly.TryParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string Similarity(string value) =>
        new string(value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && !char.IsWhiteSpace(ch))
            .ToArray()).ToLowerInvariant();
}
