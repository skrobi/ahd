using PzlEv.Shared.Models.Hr;
using PzlEv.Shared.Models.PzlProd;
using System.Globalization;
using System.Text.RegularExpressions;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>Słowniki globalne (docs/slowniki.md, rozdz. 2) z regułami rozdz. 5.4–5.5 i 5.7.</summary>
public static partial class GlobalDictionaries
{
    public const string Calendar = "calendar";
    public const string DepartmentRates = "department-rates";
    public const string FxRates = "fx-rates";
    public const string CostCategory = "cost-category";
    public const string Persons = "persons";
    public const string DjkRates = "djk-rates";
    public const string ProductionParameters = "production-parameters";
    public const string CasPools = "cas-pools";
    public const string CasBurdenGroups = "cas-burden-groups";
    public const string CasBurdenFormulas = "cas-burden-formulas";
    public const string CasBurdenRates = "cas-burden-rates";

    /// <summary>Kolumna słownika Cost Category z rodzajem kosztu (Hours / Materials / Other; puste – Other) – podział ACWP w strukturze.</summary>
    public const string CostKindColumn = "Rodzaj kosztu";
    public const string LaborKind = "Hours";
    public const string MaterialKind = "Materials";
    public const string OtherKind = "Other";

    /// <summary>Kolumna słownika „Kursy walut” z kursem projektów CAS (koszt PLN po narzutach → waluta projektu).</summary>
    public const string CasFxColumn = "Kurs CAS";

    /// <summary>Kolumna słownika Cost Category z grupą narzutową CAS elementu kosztowego (raport kosztów CES).</summary>
    public const string BurdenGroupColumn = "Grupa narzutowa CAS";

    /// <summary>Parametry słownika „Parametry produkcji” (kolumna Parametr).</summary>
    public const string ProductivityMonths = "Okno produktywności IPT (mies.)";
    public const string DeliveredStatuses = "Statusy materiałów dostarczonych";
    public const string DivideByZClo = "Wartość materiałów ÷ (1 + Z_CLO)";

