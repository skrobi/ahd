using System.Globalization;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Słowniki projektu (docs/slowniki.md, rozdz. 3, 5.2–5.6) na mechanizmie słowników (Shared/Utils/Dictionaries):
/// WP i CAM, Harmonogram i budżet, Kategorie WBS, Cost Category – zmiany w projekcie (tabela słownika globalnego
/// z kodem projektu), Wykluczenia. Reguły zależne od projektu (zakres, inne projekty, lista osób) dostają kontekst – For(...).
/// Narzuty CAS nie mają słownika projektu – słowniki globalne (docs/slowniki.md, rozdz. 6.1; O37).
/// </summary>
public static class ProjectDictionaries
{
    public const string WpCam = "wp-cam";
    public const string ScheduleBudget = "schedule-budget";
    public const string Exclusions = "exclusions";
    public const string WbsCategories = "wbs-categories";
    public const string Sources = "project-sources";
    public const string Settings = "project-settings";

    /// <summary>Słowniki pokazane w zakładce „Konfiguracja” projektu (pozostałe – „Słowniki projektu”).</summary>
    public static IReadOnlySet<string> Configuration { get; } = new HashSet<string> { Sources, Settings };

    private static readonly DictionarySpec WpCamSpec = new()
    {
        Code = WpCam,
        Name = "WP i CAM",
        Description = "Element P1S programu → WP, CAM, Cost Category (kategoria ze słownika „Kategorie WBS” programu), MPK (słownik „Stawki wydziałów” – stawka ETC Cost). Element P1S należy do programu, gdy jest kodem P1S elementu nakładki (Legacy WBS albo cel z mapowania) albo leży pod nim.",
        Columns =
        [
            new("Element P1S", ColumnType.Text, Key: true),
            new("WP", ColumnType.Text, Required: true, CheckSimilar: true),
            new("CAM", ColumnType.Text, CheckSimilar: true, Lookup: GlobalDictionaries.Persons),
            new("Cost Category", ColumnType.Text, CheckSimilar: true, Lookup: WbsCategories),
            new("MPK", ColumnType.Text, Lookup: GlobalDictionaries.DepartmentRates),
        ],
        SkipKeyOnlyRows = true,
    };

    private static readonly DictionarySpec ScheduleBudgetSpec = new()
    {
        Code = ScheduleBudget,
        Name = "Harmonogram i budżet",
        Description = "WP → BAC HOURS (godziny), BAC MATERIAL (koszt materiałów), BAC (budżet kosztowy), Baseline Start i Baseline Koniec – baseline programu; Actual Start i Actual Koniec – daty rzeczywiste WP nadpisujące daty z raportu produkcyjnego (puste – data z produkcji); Forecast Start i Forecast Koniec – prognoza; ETC HOURS i ETC MATERIAL – godziny i materiały do poniesienia nadpisujące wartości z produkcji (puste – z produkcji). Kwoty (BAC MATERIAL, BAC, ETC MATERIAL) – w walucie programu; Waluta – kod waluty programu zapisywany przy wpisaniu kwot (puste – waluta programu).",
        Columns =
        [
            new("WP", ColumnType.Text, Key: true),
            new("BAC HOURS", ColumnType.Decimal),
            new("BAC MATERIAL", ColumnType.Decimal),
            new("BAC", ColumnType.Decimal),
            new("Baseline Start", ColumnType.Date, Aliases: ["Planowany Start"]),
            new("Baseline Koniec", ColumnType.Date, Aliases: ["Planowany Koniec"]),
            new("Actual Start", ColumnType.Date),
            new("Actual Koniec", ColumnType.Date, Aliases: ["Actual Finish"]),
            new("Forecast Start", ColumnType.Date),
            new("Forecast Koniec", ColumnType.Date, Aliases: ["Forecast Finish"]),
            new("ETC HOURS", ColumnType.Decimal),
            new("ETC MATERIAL", ColumnType.Decimal),
            new(CurrencyColumn, ColumnType.Text),
        ],
        SkipKeyOnlyRows = true,
    };

