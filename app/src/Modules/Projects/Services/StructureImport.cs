using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Services;

/// <summary>Zmiany z pliku struktury: wiersze do zapisu (jak edycja w tabeli struktury) i problemy pliku.</summary>
public sealed record StructureImportResult(IReadOnlyList<StructureEdit> Edits, IReadOnlyList<Issue> Issues, int FileRows)
{
    public int Count(string column) => Edits.Count(e => e.Changes.ContainsKey(column));
}

/// <summary>
/// „Wczytaj strukturę z Excela” (docs/performance-objectives.md, rozdz. 4.2): arkusz „Struktura” pobrany z aplikacji
/// i uzupełniony ręcznie. Wiersz pliku → wiersz struktury po kodzie P1S („Legacy Element (P1S)”). Czytane kolumny
/// (wyróżnione w eksporcie – Inputs): WP (niepuste – element jest WP; kod WP = kod elementu), CAM (USRID), Cost Category;
/// z wierszy z WP – Baseline Start / Finish, BAC Hours baseline, BAC Material, BAC Cost, Actual Start / Finish (nadpisanie
/// dat z produkcji; data równa dacie z produkcji – bez nadpisania). Pusta komórka – bez zmiany;
/// pozostałe kolumny (sumy poddrzewa, wartości z AHD i ACTUALS) są pomijane. Zmiany zapisuje ProjectService.SaveStructureEdits.
/// </summary>
public static class StructureImport
{
    public const string P1sHeader = "Legacy Element (P1S)";
    public const string WpHeader = "WP";
    public const string CamHeader = "CAM";
    public const string CategoryHeader = "Cost Category";
    public const string StartHeader = "Baseline Start";
    public const string FinishHeader = "Baseline Finish";
    public const string BacHoursHeader = "BAC Hours baseline";
    public const string BacMaterialHeader = "BAC Material";
    public const string BacHeader = "BAC Cost";
    public const string ActualStartHeader = "Actual Start";
    public const string ActualFinishHeader = "Actual Finish";

    /// <summary>Kolumny eksportu czytane przy wczytaniu (wyróżnione w arkuszu).</summary>
    public static IReadOnlySet<string> Inputs { get; } = new HashSet<string>
    {
        WpHeader, CamHeader, CategoryHeader, StartHeader, FinishHeader, BacHoursHeader, BacMaterialHeader, BacHeader, ActualStartHeader, ActualFinishHeader,
    };

    private static readonly (string Header, string Column, bool Date)[] Budget =
    [
        (BacHoursHeader, StructureEdits.BacHours, false), (BacMaterialHeader, StructureEdits.BacMaterial, false), (BacHeader, StructureEdits.Bac, false),
        (StartHeader, StructureEdits.Start, true), (FinishHeader, StructureEdits.Finish, true),
        (ActualStartHeader, StructureEdits.ActualStart, true), (ActualFinishHeader, StructureEdits.ActualFinish, true),
    ];