    public static IReadOnlyList<DictionarySpec> All { get; } =
    [
        new()
        {
            Code = Calendar,
            Name = "Kalendarz okresów",
            Description = "Tygodnie okresów (RRRR-MM) i oznaczenie tygodnia zamykającego okres. Numeracja tygodni – O18 (dane startowe: tygodnie ISO).",
            Columns =
            [
                new("Rok", ColumnType.Integer, Key: true),
                new("Tydzień", ColumnType.Integer, Key: true),
                new("Okres", ColumnType.Text, Required: true),
                new("Od", ColumnType.Date, Required: true),
                new("Do", ColumnType.Date, Required: true),
                new("Zamykający", ColumnType.Boolean, Required: true),
            ],
            Rules = CalendarRules,
        },
        new()
        {
            Code = DepartmentRates,
            Name = "Stawki wydziałów",
            Description = "Stawka i narzut MPK (miejsca powstawania kosztów) na rok – przeliczenie godzin na koszt w łączeniu źródeł (P3). Department – opis MPK; Overhead opcjonalny.",
            Columns =
            [
                new("MPK", ColumnType.Text, Key: true),
                new("Department", ColumnType.Text, CheckSimilar: true),
                new("Year", ColumnType.Integer, Key: true),
                new("Labor Rate", ColumnType.Decimal, Required: true),
                new("Overhead", ColumnType.Decimal),
            ],
            Rules = RateRules,
        },
        new()
        {
            Code = FxRates,
            Name = "Kursy walut",
            Description = "Kurs waluty (do PLN: ile PLN za jednostkę waluty) obowiązujący od – do (Do puste – bezterminowo). „Kurs” – przeliczenia walut w strukturze projektu (materiały, budżety, ETC), łączeniu źródeł (P3) i obliczeniu EV (P8); „Kurs CAS” – projekty CAS w walucie innej niż PLN: koszt PLN z ACTUALS po narzutach CAS przeliczany kursem CAS obowiązującym w okresie kosztu (kurs zakupu waluty); brak kursu CAS dla okresu z kosztem – błąd.",
            Columns =
            [
                new("Waluta", ColumnType.Text, Key: true),
                new("Od", ColumnType.Date, Key: true),
                new("Do", ColumnType.Date),
                new("Kurs", ColumnType.Decimal),
                new(CasFxColumn, ColumnType.Decimal),
            ],
            Validity = ("Od", "Do"),
            Rules = FxRules,
        },
        new()
        {
            Code = CostCategory,
            Name = "Cost Category",
            Description = "Numer elementu kosztowego → opis, obszar, Cost Category; „Rozliczeniowy” – cost element rozliczenia (settlement), pomijany w ACWP jako rozliczenie wychodzące; „Rodzaj kosztu” – Hours / Materials / Other (puste – Other): podział ACWP w strukturze projektu na ACWP Hours, ACWP Materials i ACWP Other. Numer liczbowy uzupełniany zerami do 10 znaków.",
            Columns =
            [
                new("Numer elementu kosztowego", ColumnType.Text, Key: true, PadNumericTo: 10),
                new("Opis", ColumnType.Text),
                new("Obszar", ColumnType.Text, CheckSimilar: true),
                new("Cost Category", ColumnType.Text, CheckSimilar: true),
                new("Rozliczeniowy", ColumnType.Boolean),
                new(CostKindColumn, ColumnType.Choice, Choices: [LaborKind, MaterialKind, OtherKind]),
                new(BurdenGroupColumn, ColumnType.Text, Lookup: CasBurdenGroups, IdLookup: true),
            ],
            Rules = CostCategoryRules,
        },
        new()
        {
            Code = Persons,
            Name = "Osoby",
            Description = "Pracownicy z HR (PZLHRPROD, HR.ORG – „Dodaj z HR”): USRID (numer znaczka, login) → imię i nazwisko, MPK, dział, stanowisko. CAM w strukturze projektu wybiera się z tej listy (zapisywany USRID).",
            Columns =
            [
                new("USRID", ColumnType.Text, Key: true, Aliases: ["Konto AD"]),
                new("Imię i nazwisko", ColumnType.Text, Required: true, CheckSimilar: true),
                new("Imię", ColumnType.Text),
                new("Nazwisko", ColumnType.Text),
                new("E-mail", ColumnType.Text),
                new("MPK", ColumnType.Text),
                new("Dział", ColumnType.Text),
                new("Dział – pełna nazwa", ColumnType.Text),
                new("Stanowisko", ColumnType.Text),
                new("Pion", ColumnType.Text),
                new("Manager", ColumnType.Text),
                new("PERNR", ColumnType.Text),
            ],
        },
        new()
        {
            Code = DjkRates,
            Name = "Wskaźniki DJK",
            Description = "Godziny jakości DJK doliczane do godzin produkcji: udział godzin TECH / TECH_PON według prefiksu grupy stanowisk (IPT, np. W2, W51 – najdłuższy pasujący prefiks). Odczyt danych produkcyjnych struktury projektu (vAHDD).",
            Columns =
            [
                new("Grupa stanowisk", ColumnType.Text, Key: true),
                new("Udział DJK", ColumnType.Decimal, Required: true),
                new("Opis", ColumnType.Text),
            ],
            Rules = DjkRules,
        },
        new()
        {
            Code = ProductionParameters,
            Name = "Parametry produkcji",
            Description = "Parametry odczytu danych produkcyjnych z PZLPROD (struktura projektu): okno produktywności IPT w miesiącach, statusy vAPD liczone jako dostarczone (po przecinku), dzielenie wartości materiałów przez (1 + Z_CLO) – tak / nie.",
            Columns =
            [
                new("Parametr", ColumnType.Choice, Key: true, Choices: [ProductivityMonths, DeliveredStatuses, DivideByZClo]),
                new("Wartość", ColumnType.Text, Required: true),
                new("Opis", ColumnType.Text),
            ],
            Rules = ProductionParameterRules,
        },
        new()
        {
            Code = CasPools,
            Name = "Pule narzutu CAS",
            Description = "Pule narzutu CAS – kolumny raportu kosztów CES w kolejności „Kolejność”; Rodzaj: Koszt – w TOTAL COST, FCCOM – w TOTAL FCCOM. Formuły, wskaźniki i raport wskazują pulę identyfikatorem – zmiana nazwy jest od razu widoczna wszędzie; puli używanej w formułach albo wskaźnikach nie można usunąć.",
            Columns =
            [
                new("Pula", ColumnType.Text, Key: true, CheckSimilar: true),
                new("Kolejność", ColumnType.Integer, Required: true),
                new("Rodzaj", ColumnType.Choice, Required: true, Choices: [CasBurden.CostKind, CasBurden.FccomKind]),
                new("Opis", ColumnType.Text),
            ],
            Rules = CasPoolRules,
            ReferencedBy = [(CasBurdenFormulas, "Pula", CasBurden.References), (CasBurdenFormulas, "Formuła", CasBurden.References), (CasBurdenRates, "Pula", CasBurden.References)],
        },
        new()
        {
            Code = CasBurdenGroups,
            Name = "Grupy narzutowe CAS",
            Description = "Grupa narzutowa elementu kosztowego (kolumna „Grupa narzutowa CAS” słownika Cost Category – zapisany identyfikator grupy, zmiana nazwy jest od razu widoczna). Pule naliczane w grupie i ich formuły – przycisk „Formuły narzutów” (macierz grup i pul, edytor formuły z klocków).",
            Columns =
            [
                new("Grupa", ColumnType.Text, Key: true, CheckSimilar: true),
                new("Opis", ColumnType.Text),
            ],
            ReferencedBy = [(CostCategory, BurdenGroupColumn, CasBurden.References), (CasBurdenFormulas, "Grupa", CasBurden.References)],
        },
        new()
        {
            Code = CasBurdenFormulas,
            Name = "Formuły narzutów CAS",
            Description = "Formuła puli w grupie narzutowej z klocków: Base cost elements (koszt bazowy kolumny), narzuty pul, wskaźniki pul, + − × ÷ ( ). Wiersz = pula naliczana w grupie. Edycja tylko w oknie „Formuły narzutów” (macierz grup i pul).",
            Columns =
            [
                new("Grupa", ColumnType.Text, Key: true, Lookup: CasBurdenGroups, IdLookup: true),
                new("Pula", ColumnType.Text, Key: true, Lookup: CasPools, IdLookup: true),
                new("Formuła", ColumnType.Text, Required: true),
            ],
            Rules = CasBurden.FormulaRules,
            Hidden = true,
        },
        new()
        {
            Code = CasBurdenRates,
            Name = "Wskaźniki narzutu CAS",
            Description = "Wskaźnik puli narzutu CAS obowiązujący od – do (zwykle rok: 2026-01-01 – 2026-12-31; Do puste – bezterminowo); pula z listy słownika „Pule narzutu CAS” (zapisany identyfikator – zmiana nazwy puli jest od razu widoczna). Raport kosztów CES bierze wskaźnik obowiązujący w okresie kolumny (okres, rok).",
            Columns =
            [
                new("Pula", ColumnType.Text, Key: true, Lookup: CasPools, IdLookup: true),
                new("Od", ColumnType.Date, Key: true),
                new("Do", ColumnType.Date),
                new("Wskaźnik", ColumnType.Decimal, Required: true, Decimals: 18),
                new("Opis", ColumnType.Text),
            ],
            Validity = ("Od", "Do"),
            Rules = CasRateRules,
        },
    ];

