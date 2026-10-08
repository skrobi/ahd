using PzlEv.Shared.Models.Hr;
using System.Globalization;
using System.Text.RegularExpressions;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>Słowniki globalne (docs/slowniki.md, rozdz. 2) z regułami rozdz. 5.4–5.5 i 5.7.</summary>
public static partial class GlobalDictionaries
{
    public const string Calendar = "calendar";
    public const string DepartmentRates = "department-rates";
    public const string FxRates = "fx-rates";
    public const string CostCategory = "cost-category";
    public const string Persons = "persons";
    public const string MappingReport = "mapping-report";

    /// <summary>
    /// Kolumny raportu mapowań SAP↔CES (docs/mapowanie-ces-p1s.md, rozdz. 2) – nazwy jak w nagłówku pliku, w bazie
    /// kolumny DICT_MappingReport (project → ProjectDef – Project to kolumna słowników projektu). Rozstrzyganie czyta
    /// src, pspnr, pspnr_sap, project_sap, project_ces, wbs, wbs_sap, wbs_ces; pozostałe kolumny są przechowywane, żeby
    /// słownik można było pobrać do Excela w pełnym układzie.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Column)> MappingReportColumns =
    [
        ("src", "Src"), ("pspnr", "Pspnr"), ("pspnr_sap", "PspnrSap"), ("pspnr_ces", "PspnrCes"), ("pspnr_parent", "PspnrParent"),
        ("project", "ProjectDef"), ("project_sap", "ProjectSap"), ("project_sap_org", "ProjectSapOrg"), ("project_ces", "ProjectCes"),
        ("wbs", "Wbs"), ("wbs_sap", "WbsSap"), ("wbs_ces", "WbsCes"), ("wbs_desc", "WbsDesc"), ("wbs_desc_sap", "WbsDescSap"),
        ("wbs_desc_ces", "WbsDescCes"), ("prctr", "Prctr"), ("prctr_sap", "PrctrSap"), ("prctr_ces", "PrctrCes"), ("lvl", "Lvl"),
        ("lvl_sap", "LvlSap"), ("lvl_ces", "LvlCes"), ("perf_obg", "PerfObg"), ("techs", "Techs"), ("sales_order_typ", "SalesOrderTyp"),
        ("sales_order", "SalesOrder"), ("sales_order_pos", "SalesOrderPos"), ("matnr", "Matnr"), ("network", "Network"),
    ];

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
            Description = "Kurs waluty w okresie (do PLN) – przeliczenia walut w łączeniu źródeł (P3) i obliczeniu EV (P8).",
            Columns =
            [
                new("Waluta", ColumnType.Text, Key: true),
                new("Okres", ColumnType.Text, Key: true),
                new("Kurs", ColumnType.Decimal, Required: true),
            ],
            Rules = FxRules,
        },
        new()
        {
            Code = CostCategory,
            Name = "Cost Category",
            Description = "Numer elementu kosztowego → opis, obszar, Cost Category. Numer liczbowy uzupełniany zerami do 10 znaków.",
            Columns =
            [
                new("Numer elementu kosztowego", ColumnType.Text, Key: true, PadNumericTo: 10),
                new("Opis", ColumnType.Text),
                new("Obszar", ColumnType.Text, CheckSimilar: true),
                new("Cost Category", ColumnType.Text, CheckSimilar: true),
            ],
            Rules = CostCategoryRules,
        },
        new()
        {
            Code = Persons,
            Name = "Osoby",
            Description = "Pracownicy z HR (PZLHRPROD, HR.ORG – „Wczytaj z HR”): USRID (numer znaczka, login) → imię i nazwisko, MPK, dział, stanowisko. CAM w strukturze projektu wybiera się z tej listy (zapisywany USRID).",
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
            Code = MappingReport,
            Name = "Raport mapowań CES ↔ P1S",
            Description = "Raport mapowań SAP↔CES (eksport z PZLPROD) – źródło przypisań elementów CES do P1S (ekran Mapowanie). " +
                          "Wczytanie z Excela zastępuje cały słownik zawartością pliku: wiersze spoza pliku są usuwane (historia zostaje).",
            Columns = [.. MappingReportColumns.Select(c => c.Name switch
            {
                "src" => new DictColumn(c.Name, ColumnType.Choice, Key: true, Choices: ["SAP", "CES"]),
                "pspnr" => new DictColumn(c.Name, ColumnType.Text, Key: true),
                _ => new DictColumn(c.Name, ColumnType.Text),
            })],
            Rules = MappingReportRules,
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
        new(Get(FxRates), "dict.FxRate", [("Waluta", "Currency"), ("Okres", "Period"), ("Kurs", "Rate")]),
        new(Get(CostCategory), "dict.CostCategory",
            [("Numer elementu kosztowego", "CostElement"), ("Opis", "Description"), ("Obszar", "Area"), ("Cost Category", "CostCategory")]),
        new(Get(Persons), "dict.Person",
        [
            ("USRID", "AdAccount"), ("Imię i nazwisko", "FullName"), ("Imię", "FirstName"), ("Nazwisko", "LastName"), ("E-mail", "Email"),
            ("MPK", "CostCenter"), ("Dział", "DepartmentShort"), ("Dział – pełna nazwa", "DepartmentName"), ("Stanowisko", "Position"),
            ("Pion", "Division"), ("Manager", "IsManager"), ("PERNR", "Pernr"),
        ]),
        new(Get(MappingReport), "dict.MappingReport", MappingReportColumns),
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
            if (rows[i]["Okres"] is { } period && !PeriodPattern().IsMatch(period))
                yield return Issue.Error($"Okres '{period}' – oczekiwano RRRR-MM", at);
            if (Dec(rows[i]["Kurs"]) is { } rate && rate <= 0)
                yield return Issue.Error("Kurs musi być większy od 0", at);
        }
    }

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
    /// Wiersz raportu mapowań bez przypisania (rozstrzyganie go pominie): brak elementu CES (wbs_ces, a w wierszu CES – wbs)
    /// albo celu P1S (pspnr_sap / wbs_sap, a w wierszu SAP – pspnr / wbs) i brak pary project_ces → project_sap.
    /// </summary>
    private static IEnumerable<Issue> MappingReportRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var ces = row["src"] == "CES";
            var element = row["wbs_ces"] ?? (ces ? row["wbs"] : null);
            var target = row["pspnr_sap"] ?? row["wbs_sap"] ?? (ces ? null : row["pspnr"] ?? row["wbs"]);
            var project = row["project_ces"] is not null && row["project_sap"] is not null;
            if (ces && (element is null || target is null) && !project)
                yield return Issue.Warning($"Wiersz CES {row["pspnr"]} bez przypisania (brak wbs_ces / wbs albo pspnr_sap / wbs_sap i pary project_ces → project_sap)",
                    DictionaryValidator.RowElement(i));
        }
    }

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

    private static string? Null(string value) => value.Length == 0 ? null : value;
}
