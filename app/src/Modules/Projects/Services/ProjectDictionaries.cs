using System.Globalization;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Słowniki projektu (docs/slowniki.md, rozdz. 3, 5.2–5.6) na mechanizmie słowników (Shared/Utils/Dictionaries):
/// WP i CAM, Harmonogram i budżet, Cost Category – zmiany w projekcie (tabela słownika globalnego z kodem projektu),
/// Wykluczenia. Reguły zależne od projektu (zakres, inne projekty, lista osób) dostają kontekst – For(...).
/// Stawki CAS – zawartość nieustalona (O37): słownik nie jest wczytywany ani zapisywany.
/// </summary>
public static class ProjectDictionaries
{
    public const string WpCam = "wp-cam";
    public const string ScheduleBudget = "schedule-budget";
    public const string Exclusions = "exclusions";
    public const string CasRates = "cas-rates";

    public static readonly IReadOnlyList<string> CostCategoryChoices = ["Labor", "Material", "Subcontract"];

    private static readonly DictionarySpec WpCamSpec = new()
    {
        Code = WpCam,
        Name = "WP i CAM",
        Description = "Element P1S projektu → WP, CAM, Cost Category. Element P1S należy do projektu, gdy jest kodem P1S elementu nakładki (Legacy WBS albo cel z mapowania) albo leży pod nim.",
        Columns =
        [
            new("Element P1S", ColumnType.Text, Key: true),
            new("WP", ColumnType.Text, Required: true, CheckSimilar: true),
            new("CAM", ColumnType.Text, Required: true, CheckSimilar: true),
            new("Cost Category", ColumnType.Choice, Choices: CostCategoryChoices),
        ],
    };

    private static readonly DictionarySpec ScheduleBudgetSpec = new()
    {
        Code = ScheduleBudget,
        Name = "Harmonogram i budżet",
        Description = "WP → BAC HOURS (godziny), BAC MATERIAL (koszt materiałów), Baseline Start i Baseline Koniec – baseline projektu.",
        Columns =
        [
            new("WP", ColumnType.Text, Key: true),
            new("BAC HOURS", ColumnType.Decimal),
            new("BAC MATERIAL", ColumnType.Decimal),
            new("Baseline Start", ColumnType.Date, Aliases: ["Planowany Start"]),
            new("Baseline Koniec", ColumnType.Date, Aliases: ["Planowany Koniec"]),
        ],
    };

    private static readonly DictionarySpec ExclusionsSpec = new()
    {
        Code = Exclusions,
        Name = "Wykluczenia",
        Description = "Elementy pomijane w analizie: Cost Element, WBS Element, Partner object (co najmniej jedno) + opis. Słownik opcjonalny.",
        Columns =
        [
            new("Cost Element", ColumnType.Text, Key: true, PadNumericTo: 10),
            new("WBS Element", ColumnType.Text, Key: true),
            new("Partner object", ColumnType.Text, Key: true),
            new("Opis", ColumnType.Text, Required: true),
        ],
        EmptyKeyPartsAllowed = true,
    };

    /// <summary>Słowniki projektu w kolejności kreatora i ekranu projektu.</summary>
    public static IReadOnlyList<ProjectDictionaryItem> Items { get; } =
    [
        new(WpCam, "WP i CAM", "WP i CAM", RequiredFor: ProjectTypes.All, Stored: true),
        new(ScheduleBudget, "Harmonogram i budżet", "Harmonogram i budżet", RequiredFor: ProjectTypes.All, Stored: true),
        new(CasRates, "Stawki CAS", "Stawki CAS", RequiredFor: [ProjectTypes.Cas], Stored: false),
        new(GlobalDictionaries.CostCategory, "Cost Category – zmiany w projekcie", "Cost Category projektu", RequiredFor: [], Stored: true),
        new(Exclusions, "Wykluczenia", "Wykluczenia", RequiredFor: [], Stored: true),
    ];

    /// <summary>Słowniki projektu dla typu (Stawki CAS tylko dla CAS).</summary>
    public static IReadOnlyList<ProjectDictionaryItem> ForType(string type) =>
        Items.Where(i => i.Code != CasRates || type == ProjectTypes.Cas).ToList();

    /// <summary>Tabele słowników projektu w bazie (SqlDictionaryStore; Cost Category – tabela globalna, Tables w GlobalDictionaries).</summary>
    public static IReadOnlyList<DictionaryTable> Tables { get; } =
    [
        new(WpCamSpec, "dict.WpCam", [("Element P1S", "P1sElement"), ("WP", "Wp"), ("CAM", "Cam"), ("Cost Category", "CostCategory")]),
        new(ScheduleBudgetSpec, "dict.ScheduleBudget",
            [("WP", "Wp"), ("BAC HOURS", "BacHours"), ("BAC MATERIAL", "BacMaterial"), ("Baseline Start", "PlannedStart"), ("Baseline Koniec", "PlannedEnd")]),
        new(ExclusionsSpec, "dict.Exclusion", [("Cost Element", "CostElement"), ("WBS Element", "WbsElement"), ("Partner object", "PartnerObject"), ("Opis", "Description")]),
    ];

    /// <summary>Opis słownika bez reguł projektu – odczyt i eksport.</summary>
    public static DictionarySpec Base(string code) =>
        code == GlobalDictionaries.CostCategory ? GlobalDictionaries.Get(code) : Tables.First(t => t.Spec.Code == code).Spec;