    public static DictionarySpec Get(string code) => All.First(s => s.Code == code);

    /// <summary>Tabele słowników globalnych w bazie (SqlDictionaryStore).</summary>
    public static IReadOnlyList<DictionaryTable> Tables { get; } =
    [
        new(Get(Calendar), "dict.Calendar",
            [("Rok", "Year"), ("Tydzień", "Week"), ("Okres", "Period"), ("Od", "DateFrom"), ("Do", "DateTo"), ("Zamykający", "IsClosing")]),
        new(Get(DepartmentRates), "dict.DepartmentRate",
            [("MPK", "CostCenter"), ("Department", "Department"), ("Year", "Year"), ("Labor Rate", "LaborRate"), ("Overhead", "Overhead")]),
        new(Get(FxRates), "dict.FxRate", [("Waluta", "Currency"), ("Od", "ValidFrom"), ("Do", "ValidTo"), ("Kurs", "Rate"), (CasFxColumn, "CasRate")]),
        new(Get(CostCategory), "dict.CostCategory",
            [("Numer elementu kosztowego", "CostElement"), ("Opis", "Description"), ("Obszar", "Area"), ("Cost Category", "CostCategory"),
             ("Rozliczeniowy", "IsSettlement"), (CostKindColumn, "CostKind"), (BurdenGroupColumn, "BurdenGroupId")]),
        new(Get(Persons), "dict.Person",
        [
            ("USRID", "AdAccount"), ("Imię i nazwisko", "FullName"), ("Imię", "FirstName"), ("Nazwisko", "LastName"), ("E-mail", "Email"),
            ("MPK", "CostCenter"), ("Dział", "DepartmentShort"), ("Dział – pełna nazwa", "DepartmentName"), ("Stanowisko", "Position"),
            ("Pion", "Division"), ("Manager", "IsManager"), ("PERNR", "Pernr"),
        ]),
        new(Get(DjkRates), "dict.DjkRate", [("Grupa stanowisk", "WorkCenterGroup"), ("Udział DJK", "Share"), ("Opis", "Description")]),
        new(Get(ProductionParameters), "dict.ProductionParameter", [("Parametr", "Name"), ("Wartość", "Value"), ("Opis", "Description")]),
        new(Get(CasPools), "dict.CasPool", [("Pula", "Pool"), ("Kolejność", "SortOrder"), ("Rodzaj", "Kind"), ("Opis", "Description")]),
        new(Get(CasBurdenGroups), "dict.CasBurdenGroup", [("Grupa", "BurdenGroup"), ("Opis", "Description")]),
        new(Get(CasBurdenFormulas), "dict.CasFormula", [("Grupa", "GroupId"), ("Pula", "PoolId"), ("Formuła", "Formula")]),
        new(Get(CasBurdenRates), "dict.CasBurdenRate", [("Pula", "PoolId"), ("Od", "ValidFrom"), ("Do", "ValidTo"), ("Wskaźnik", "Rate"), ("Opis", "Description")]),
    ];