    /// <param name="schedule">Wiersze „Harmonogram i budżet” projektu – wartości bieżące (bez zmiany – bez zapisu).</param>
    public static StructureImportResult Read(TabularData data, ProjectStructure structure, IReadOnlyList<DictRow> schedule)
    {
        var issues = new List<Issue>();
        int Column(string header) => data.Headers.ToList().FindIndex(h => string.Equals(h.Trim(), header, StringComparison.OrdinalIgnoreCase));
        var p1sColumn = Column(P1sHeader);
        if (p1sColumn < 0)
            return new StructureImportResult([], [Issue.Error($"Plik nie ma kolumny „{P1sHeader}” – wczytaj arkusz „Struktura” pobrany z aplikacji")], data.Rows.Count);
        var columns = Inputs.ToDictionary(h => h, Column);
        var rows = structure.Rows.Where(r => r.P1s is not null).GroupBy(r => MappingKeys.Key(r.P1s)).ToDictionary(g => g.Key, g => g.First());
        var current = schedule.Where(r => r["WP"] is not null).GroupBy(r => r["WP"]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var edits = new Dictionary<string, (StructureRow Row, Dictionary<string, string?> Changes, string Id)>(StringComparer.Ordinal);
        var budgets = new Dictionary<string, (string Element, Dictionary<string, string?> Values)>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < data.Rows.Count; i++)
        {
            var cells = data.Rows[i];
            var at = $"wiersz {data.RowNumbers?[i] ?? i + 2}";
            string? Cell(string header) => columns[header] is var c and >= 0 && c < cells.Length && !string.IsNullOrWhiteSpace(cells[c]) ? cells[c]!.Trim() : null;
            var p1s = p1sColumn < cells.Length ? cells[p1sColumn]?.Trim() : null;
            if (string.IsNullOrEmpty(p1s))
                continue;   // węzeł wirtualny bez kodu, wiersz pusty
            if (!rows.TryGetValue(MappingKeys.Key(p1s), out var row))
            {
                if (Inputs.Any(h => Cell(h) is not null))
                    issues.Add(Issue.Warning($"Elementu P1S {p1s} nie ma w strukturze projektu – wiersz pominięty", at));
                continue;
            }
            var changes = new Dictionary<string, string?>();
            var wp = row.Wp;
            if (Cell(WpHeader) is { } wpText && row.Wp is null)
            {
                changes[StructureEdits.Wp] = "true";
                wp = p1s;
                if (!StructureEdits.IsChecked(wpText) && !wpText.Equals("x", StringComparison.OrdinalIgnoreCase) && MappingKeys.Key(wpText) != MappingKeys.Key(p1s))
                    issues.Add(Issue.Warning($"WP „{wpText}” przy elemencie {p1s} – kod WP to kod elementu ({p1s})", at));
            }
            if (Cell(CamHeader) is { } cam && !string.Equals(cam, row.Cam, StringComparison.OrdinalIgnoreCase))
                changes[StructureEdits.Cam] = cam;
            if (Cell(CategoryHeader) is { } category && !string.Equals(category, row.CostCategory, StringComparison.OrdinalIgnoreCase))
                changes[StructureEdits.CostCategory] = category;
            if ((changes.ContainsKey(StructureEdits.Cam) || changes.ContainsKey(StructureEdits.CostCategory)) && row.Wp is null && wp is null)
                wp = p1s;   // CAM / Cost Category zaznacza WP (StructureEdits.WpCam)

            var values = new Dictionary<string, string?>();
            foreach (var (header, column, date) in Budget)
            {
                if (Cell(header) is not { } text)
                    continue;
                if (wp is null)
                {
                    issues.Add(Issue.Warning($"{header} przy elemencie {p1s} bez WP – pominięte (budżet i daty podaje się w wierszu z WP)", at));
                    continue;
                }
                string? canonical = date
                    ? DateText.TryParse(text, out var d) ? DateText.ToCanonical(d) : null
                    : PolishNumber.TryParse(text, out var n) ? PolishNumber.ToCanonical(n) : null;
                if (canonical is null)
                    issues.Add(Issue.Error($"{header} „{text}” przy elemencie {p1s} – {(date ? "data RRRR-MM-DD" : "liczba")}; komórka pominięta", at));
                else
                    values[column] = canonical;
            }
            if (values.Count > 0 && wp is not null)
            {
                if (budgets.TryGetValue(wp, out var seen))
                {
                    if (values.Any(v => seen.Values.TryGetValue(v.Key, out var other) && other != v.Value))
                        issues.Add(Issue.Error($"WP {wp}: inny budżet lub daty niż w wierszu elementu {seen.Element} – pominięte", at));
                }
                else
                {
                    budgets[wp] = (p1s, values);
                    current.TryGetValue(wp, out var existing);
                    foreach (var (column, value) in values)
                    {
                        var stored = existing?[StructureEdits.DictionaryColumn(column)];
                        // Data rzeczywista równa dacie z produkcji (eksport pokazuje ją w kolumnie) – bez nadpisania.
                        var produced = stored is null && row.BudgetWp == wp ? column switch
                        {
                            StructureEdits.ActualStart => row.ActualStart,
                            StructureEdits.ActualFinish => row.ActualFinish,
                            _ => null,
                        } : null;
                        if (!SameValue(stored, value) && produced != value)
                            changes[column] = value;
                    }
                }
            }
            if (changes.Count > 0)
                edits[row.Id] = (row, changes, at);
        }
        return new StructureImportResult(edits.Values.Select(e => new StructureEdit(e.Id, e.Row, e.Changes)).ToList(), issues, data.Rows.Count);
    }

    private static bool SameValue(string? stored, string value) =>
        stored == value || (PolishNumber.TryParse(stored, out var a) && PolishNumber.TryParse(value, out var b) && a == b);
}