    public static ProjectDictionaryItem Item(string code) => Items.First(i => i.Code == code);

    /// <summary>Opis słownika z regułami projektu (kontekst: zakres nakładki, inne projekty, osoby, WP).</summary>
    public static DictionarySpec For(string code, ProjectDictionaryContext context) => code switch
    {
        WpCam => With(WpCamSpec, rows => WpCamRules(rows, context)),
        ScheduleBudget => With(ScheduleBudgetSpec, rows => ScheduleBudgetRules(rows, context)),
        Exclusions => With(ExclusionsSpec, ExclusionRules),
        GlobalDictionaries.CostCategory => GlobalDictionaries.Get(GlobalDictionaries.CostCategory),
        _ => throw new NotSupportedException($"Słownik {code} nie jest zapisywany w bazie"),
    };

    /// <summary>
    /// Cost Category projektu do edycji (zakładka „Słowniki projektu”): zmiany projektu i pozycje słownika globalnego,
    /// których projekt nie zmienia (dziedziczone – Inherited). Zmiana pozycji dziedziczonej zapisuje się jako zmiana
    /// projektu, usunięcie zmiany projektu przywraca pozycję globalną. Kolejność – według klucza.
    /// </summary>
    public static IReadOnlyList<(DictRow Row, bool Inherited)> WithGlobal(DictionarySpec spec, IReadOnlyList<DictRow> global, IReadOnlyList<DictRow> project)
    {
        var own = project.Select(r => spec.KeyOf(r.Values)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return project.Select(r => (r, false))
            .Concat(global.Where(r => !own.Contains(spec.KeyOf(r.Values))).Select(r => (r, true)))
            .OrderBy(x => spec.KeyOf(x.Item1.Values), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DictionarySpec With(DictionarySpec spec, Func<IReadOnlyList<DictRow>, IEnumerable<Issue>> rules) => new()
    {
        Code = spec.Code, Name = spec.Name, Description = spec.Description, Columns = spec.Columns, Validity = spec.Validity,
        EmptyKeyPartsAllowed = spec.EmptyKeyPartsAllowed, Rules = rules,
    };

    /// <summary>docs/slowniki.md, rozdz. 5.2. Jeden WP na element P1S i WP wymaga CAM – klucz i pole wymagane.</summary>
    private static IEnumerable<Issue> WpCamRules(IReadOnlyList<DictRow> rows, ProjectDictionaryContext context)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            if (rows[i]["Element P1S"] is { } element)
            {
                if (context.P1sOwners.TryGetValue(element, out var owner))
                    yield return Issue.Error($"Element P1S {element} należy do projektu {owner}", at);
                else if (context.Scope.RootOf(element) is null)
                    yield return Issue.Error($"Element P1S {element} jest poza zakresem projektu (nie jest Legacy WBS ani celem mapowania elementu nakładki i nie leży pod nimi)", at);
            }
            if (rows[i]["CAM"] is { } cam && !context.Persons.Contains(cam))
                yield return Issue.Warning($"CAM „{cam}” spoza listy osób (słownik Osoby) – sprawdź pisownię", at);
        }
    }

    /// <summary>docs/slowniki.md, rozdz. 5.3.</summary>
    private static IEnumerable<Issue> ScheduleBudgetRules(IReadOnlyList<DictRow> rows, ProjectDictionaryContext context)
    {
        if (context.Wps is null && rows.Count > 0)
            yield return Issue.Warning("Nie wczytano słownika „WP i CAM” – nie sprawdzono, czy WP istnieją", "WP");
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            var row = rows[i];
            if (context.Wps is { } wps && row["WP"] is { } wp && !wps.Contains(wp))
                yield return Issue.Error($"WP {wp} nie istnieje w słowniku „WP i CAM” projektu", at);
            if (Dec(row["BAC HOURS"]) is < 0)
                yield return Issue.Error("BAC HOURS – budżet nie może być ujemny", at);
            if (Dec(row["BAC MATERIAL"]) is < 0)
                yield return Issue.Error("BAC MATERIAL – budżet nie może być ujemny", at);
            if (row["BAC HOURS"] is null && row["BAC MATERIAL"] is null)
                yield return Issue.Warning($"WP {row["WP"]} bez budżetu (BAC HOURS i BAC MATERIAL puste)", at);
            if (row["Baseline Start"] is { } start && row["Baseline Koniec"] is { } end && string.CompareOrdinal(start, end) > 0)
                yield return Issue.Error("Baseline Start jest późniejszy niż Baseline Koniec", at);
        }
    }

    /// <summary>docs/slowniki.md, rozdz. 5.6 (powtórzona kombinacja – klucz słownika).</summary>
    private static IEnumerable<Issue> ExclusionRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            if (rows[i]["Cost Element"] is null && rows[i]["WBS Element"] is null && rows[i]["Partner object"] is null)
                yield return Issue.Error("Podaj co najmniej jedno z pól: Cost Element, WBS Element, Partner object", at);
            if (rows[i]["Cost Element"] is { Length: > 10 } number)
                yield return Issue.Error($"Cost Element '{number}' dłuższy niż 10 znaków", at);
        }
    }

    private static decimal? Dec(string? canonical) =>
        decimal.TryParse(canonical, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
}