    private static IEnumerable<Issue> CalendarRules(IReadOnlyList<DictRow> rows)
    {
        var weeks = new List<(DateOnly From, DateOnly To, int Index)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var at = DictionaryValidator.RowElement(i);
            if (Int(row["Rok"]) is { } year && (year < 2000 || year > 2100))
                yield return Issue.Error("Rok poza zakresem 2000–2100", at);
            if (Int(row["Tydzień"]) is { } week && (week < 1 || week > 53))
                yield return Issue.Error("Tydzień poza zakresem 1–53", at);
            if (row["Okres"] is { } period && !PeriodPattern().IsMatch(period))
                yield return Issue.Error($"Okres '{period}' – oczekiwano RRRR-MM", at);
            if (Date(row["Od"]) is { } from && Date(row["Do"]) is { } to)
            {
                if (from > to)
                    yield return Issue.Error("Od jest późniejsze niż Do", at);
                else
                    weeks.Add((from, to, i));
            }
        }

        var ordered = weeks.OrderBy(w => w.From).ToList();
        for (var j = 1; j < ordered.Count; j++)
        {
            if (ordered[j].From <= ordered[j - 1].To)
                yield return Issue.Error($"Tydzień nakłada się z wierszem {ordered[j - 1].Index + 1}", DictionaryValidator.RowElement(ordered[j].Index));
        }