    private static readonly DictionarySpec WbsCategoriesSpec = new()
    {
        Code = WbsCategories,
        Name = "Kategorie WBS",
        Description = "Kategorie elementów WBS programu (np. Production, Programs) – do wyboru w kolumnie Cost Category struktury i słownika „WP i CAM”; raport po kategoriach. Inne niż globalny słownik Cost Category (numer elementu kosztowego).",
        Columns =
        [
            new("Cost Category", ColumnType.Text, Key: true, CheckSimilar: true),
            new("Opis", ColumnType.Text),
        ],
    };

    private static readonly DictionarySpec SourcesSpec = new()
    {
        Code = Sources,
        Name = "Źródła danych",
        Description = "Definicje źródeł (Administracja) importowane dla programu – „Importuj źródła programu” importuje tylko ich pliki.",
        Columns =
        [
            new("Definicja źródła", ColumnType.Text, Key: true, Lookup: Sources),
            new("Opis", ColumnType.Text),
        ],
    };

    private static readonly DictionarySpec SettingsSpec = new()
    {
        Code = Settings,
        Name = "Parametry programu",
        Description = $"Parametry programu: {ProjectSettings.ActualsAmount} – pole kwoty parsera ACTUALS dla ACWP struktury, raportu kosztów i przebiegu "
                      + "(ValueObjCrcy – waluta obiektu (PLN), ValueRepCur – waluta raportu); brak wiersza – ValueObjCrcy. "
                      + $"{ProjectSettings.RunDefinition} – szablon przebiegu programu (ekran Przebiegi); brak wiersza – szablon domyślny dla typu programu.",
        Columns =
        [
            new("Parametr", ColumnType.Choice, Key: true, Choices: [ProjectSettings.ActualsAmount, ProjectSettings.RunDefinition]),
            new("Wartość", ColumnType.Text, Required: true, Lookup: Settings),
            new("Opis", ColumnType.Text),
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
        new(WpCam, "WP i CAM", "WP i CAM", Stored: true),
        new(ScheduleBudget, "Harmonogram i budżet", "Harmonogram i budżet", Stored: true),
        new(WbsCategories, "Kategorie WBS", "Kategorie WBS", Stored: true),
        new(GlobalDictionaries.CostCategory, "Cost Category – zmiany w programie", "Cost Category projektu", Stored: true),
        new(Exclusions, "Wykluczenia", "Wykluczenia", Stored: true),
        new(Sources, "Źródła danych", "Źródła danych", Stored: true),
        new(Settings, "Parametry programu", "Parametry projektu", Stored: true),
    ];

    /// <summary>Tabele słowników projektu w bazie (SqlDictionaryStore; Cost Category – tabela globalna, Tables w GlobalDictionaries).</summary>
    public static IReadOnlyList<DictionaryTable> Tables { get; } =
    [
        new(WpCamSpec, "dict.WpCam", [("Element P1S", "P1sElement"), ("WP", "Wp"), ("CAM", "Cam"), ("Cost Category", "CostCategory"), ("MPK", "Mpk")]),
        new(ScheduleBudgetSpec, "dict.ScheduleBudget",
            [("WP", "Wp"), ("BAC HOURS", "BacHours"), ("BAC MATERIAL", "BacMaterial"), ("BAC", "Bac"), ("Baseline Start", "PlannedStart"), ("Baseline Koniec", "PlannedEnd"),
             ("Actual Start", "ActualStart"), ("Actual Koniec", "ActualEnd"), ("Forecast Start", "ForecastStart"), ("Forecast Koniec", "ForecastEnd"),
             ("ETC HOURS", "EtcHours"), ("ETC MATERIAL", "EtcMaterial"), (CurrencyColumn, "Currency")]),
        new(WbsCategoriesSpec, "dict.WbsCategory", [("Cost Category", "Category"), ("Opis", "Description")]),
        new(ExclusionsSpec, "dict.Exclusion", [("Cost Element", "CostElement"), ("WBS Element", "WbsElement"), ("Partner object", "PartnerObject"), ("Opis", "Description")]),
        new(SourcesSpec, "dict.ProjectSource", [("Definicja źródła", "SourceCode"), ("Opis", "Description")]),
        new(SettingsSpec, "dict.ProjectSetting", [("Parametr", "Name"), ("Wartość", "Value"), ("Opis", "Description")]),
    ];

    /// <summary>Opis słownika bez reguł projektu – odczyt i eksport.</summary>
    public static DictionarySpec Base(string code) =>
        code == GlobalDictionaries.CostCategory ? GlobalDictionaries.Get(code) : Tables.First(t => t.Spec.Code == code).Spec;

    public static ProjectDictionaryItem Item(string code) => Items.First(i => i.Code == code);

    /// <summary>Opis słownika z regułami projektu (kontekst: zakres nakładki, inne projekty, osoby, WP).</summary>
    public static DictionarySpec For(string code, ProjectDictionaryContext context) => code switch
    {
        WpCam => With(WpCamSpec, rows => WpCamRules(rows, context)),
        ScheduleBudget => With(ScheduleBudgetSpec, rows => ScheduleBudgetRules(rows, context), (row, stored) => StampCurrency(row, stored, context.Currency, context.Factor)),
        Exclusions => With(ExclusionsSpec, ExclusionRules),
        WbsCategories => WbsCategoriesSpec,
        Sources => With(SourcesSpec, rows => SourceRules(rows, context)),
        Settings => With(SettingsSpec, SettingRules),
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

    private static DictionarySpec With(DictionarySpec spec, Func<IReadOnlyList<DictRow>, IEnumerable<Issue>> rules,
        Func<DictRow, DictRow?, DictRow>? prepare = null) => new()
    {
        Code = spec.Code, Name = spec.Name, Description = spec.Description, Columns = spec.Columns, Validity = spec.Validity,
        EmptyKeyPartsAllowed = spec.EmptyKeyPartsAllowed, SkipKeyOnlyRows = spec.SkipKeyOnlyRows, Rules = rules, Prepare = prepare,
    };

    /// <summary>„Harmonogram i budżet”: kolumna z kodem waluty wpisanych kwot (MoneyColumns).</summary>
    public const string CurrencyColumn = "Waluta";

    /// <summary>Kwoty „Harmonogram i budżet” – w walucie projektu (CurrencyColumn); BAC HOURS i ETC HOURS to godziny.</summary>
    public static IReadOnlyList<string> MoneyColumns { get; } = ["BAC MATERIAL", "BAC", "ETC MATERIAL"];

    /// <summary>
    /// Kwoty wiersza „Harmonogram i budżet” w walucie projektu (rates.Currency): wiersz w innej walucie (CurrencyColumn) –
    /// przeliczony kursem bieżącym (rates.Factor); brak kursu – kwoty puste i powód w gap. rates null albo waluta projektu
    /// nieznana – wiersz bez zmian.
    /// </summary>
    public static DictRow InProjectCurrency(DictRow row, EtcRates? rates, out string? gap)
    {
        gap = null;
        var currency = row[CurrencyColumn];
        if (rates is null || rates.Factor(currency) is 1m)
            return row;
        var factor = rates.Factor(currency);
        if (factor is null)
            gap = $"budżet w {currency} – brak kursu {currency} w „Kursy walut” (przeliczenie na {rates.Currency})";
        var values = row.Values.ToDictionary(v => v.Key, v => v.Value);
        foreach (var column in MoneyColumns)
            values[column] = Dec(row[column]) is { } amount ? (amount * factor)?.ToString(System.Globalization.CultureInfo.InvariantCulture) : row[column];
        return row with { Values = values };
    }

    /// <summary>
    /// Waluta kwot wiersza „Harmonogram i budżet” przy zapisie: kwoty zmienione (albo nowy wiersz z kwotą) – waluta projektu
    /// (nieznana – waluta wiersza bez zmian); kwoty bez zmian – waluta wiersza; brak kwot – pusta. Wiersz w innej walucie:
    /// niezmienione kwoty przeliczone na walutę projektu (factor; brak kursu – wiersz zostaje w swojej walucie).
    /// </summary>
    public static DictRow StampCurrency(DictRow row, DictRow? stored, string? currency, Func<string, decimal?>? factor = null)
    {
        var previous = stored?[CurrencyColumn] ?? row[CurrencyColumn];
        var currencyOf = MoneyColumns.All(c => row[c] is null) ? null
            : stored is not null && MoneyColumns.All(c => row[c] == stored[c]) ? row[CurrencyColumn]
            : currency ?? row[CurrencyColumn];
        var values = row.Values.ToDictionary(v => v.Key, v => v.Value);
        if (currencyOf is not null && previous is not null && stored is not null && !string.Equals(previous, currencyOf, StringComparison.OrdinalIgnoreCase)
            && MoneyColumns.Any(c => row[c] is not null && row[c] == stored[c]))
        {
            if (factor?.Invoke(previous) is not { } rate)
                currencyOf = previous;
            else
                foreach (var column in MoneyColumns.Where(c => row[c] is not null && row[c] == stored[c]))
                    values[column] = Dec(row[column]) is { } amount ? (amount * rate).ToString(System.Globalization.CultureInfo.InvariantCulture) : row[column];
        }
        if (currencyOf == row[CurrencyColumn] && values.All(v => v.Value == row[v.Key]))
            return row;
        values[CurrencyColumn] = currencyOf;
        return row with { Values = values };
    }

    /// <summary>
    /// docs/slowniki.md, rozdz. 5.2. Jeden WP na element P1S – klucz. CAM uzupełnia się później (najpierw wskazuje się WP):
    /// WP bez CAM – WARNING przy zapisie, ERROR w gotowości projektu (blokuje przebieg – ProjectReadiness).
    /// </summary>
    private static IEnumerable<Issue> WpCamRules(IReadOnlyList<DictRow> rows, ProjectDictionaryContext context)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            if (rows[i]["Element P1S"] is { } element)
            {
                if (context.P1sOwners.TryGetValue(element, out var owner))
                    yield return Issue.Error($"Element P1S {element} należy do programu {owner}", at);
                else if (context.Scope.RootOf(element) is null)
                    yield return Issue.Error($"Element P1S {element} jest poza zakresem programu (nie jest Legacy WBS ani celem mapowania elementu nakładki i nie leży pod nimi)", at);
            }
            if (rows[i]["WP"] is { } wp && rows[i]["CAM"] is null)
                yield return Issue.Warning($"WP {wp} bez CAM – uzupełnij CAM przed przebiegiem", at);
            if (rows[i]["CAM"] is { } cam && !context.Persons.Contains(cam))
                yield return Issue.Warning($"CAM „{cam}” spoza listy osób (słownik Osoby) – sprawdź pisownię", at);
            if (rows[i]["Cost Category"] is { } category && context.Categories is { } categories && !categories.Contains(category))
                yield return Issue.Warning($"Cost Category „{category}” spoza słownika „Kategorie WBS” programu – dodaj ją do słownika albo wybierz z listy", at);
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
                yield return Issue.Error($"WP {wp} nie istnieje w słowniku „WP i CAM” programu", at);
            if (Dec(row["BAC HOURS"]) is < 0)
                yield return Issue.Error("BAC HOURS – budżet nie może być ujemny", at);
            if (Dec(row["BAC MATERIAL"]) is < 0)
                yield return Issue.Error("BAC MATERIAL – budżet nie może być ujemny", at);
            if (Dec(row["BAC"]) is < 0)
                yield return Issue.Error("BAC – budżet nie może być ujemny", at);
            if (row["BAC HOURS"] is null && row["BAC MATERIAL"] is null && row["BAC"] is null)
                yield return Issue.Warning($"WP {row["WP"]} bez budżetu (BAC HOURS, BAC MATERIAL i BAC puste)", at);
            if (row["Baseline Start"] is { } start && row["Baseline Koniec"] is { } end && string.CompareOrdinal(start, end) > 0)
                yield return Issue.Error("Baseline Start jest późniejszy niż Baseline Koniec", at);
            if (row["Actual Start"] is { } actualStart && row["Actual Koniec"] is { } actualEnd && string.CompareOrdinal(actualStart, actualEnd) > 0)
                yield return Issue.Error("Actual Start jest późniejszy niż Actual Koniec", at);
            if (row["Forecast Start"] is { } forecastStart && row["Forecast Koniec"] is { } forecastEnd && string.CompareOrdinal(forecastStart, forecastEnd) > 0)
                yield return Issue.Error("Forecast Start jest późniejszy niż Forecast Koniec", at);
            if (Dec(row["ETC HOURS"]) is < 0)
                yield return Issue.Error("ETC HOURS nie może być ujemne", at);
            if (Dec(row["ETC MATERIAL"]) is < 0)
                yield return Issue.Error("ETC MATERIAL nie może być ujemne", at);
            if (row[CurrencyColumn] is { } code && (code.Length != 3 || !code.All(char.IsAsciiLetterUpper)))
                yield return Issue.Error($"Waluta „{code}” – kod waluty z trzech wielkich liter (np. PLN, USD)", at);
        }
    }

