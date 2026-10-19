using System.Globalization;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>Reguły walidacji ↔ wiersze słownika „Reguły walidacji” (wartości kanoniczne DictRow).</summary>
public static class RunValidationRows
{
    /// <summary>Reguła z wiersza; null – wiersz bez nazwy.</summary>
    public static RunValidationRule? Rule(DictRow row) =>
        row["Nazwa"] is not { } name ? null
            : new RunValidationRule(name.Trim(), row["Kontrola"] ?? RunValidationRules.Area, row["Aktywna"] != "nie", row["Filtr"], row["Grupuj"],
                row["Warunek"] ?? "", row["Kwota"], row["Komunikat"] ?? "", row["Komunikat OK"],
                int.TryParse(row["Najwyżej"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var top) ? top : 0,
                RunCheckRules.LevelOf(row["Poziom"]) ?? CheckLevel.Warning,
                decimal.TryParse(row["Próg"], NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold) ? threshold : null, row["Opis"]);

    public static IReadOnlyList<RunValidationRule> Rules(IEnumerable<DictRow> rows) => rows.Select(Rule).OfType<RunValidationRule>().ToList();

    public static IReadOnlyDictionary<string, string?> Values(RunValidationRule rule) => new Dictionary<string, string?>
    {
        ["Nazwa"] = rule.Name, ["Kontrola"] = rule.Check, ["Aktywna"] = rule.IsActive ? "tak" : "nie", ["Filtr"] = Text(rule.Filter), ["Grupuj"] = Text(rule.GroupBy),
        ["Warunek"] = rule.Condition, ["Kwota"] = Text(rule.Amount), ["Komunikat"] = rule.Message, ["Komunikat OK"] = Text(rule.OkMessage),
        ["Najwyżej"] = rule.Top.ToString(CultureInfo.InvariantCulture),
        ["Poziom"] = RunCheckRules.Levels[rule.Level switch { CheckLevel.Error => 0, CheckLevel.Warning => 1, _ => 2 }],
        ["Próg"] = rule.Threshold is { } t ? PolishNumber.ToCanonical(t) : null, ["Opis"] = Text(rule.Description),
    };

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
