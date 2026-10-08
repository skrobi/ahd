using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Edycja w tabeli struktury projektu (docs/performance-objectives.md, rozdz. 4.2): kolumny, które wolno zmieniać
/// w danym wierszu, i zmiana wiersza słownika projektu, do którego należy komórka. Kolumny zablokowane – Locked
/// (lista do uzupełnienia, gdy zostanie ustalone, czego nie wolno zmieniać).
/// </summary>
public static class StructureEdits
{
    public const string Name = "Name";
    public const string P1s = "P1s";
    public const string Wp = "Wp";
    public const string Cam = "Cam";
    public const string CostCategory = "CostCategory";
    public const string BacHours = "BacHours";
    public const string BacMaterial = "BacMaterial";
    public const string Start = "Start";
    public const string Finish = "Finish";

    /// <summary>Kolumny, których nie wolno edytować w tabeli (zmiana tylko przez słowniki projektu).</summary>
    public static readonly IReadOnlySet<string> Locked = new HashSet<string>();

    /// <summary>Kolumny słownika „WP i CAM” (klucz – kod P1S wiersza).</summary>
    public static readonly IReadOnlyList<string> WpCamColumns = [Wp, Cam, CostCategory];

    /// <summary>Kolumny słownika „Harmonogram i budżet” (klucz – WP wiersza).</summary>
    public static readonly IReadOnlyList<string> ScheduleColumns = [BacHours, BacMaterial, Start, Finish];

    /// <summary>
    /// Czy komórkę można zmienić: nazwa i Legacy WBS – węzeł nakładki; WP, CAM, Cost Category – wiersz z kodem P1S;
    /// budżet i daty – wiersz, którego sumy to jego własny WP (bez innych WP w poddrzewie).
    /// </summary>
    public static bool CanEdit(StructureRow row, string column)
    {
        if (Locked.Contains(column))
            return false;
        return column switch
        {
            Name => row.Kind == GridRowKind.Objective,
            P1s => row is { Kind: GridRowKind.Objective, IsVirtual: false },
            Wp or Cam or CostCategory => row.P1s is not null,
            BacHours or BacMaterial or Start or Finish => row.OwnsBudget,
            _ => false,
        };
    }

    /// <summary>
    /// Budżet i daty wiersza, w którym WP jest właśnie zaznaczany (wklejenie WP z budżetem za jednym razem): wiersz
    /// z kodem P1S, bez WP i bez WP w poddrzewie – po zapisie jego sumy to jego własny WP.
    /// </summary>
    public static bool CanEditWithNewWp(StructureRow row, string column) =>
        !Locked.Contains(column) && ScheduleColumns.Contains(column) && row is { P1s: not null, Wp: null, Wps.Count: 0 };

    /// <summary>Nazwa kolumny słownika dla kolumny tabeli.</summary>
    public static string DictionaryColumn(string column) => column switch
    {
        Wp => "WP",
        Cam => "CAM",
        CostCategory => "Cost Category",
        BacHours => "BAC HOURS",
        BacMaterial => "BAC MATERIAL",
        Start => "Baseline Start",
        Finish => "Baseline Koniec",
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
    };

