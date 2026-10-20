using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>Reguły walidacji (SQL) ↔ wiersze słownika „Reguły walidacji” (wartości kanoniczne DictRow).</summary>
public static class RunRuleRows
{
    /// <summary>Reguła z wiersza; null – wiersz bez nazwy.</summary>
    public static RunRule? Rule(DictRow row) =>
        row["Nazwa"] is not { } name ? null
            : new RunRule(name.Trim(), RunRules.LevelOf(row["Poziom"]) ?? CheckLevel.Warning, row["Aktywna"] != "nie", row["Zapytanie SQL"] ?? "",
                row["Opis wyniku"], row["Opis"]);

    public static IReadOnlyList<RunRule> Rules(IEnumerable<DictRow> rows) => rows.Select(Rule).OfType<RunRule>().ToList();

    public static IReadOnlyDictionary<string, string?> Values(RunRule rule) => new Dictionary<string, string?>
    {
        ["Nazwa"] = rule.Name.Trim(), ["Opis"] = Text(rule.Description), ["Poziom"] = RunRules.LevelText(rule.Level), ["Aktywna"] = rule.IsActive ? "tak" : "nie",
        ["Zapytanie SQL"] = rule.Sql.Trim(), ["Opis wyniku"] = Text(rule.ResultTemplate),
    };

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
