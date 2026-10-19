using System.Globalization;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Szablony przebiegu ↔ wiersze słowników „Definicje przebiegów”, „Etapy przebiegów”, „Elementy kontrolne etapów” (wartości
/// kanoniczne DictRow): odczyt szablonów (ekran Przebiegi, wybór przebiegu w programie) i wiersze do zapisu szablonu.
/// </summary>
public static class RunDefinitionRows
{
    public static IReadOnlyList<RunDefinitionHeaderRow> Headers(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Definicja"] is not null)
            .Select(r => new RunDefinitionHeaderRow(r["Definicja"]!, r["Typ programu"] ?? RunCheckRules.AllTypes, r["Aktywna"] == "tak", r["Opis"])).ToList();

    public static IReadOnlyList<RunStepRowData> Steps(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Definicja"] is not null && r["Etap"] is not null)
            .Select(r => new RunStepRowData(r["Definicja"]!, int.TryParse(r["Kolejność"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var no) ? no : 0,
                r["Etap"]!, r["Akcja"] ?? RunActions.Checks, r["Bramka WARNING"] ?? RunGates.Confirm, r["Zatwierdzenie"] == "tak", r["Opis"])).ToList();

    public static IReadOnlyList<RunStepElementRow> Elements(IEnumerable<DictRow> rows) =>
        rows.Where(r => r["Definicja"] is not null && r["Etap"] is not null && r["Element"] is not null)
            .Select(r => new RunStepElementRow(r["Definicja"]!, r["Etap"]!, r["Element"]!, r["Aktywny"] == "tak", r["Poziom"],
                decimal.TryParse(r["Próg"], NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold) ? threshold : null)).ToList();

    /// <summary>Wartości wiersza szablonu (bez wariantu i programu – „Każdy”, puste).</summary>
    public static IReadOnlyDictionary<string, string?> Header(RunDefinition definition, bool isActive) => new Dictionary<string, string?>
    {
        ["Definicja"] = definition.Code, ["Typ programu"] = definition.ProjectType, ["Program"] = null, ["Przebieg"] = RunCheckRules.AnyRun,
        ["Aktywna"] = YesNo(isActive), ["Opis"] = definition.Description,
    };

    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> StepValues(RunDefinition definition) =>
        definition.Steps.OrderBy(s => s.No).Select(s => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
        {
            ["Definicja"] = definition.Code, ["Kolejność"] = s.No.ToString(CultureInfo.InvariantCulture), ["Etap"] = s.Name, ["Akcja"] = s.Action,
            ["Bramka WARNING"] = s.WarningGate, ["Zatwierdzenie"] = YesNo(s.Approval), ["Opis"] = s.Description,
        }).ToList();

    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> ElementValues(RunDefinition definition) =>
        definition.Steps.OrderBy(s => s.No).SelectMany(s => s.Elements.Select(e => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
        {
            ["Definicja"] = definition.Code, ["Etap"] = s.Name, ["Element"] = e.Element, ["Aktywny"] = YesNo(e.Active),
            ["Poziom"] = e.Level is { } level ? RunCheckRules.Levels[level switch { CheckLevel.Error => 0, CheckLevel.Warning => 1, _ => 2 }] : null,
            ["Próg"] = e.Threshold is { } threshold ? PolishNumber.ToCanonical(threshold) : null, ["Opis"] = null,
        })).ToList();

    /// <summary>
    /// Stan słownika po zapisie szablonu code: wiersze innych szablonów bez zmian, wiersze szablonu – nowe wartości (wiersz o tym samym
    /// kluczu zachowuje RowId i historię); removed – wiersze szablonu, których już nie ma.
    /// </summary>
    public static (IReadOnlyList<DictRow> Working, IReadOnlyList<DictRow> Removed) Replace(DictionarySpec spec, IReadOnlyList<DictRow> current, string code,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> values)
    {
        bool Mine(DictRow r) => string.Equals(r["Definicja"]?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase);
        var mine = current.Where(Mine).ToList();
        var byKey = mine.GroupBy(r => spec.KeyOf(r.Values), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var working = current.Where(r => !Mine(r)).ToList();
        var kept = new HashSet<long>();
        foreach (var v in values)
        {
            if (byKey.TryGetValue(spec.KeyOf(v), out var existing) && existing.RowId is { } id && kept.Add(id))
                working.Add(existing with { Values = v });
            else
                working.Add(new DictRow(null, null, v));
        }
        return (working, mine.Where(r => r.RowId is not { } id || !kept.Contains(id)).ToList());
    }

    private static string YesNo(bool value) => value ? "tak" : "nie";
}