    /// <summary>
    /// „WP i CAM” po zmianie w wierszu elementu P1S. WP to znacznik (Wp = "true" / "false"): zaznaczenie – element jest
    /// pakietem pracy, który ma mieć koszty i budżet (kod WP = kod elementu; dotychczasowy kod WP zostaje); odznaczenie –
    /// przypisanie usuwane (WP, CAM, Cost Category). Wybór CAM w wierszu bez WP zaznacza WP. Zwraca stan do zapisu
    /// (DictionaryService.Save).
    /// </summary>
    public static (List<DictRow> Working, List<DictRow> Removed) WpCam(IReadOnlyList<DictRow> rows, string element, IReadOnlyDictionary<string, string?> changes)
    {
        var index = rows.ToList().FindIndex(r => MappingKeys.Key(r["Element P1S"]) == MappingKeys.Key(element));
        var existingWp = index >= 0 ? rows[index]["WP"] : null;
        var values = new Dictionary<string, string?>();
        var cleared = changes.TryGetValue(Wp, out var flag) && !IsChecked(flag);
        if (cleared)
        {
            values[Wp] = null;
            values[Cam] = null;
            values[CostCategory] = null;
        }
        else
        {
            if (changes.TryGetValue(Cam, out var cam))
                values[Cam] = cam;
            if (changes.TryGetValue(CostCategory, out var category))
                values[CostCategory] = category;
            if (IsChecked(flag) || values.Count > 0)
                values[Wp] = existingWp ?? element;
        }
        return Change(rows, r => MappingKeys.Key(r["Element P1S"]) == MappingKeys.Key(element), "Element P1S", element, WpCamColumns, values);
    }

    /// <summary>Wartość znacznika WP z tabeli (checkbox): "true" / "tak" / "1" – zaznaczony.</summary>
    public static bool IsChecked(string? value) =>
        value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("tak", StringComparison.OrdinalIgnoreCase) || value == "1");

    /// <summary>„Harmonogram i budżet” po zmianie budżetu lub dat WP (wszystkie puste – wiersz usunięty).</summary>
    public static (List<DictRow> Working, List<DictRow> Removed) Schedule(IReadOnlyList<DictRow> rows, string wp, IReadOnlyDictionary<string, string?> changes) =>
        Change(rows, r => string.Equals(r["WP"], wp, StringComparison.OrdinalIgnoreCase), "WP", wp, ScheduleColumns, changes);

    /// <summary>
    /// Harmonogram po zmianie WP elementu: WP, którego nie ma już w „WP i CAM”, przekazuje budżet nowemu WP (gdy ten
    /// go nie ma), a w pozostałych przypadkach jego wiersz jest usuwany – inaczej harmonogram miałby WP spoza słownika.
    /// </summary>
    public static (List<DictRow> Working, List<DictRow> Removed) ScheduleAfterWpChange(IReadOnlyList<DictRow> rows, string oldWp, string? newWp)
    {
        var working = rows.ToList();
        var removed = new List<DictRow>();
        var index = working.FindIndex(r => string.Equals(r["WP"], oldWp, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return (working, removed);
        var old = working[index];
        if (newWp is not null && !working.Any(r => string.Equals(r["WP"], newWp, StringComparison.OrdinalIgnoreCase)))
        {
            var values = old.Values.ToDictionary(p => p.Key, p => p.Value);
            values["WP"] = newWp;
            working[index] = old with { Values = values };
        }
        else
        {
            working.RemoveAt(index);
            removed.Add(old);
        }
        return (working, removed);
    }

    private static (List<DictRow> Working, List<DictRow> Removed) Change(IReadOnlyList<DictRow> rows, Func<DictRow, bool> isRow, string keyColumn, string key,
        IReadOnlyList<string> columns, IReadOnlyDictionary<string, string?> changes)
    {
        var working = rows.ToList();
        var removed = new List<DictRow>();
        var index = working.FindIndex(r => isRow(r));
        var values = index >= 0 ? working[index].Values.ToDictionary(p => p.Key, p => p.Value) : new Dictionary<string, string?> { [keyColumn] = key };
        foreach (var column in columns)
        {
            if (changes.TryGetValue(column, out var value))
                values[DictionaryColumn(column)] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        var empty = columns.All(c => values.GetValueOrDefault(DictionaryColumn(c)) is null);
        if (index >= 0)
        {
            if (empty)
            {
                removed.Add(working[index]);
                working.RemoveAt(index);
            }
            else
                working[index] = working[index] with { Values = values };
        }
        else if (!empty)
            working.Add(new DictRow(null, null, values));
        return (working, removed);
    }
}