    /// <summary>Definicja źródła musi być aktywną definicją z Administracji (context.Sources; null – bez kontroli).</summary>
    private static IEnumerable<Issue> SourceRules(IReadOnlyList<DictRow> rows, ProjectDictionaryContext context)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i]["Definicja źródła"] is { } code && context.Sources is { } sources && !sources.Contains(code))
                yield return Issue.Warning($"Definicja źródła {code} nie istnieje albo jest nieaktywna (Administracja) – jej pliki nie zostaną zaimportowane",
                    DictionaryValidator.RowElement(i));
        }
    }

    /// <summary>„Parametry projektu”: pole kwoty ACTUALS – jedno z pól ProjectSettings.ActualsAmounts.</summary>
    private static IEnumerable<Issue> SettingRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i]["Parametr"] == ProjectSettings.ActualsAmount && rows[i]["Wartość"] is { } value
                && !ProjectSettings.ActualsAmounts.Any(a => string.Equals(a.Field, value, StringComparison.OrdinalIgnoreCase)))
                yield return Issue.Error($"{ProjectSettings.ActualsAmount} – jedno z pól: {string.Join(", ", ProjectSettings.ActualsAmounts.Select(a => a.Field))}",
                    DictionaryValidator.RowElement(i));
        }
    }

    /// <summary>Przebieg programu z wierszy słownika „Parametry projektu” (null – szablon domyślny dla typu programu).</summary>
    public static string? RunDefinition(IEnumerable<DictRow> settings) =>
        settings.FirstOrDefault(r => r["Parametr"] == ProjectSettings.RunDefinition)?["Wartość"]?.Trim() is { Length: > 0 } code ? code : null;

    /// <summary>Pole kwoty ACTUALS z wierszy słownika „Parametry projektu” (brak – ValueObjCrcy).</summary>
    public static string ActualsAmount(IEnumerable<DictRow> settings) =>
        ProjectSettings.ActualsAmountOf(settings.FirstOrDefault(r => r["Parametr"] == ProjectSettings.ActualsAmount)?["Wartość"]);

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