        foreach (var period in rows.Where(r => r["Okres"] is not null).GroupBy(r => r["Okres"]!))
        {
            var closing = period.Count(r => r["Zamykający"] == "tak");
            if (closing == 0)
                yield return Issue.Warning("Okres bez tygodnia zamykającego", period.Key);
            else if (closing > 1)
                yield return Issue.Error($"Okres ma {closing} tygodnie zamykające – dozwolony jeden", period.Key);
        }
    }

    private static IEnumerable<Issue> RateRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            if (rows[i]["MPK"] is { Length: > 40 } mpk)
                yield return Issue.Error($"MPK '{mpk}' dłuższy niż 40 znaków", at);
            if (rows[i]["Department"] is { Length: > 100 })
                yield return Issue.Error("Department (opis MPK) dłuższy niż 100 znaków", at);
            if (Int(rows[i]["Year"]) is { } year && (year < 2000 || year > 2100))
                yield return Issue.Error("Year poza zakresem 2000–2100", at);
            if (Dec(rows[i]["Labor Rate"]) is { } rate && rate <= 0)
                yield return Issue.Error("Labor Rate musi być większa od 0", at);
            if (Dec(rows[i]["Overhead"]) is { } overhead && overhead < 0)
                yield return Issue.Error("Overhead nie może być ujemny", at);
        }
    }

    private static IEnumerable<Issue> FxRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            if (rows[i]["Waluta"] is { } currency && !CurrencyPattern().IsMatch(currency))
                yield return Issue.Error($"Waluta '{currency}' – kod ISO z trzech wielkich liter (np. USD)", at);
            if (Dec(rows[i]["Kurs"]) is { } rate && rate <= 0)
                yield return Issue.Error("Kurs musi być większy od 0", at);
            if (Dec(rows[i][CasFxColumn]) is { } casRate && casRate <= 0)
                yield return Issue.Error($"{CasFxColumn} musi być większy od 0", at);
            if (rows[i]["Kurs"] is null && rows[i][CasFxColumn] is null)
                yield return Issue.Error($"Podaj Kurs albo {CasFxColumn}", at);
        }
    }

    /// <summary>„Wskaźniki DJK”: udział 0–1; prefiks zawarty w innym (W5 i W51) – ostrzeżenie (liczy się najdłuższy).</summary>
    private static IEnumerable<Issue> DjkRules(IReadOnlyList<DictRow> rows)
    {
        var prefixes = rows.Select(r => r["Grupa stanowisk"]).OfType<string>().ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            if (Dec(rows[i]["Udział DJK"]) is { } share && (share < 0 || share > 1))
                yield return Issue.Error("Udział DJK – wartość od 0 do 1 (np. 0,15 = 15%)", at);
            if (rows[i]["Grupa stanowisk"] is { } prefix && prefixes.Any(p => p.Length > prefix.Length && p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                yield return Issue.Warning($"Grupa {prefix} jest prefiksem innej grupy – dla dłuższej liczy się jej udział", at);
        }
    }

    /// <summary>„Pule narzutu CAS”: nazwa inna niż koszt bazowy i bez „+” (separator podstawy), kolejność bez powtórzeń.</summary>
    private static IEnumerable<Issue> CasPoolRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            if (rows[i]["Pula"] is { } pool && (pool.Contains('+') || string.Equals(pool, CasBurden.Base, StringComparison.OrdinalIgnoreCase)))
                yield return Issue.Error($"Pula „{pool}” – nazwa nie może zawierać „+” ani być „{CasBurden.Base}”", at);
        }
        foreach (var order in rows.Where(r => r["Kolejność"] is not null).GroupBy(r => r["Kolejność"]!).Where(g => g.Count() > 1))
            yield return Issue.Warning($"Kolejność {order.Key} ma kilka pul ({string.Join(", ", order.Select(r => r["Pula"]))}) – kolumny ułożone po nazwie", "Kolejność");
    }

    /// <summary>„Wskaźniki narzutu CAS”: wskaźnik nieujemny (okresy obowiązywania – DictionaryValidator, Validity).</summary>
    private static IEnumerable<Issue> CasRateRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (Dec(rows[i]["Wskaźnik"]) is < 0)
                yield return Issue.Error("Wskaźnik nie może być ujemny", DictionaryValidator.RowElement(i));
        }
    }

    /// <summary>„Parametry produkcji”: okno 1–120 miesięcy, statusy niepuste, Z_CLO – tak / nie.</summary>
    private static IEnumerable<Issue> ProductionParameterRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            var value = rows[i]["Wartość"];
            switch (rows[i]["Parametr"])
            {
                case ProductivityMonths when Int(value) is not (>= 1 and <= 120):
                    yield return Issue.Error($"{ProductivityMonths} – liczba miesięcy od 1 do 120", at);
                    break;
                case DeliveredStatuses when Statuses(value).Count == 0:
                    yield return Issue.Error($"{DeliveredStatuses} – podaj statusy po przecinku (np. DOST, WYD)", at);
                    break;
                case DivideByZClo when YesNo(value) is null:
                    yield return Issue.Error($"{DivideByZClo} – tak albo nie", at);
                    break;
            }
        }
    }

    /// <summary>
    /// Parametry odczytu produkcji ze słowników „Wskaźniki DJK” i „Parametry produkcji”; pusty słownik – wartości raportu
    /// S70MR (ProductionParameters.Default, IsDefault – informacja na ekranie).
    /// </summary>
    public static Models.PzlProd.ProductionParameters ProductionParametersFrom(IReadOnlyList<DictRow> djk, IReadOnlyList<DictRow> parameters)
    {
        var defaults = Models.PzlProd.ProductionParameters.Default;
        string? Value(string name) => parameters.FirstOrDefault(r => r["Parametr"] == name)?["Wartość"];
        var shares = djk.Where(r => r["Grupa stanowisk"] is not null && Dec(r["Udział DJK"]) is not null)
            .Select(r => (r["Grupa stanowisk"]!.Trim(), Dec(r["Udział DJK"])!.Value)).ToList();
        var statuses = Statuses(Value(DeliveredStatuses));
        return new Models.PzlProd.ProductionParameters(
            djk.Count > 0 ? shares : defaults.Djk,
            Int(Value(ProductivityMonths)) ?? defaults.ProductivityMonths,
            statuses.Count > 0 ? statuses : defaults.DeliveredStatuses,
            YesNo(Value(DivideByZClo)) ?? defaults.DivideByZClo,
            IsDefault: djk.Count == 0 || parameters.Count == 0);
    }

    private static IReadOnlyList<string> Statuses(string? value) =>
        (value ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToUpperInvariant()).Distinct().ToList();

    private static bool? YesNo(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "tak" or "t" or "1" or "true" or "yes" => true,
        "nie" or "n" or "0" or "false" or "no" => false,
        _ => null,
    };

    private static IEnumerable<Issue> CostCategoryRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var at = DictionaryValidator.RowElement(i);
            if (rows[i]["Numer elementu kosztowego"] is { Length: > 10 } number)
                yield return Issue.Error($"Numer '{number}' dłuższy niż 10 znaków", at);
            if (rows[i]["Cost Category"] is null)
                yield return Issue.Warning($"Numer {rows[i]["Numer elementu kosztowego"]} bez Cost Category", at);
        }
    }

    /// <summary>
    /// Cost Category uzupełniony o cost elementy z danych ACTUALS („Uzupełnij z ACTUALS”): bieżące wiersze bez zmian
    /// i nowe wiersze dla numerów, których w słowniku nie ma (opis z danych; Cost Category i „Rozliczeniowy” – do
    /// określenia przez finansistę). Numer porównywany po normalizacji (zera wiodące, wielkość liter). Wynik dla
    /// DictionaryService.PreviewRows – podgląd pokazuje same nowe pozycje, nic nie jest usuwane.
    /// </summary>
    public static (IReadOnlyList<DictRow> Rows, int Added) CostCategoryWithActuals(IReadOnlyList<DictRow> current, IEnumerable<ActualsCostElement> elements)
    {
        var spec = Get(CostCategory);
        var number = spec.Columns[0];
        string Key(string? value) => ValueFormat.TryNormalize(number, value, out var canonical, out _) ? canonical ?? "" : value ?? "";
        var known = current.Select(r => Key(r[number.Name])).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = current.Select(r => new DictRow(null, null, r.Values)).ToList();
        var added = 0;
        foreach (var element in elements.OrderBy(e => Key(e.CostElement), StringComparer.OrdinalIgnoreCase))
        {
            if (!known.Add(Key(element.CostElement)))
                continue;
            rows.Add(new DictRow(null, null, new Dictionary<string, string?>
            {
                [number.Name] = element.CostElement, ["Opis"] = element.Name, ["Obszar"] = null, ["Cost Category"] = null, ["Rozliczeniowy"] = null,
                [BurdenGroupColumn] = null,
            }));
            added++;
        }
        return (rows, added);
    }

    /// <summary>
    /// Wartości słownika do wyboru w kolumnie powiązanej (DictColumn.Lookup): Osoby – USRID → imię i nazwisko,
    /// posortowane po opisie. Inne słowniki – klucz bez opisu.
    /// </summary>
    public static IReadOnlyList<LookupOption> LookupOptions(string code, IEnumerable<DictRow> rows) => code switch
    {
        Persons => rows.Where(r => r["USRID"] is not null)
            .Select(r => new LookupOption(r["USRID"]!, r["Imię i nazwisko"] ?? r["USRID"]!))
            .OrderBy(o => o.Label, StringComparer.CurrentCulture)
            .ToList(),
        // Pule i grupy narzutowe CAS: zapisywany identyfikator wiersza (RowId), wyświetlana nazwa.
        CasPools => rows.Where(r => r.RowId is not null && r["Pula"] is not null)
            .OrderBy(r => int.TryParse(r["Kolejność"], out var order) ? order : int.MaxValue)
            .Select(r => new LookupOption(CasBurden.Ref(r.RowId!.Value), r["Pula"]!)).ToList(),
        CasBurdenGroups => rows.Where(r => r.RowId is not null && r["Grupa"] is not null)
            .Select(r => new LookupOption(CasBurden.Ref(r.RowId!.Value), r["Grupa"]!)).OrderBy(o => o.Label, StringComparer.CurrentCulture).ToList(),
        // MPK ze stawek wydziałów (klucz MPK + rok) – raz na MPK, z opisem wydziału.
        DepartmentRates => rows.Where(r => r["MPK"] is not null).GroupBy(r => r["MPK"]!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LookupOption(g.Key, g.Select(r => r["Department"]).FirstOrDefault(d => d is not null) is { } department ? $"{g.Key} – {department}" : g.Key))
            .OrderBy(o => o.Value, StringComparer.OrdinalIgnoreCase).ToList(),
        _ => rows.Select(r => Get(code).KeyOf(r.Values)).Distinct().Select(k => new LookupOption(k, k)).ToList(),
    };

    private static int? Int(string? canonical) =>
        int.TryParse(canonical, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static decimal? Dec(string? canonical) =>
        decimal.TryParse(canonical, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateOnly? Date(string? canonical) =>
        DateOnly.TryParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();

    /// <summary>
    /// Wiersze słownika Osoby z pracowników HR (PZLHRPROD.HR.ORG): USRID – klucz, imię i nazwisko z imienia i nazwiska;
    /// USRID powtórzony w HR (kilka stanowisk) – pierwszy wiersz.
    /// </summary>
    public static IReadOnlyList<DictRow> PersonRows(IEnumerable<HrPerson> persons) =>
        persons.Where(p => p.Usrid.Length > 0)
            .GroupBy(p => p.Usrid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.LastName, StringComparer.CurrentCulture).ThenBy(p => p.FirstName, StringComparer.CurrentCulture)
            .Select(p => new DictRow(null, null, new Dictionary<string, string?>
            {
                ["USRID"] = p.Usrid, ["Imię i nazwisko"] = p.FullName.Length > 0 ? p.FullName : p.Usrid, ["Imię"] = Null(p.FirstName),
                ["Nazwisko"] = Null(p.LastName), ["E-mail"] = Null(p.Email), ["MPK"] = Null(p.CostCenter), ["Dział"] = Null(p.DepartmentShort),
                ["Dział – pełna nazwa"] = Null(p.DepartmentName), ["Stanowisko"] = Null(p.Position), ["Pion"] = Null(p.Division),
                ["Manager"] = Null(p.IsManager), ["PERNR"] = Null(p.Pernr),
            }))
            .ToList();

    /// <summary>
    /// Osoby uzupełnione o wybranych pracowników z HR („Dodaj z HR”): bieżące wiersze bez zmian i nowe wiersze dla USRID,
    /// których w słowniku nie ma – podgląd pokazuje same nowe osoby, nic nie jest usuwane.
    /// </summary>
    public static (IReadOnlyList<DictRow> Rows, int Added) PersonsWithAdded(IReadOnlyList<DictRow> current, IEnumerable<HrPerson> persons)
    {
        var known = current.Select(r => r["USRID"] ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = current.Select(r => new DictRow(null, null, r.Values)).ToList();
        var added = PersonRows(persons).Where(r => known.Add(r["USRID"] ?? "")).ToList();
        rows.AddRange(added);
        return (rows, added.Count);
    }

    /// <summary>
    /// Szukanie pracowników HR: każde słowo frazy musi wystąpić w którymś polu – USRID, PERNR (numer znaczka), imię,
    /// nazwisko, „imię nazwisko” / „nazwisko imię”, e-mail, dział (skrót i pełna nazwa). Wielkość liter bez znaczenia.
    /// </summary>
    public static IReadOnlyList<HrPerson> SearchPersons(IEnumerable<HrPerson> persons, string text, int limit = 200)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || words.Sum(w => w.Length) < 2)
            return [];
        return persons
            .Where(p => p.Usrid.Length > 0)
            .Where(p =>
            {
                string[] fields = [p.Usrid, p.Pernr, p.FirstName, p.LastName, p.FullName, $"{p.LastName} {p.FirstName}", p.Email, p.DepartmentShort, p.DepartmentName];
                return words.All(w => fields.Any(f => f.Contains(w, StringComparison.CurrentCultureIgnoreCase)));
            })
            .GroupBy(p => p.Usrid, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(p => p.LastName, StringComparer.CurrentCulture).ThenBy(p => p.FirstName, StringComparer.CurrentCulture)
            .Take(limit)
            .ToList();
    }

    private static string? Null(string value) => value.Length == 0 ? null : value;
}
