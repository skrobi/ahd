using System.Globalization;
using System.Text.RegularExpressions;
using PzlEv.Modules.MasterData.Models;
using PzlEv.Shared.Models;

namespace PzlEv.Modules.MasterData.Services;

/// <summary>Słowniki globalne (docs/slowniki.md, rozdz. 2) z regułami rozdz. 5.4–5.5.</summary>
public static partial class GlobalDictionaries
{
    public const string Calendar = "calendar";
    public const string DepartmentRates = "department-rates";
    public const string FxRates = "fx-rates";
    public const string CostCategory = "cost-category";
    public const string Persons = "persons";

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
            Description = "Stawka i narzut wydziału na rok – przeliczenie godzin na koszt w łączeniu źródeł (P3).",
            Columns =
            [
                new("Department", ColumnType.Text, Key: true),
                new("Year", ColumnType.Integer, Key: true),
                new("Labor Rate", ColumnType.Decimal, Required: true),
                new("Overhead", ColumnType.Decimal, Required: true),
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
            Description = "Osoby pełniące funkcję CAM – wybór CAM w słowniku „WP i CAM”.",
            Columns =
            [
                new("Konto AD", ColumnType.Text, Key: true),
                new("Imię i nazwisko", ColumnType.Text, Required: true, CheckSimilar: true),
            ],
            Rules = PersonRules,
        },
    ];

    public static DictionarySpec Get(string code) => All.First(s => s.Code == code);

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

    private static IEnumerable<Issue> PersonRules(IReadOnlyList<DictRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i]["Konto AD"] is { } account && !account.Contains('\\'))
                yield return Issue.Warning($"Konto '{account}' bez domeny (oczekiwano DOMENA\\login)", DictionaryValidator.RowElement(i));
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
}
