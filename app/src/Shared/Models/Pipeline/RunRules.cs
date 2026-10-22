using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Reguła walidacji przebiegu (docs/pipeline-fazy.md, rozdz. 4.1; słownik „Reguły walidacji”, edytor na ekranie Przebiegi):
/// zapytanie SQL (MS SQL) na danych uruchomienia – każdy zwrócony wiersz to jedna nieprawidłowość z poziomem reguły, brak wierszy –
/// reguła spełniona; opis wyniku z wartościami kolumn w { } (puste – „Kolumna: wartość; …”).
/// </summary>
public sealed record RunRule(string Name, CheckLevel Level, bool IsActive, string Sql, string? ResultTemplate = null, string? Description = null);

/// <summary>Tabela danych uruchomienia dostępna w zapytaniu reguły (podpowiedź w edytorze).</summary>
public sealed record RunRuleTable(string Name, string Description, IReadOnlyList<string> Columns);

/// <summary>Kolumna wyniku zapytania reguły: nazwa i czy liczbowa (porównanie uruchomień – różnice w kolumnach liczbowych).</summary>
public sealed record RunRuleColumn(string Name, bool IsNumber);

/// <summary>
/// Wynik zapytania reguły: kolumny i wiersze (najwyżej RunRules.MaxRows – Truncated); Error – błąd SQL Server (z numerem linii
/// zapytania) albo zapytanie odrzucone (RunRules.Check).
/// </summary>
public sealed record RunRuleResult(IReadOnlyList<RunRuleColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, bool Truncated = false, string? Error = null)
{
    public static RunRuleResult Failed(string error) => new([], [], false, error);
}

/// <summary>
/// Dane przebiegu w zapytaniu reguły (podstawia aplikacja): uruchomienie, poprzednie uruchomienie do porównania (null – brak), program,
/// okresy, waluta kosztów, typ programu.
/// </summary>
public sealed record RunRuleContext(long PinId, long? PreviousPinId, string Project, (int Year, int Period) ClosedBefore, (int Year, int Period) Today,
    string? Currency, string ProjectType);

/// <summary>
/// Reguły walidacji w SQL: tabele i parametry dostępne w zapytaniu, kontrola zapytania (tylko pojedynczy SELECT / WITH – przy zapisie
/// i wykonaniu), złożenie zapytania z danymi uruchomienia (CTE), opis wyniku, wiersz wyniku do porównań (JSON), reguły startowe.
/// </summary>
public static partial class RunRules
{
    public const string ActualsArea = "Walidacja ACTUALS";
    public const string EvArea = "Walidacja EV";

    /// <summary>Najwyżej tyle wierszy wyniku jednej reguły (więcej – wynik obcięty z ostrzeżeniem).</summary>
    public const int MaxRows = 500;

    /// <summary>Limit czasu zapytania reguły (s).</summary>
    public const int TimeoutSeconds = 30;

    /// <summary>Typ programu w szablonie przebiegu: wszystkie albo kod typu (META_Project.ProjectType).</summary>
    public const string AllTypes = "Wszystkie";
    public static IReadOnlyList<string> ProjectTypes { get; } = [AllTypes, "WEWNETRZNY", "SAC", "CAS"];

    /// <summary>Wariant przebiegu w słowniku „Definicje przebiegów” (nieużywany – „Każdy”).</summary>
    public const string AnyRun = "Każdy";
    public static IReadOnlyList<string> RunKinds { get; } = [AnyRun, "Zamykający", "W środku okresu"];

    /// <summary>Poziom reguły; INFO – wynik informacyjny (PASS).</summary>
    public static IReadOnlyList<string> Levels { get; } = ["ERROR", "WARNING", "INFO"];

    public static CheckLevel? LevelOf(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "ERROR" => CheckLevel.Error,
        "WARNING" => CheckLevel.Warning,
        "INFO" => CheckLevel.Pass,
        _ => null,
    };

    public static string LevelText(CheckLevel level) => level switch { CheckLevel.Error => "ERROR", CheckLevel.Warning => "WARNING", _ => "INFO" };

    /// <summary>Tabele danych uruchomienia (CTE złożone przez aplikację przed zapytaniem reguły).</summary>
    public static IReadOnlyList<RunRuleTable> Tables { get; } =
    [
        new("biezace", "ACTUALS przypięte w uruchomieniu – cała pula (także rozliczeniowe), przed narzutami CAS, bez Wykluczeń; wiersz = element WBS × cost element × rok × okres × rozliczeniowy × waluta",
            ["ElementWBS (NULL – sieci, zlecenia)", "CostElement", "Rok", "Okres", "RokOkres", "OkresyWstecz", "Rozliczeniowy (0/1)", "Waluta", "Wiersze (liczba wierszy źródłowych)",
             "Kwota", "Utworzono (najpóźniejsza data utworzenia)", "OkresZamkniety (0/1)", "OkresPrzyszly (0/1)"]),
        new("poprzednie", "To samo z poprzedniego przebiegu (ostatnie uruchomienie wcześniejszego tygodnia z tym samym polem kwoty); pusta – brak (@JestPoprzedni = 0)",
            ["jak biezace"]),
        new("ev", "Dane jak w strukturze programu: bez rozliczeniowych, po Wykluczeniach (bez partnera), CAS po narzutach – w walucie programu",
            ["ElementWBS", "CostElement", "Rok", "Okres", "RokOkres", "OkresyWstecz", "OkresZamkniety (0/1)", "RodzajKosztu (Hours / Materials / Other)", "Kwota"]),
        new("ev_poprzednie", "To samo z poprzedniego przebiegu", ["jak ev"]),
        new("cost_category", "Cost Category programu (globalny i zmiany programu – wiersz programu przed globalnym)",
            ["CostElement", "Opis", "CostCategory (NULL – pusta)", "RodzajKosztu (Hours / Materials / Other)", "Rozliczeniowy (0/1)"]),
        new("wp_cam", "WP i CAM programu (wiersz = element P1S)", ["ElementP1S", "WP", "CAM", "MPK"]),
        new("stawki", "Stawki wydziałów (globalne)", ["MPK", "Rok", "LaborRate"]),
        new("harmonogram", "Harmonogram i budżet programu", ["WP", "BAC", "BacHours", "BacMaterial", "Waluta"]),
    ];

    /// <summary>Kolumny wyniku z własnym znaczeniem w programie (podpowiedź w edytorze).</summary>
    public static RunRuleTable ResultColumns { get; } = new("kolumny wyniku",
        "Opcjonalne kolumny o znaczeniu w zakładce Przebieg programu: Problem i Wskazowka – problem wiersza (zamiast opisu wyniku); "
        + "ElementWBS, CostElement, RodzajKosztu, RokOkres (albo Rok i Okres) – zakres linii ACTUALS wiersza i „Pokaż zmiany”; "
        + "Slownik i Filtr – „Popraw w aplikacji”: słownik (nazwa albo kod – słownik programu, np. WP i CAM, albo globalny, np. Stawki wydziałów) "
        + "i wartość filtra jego tabeli",
        ["Problem", "Wskazowka", "ElementWBS", "CostElement", "RodzajKosztu", "RokOkres", "Slownik", "Filtr"]);

    /// <summary>Parametry zapytania (wartości uruchomienia).</summary>
    public static IReadOnlyList<RunRuleTable> Parameters { get; } =
    [
        new("@BiezacyOkres", "Bieżący okres kalendarza okresów, tekst „RRRR/OO” (porównania z RokOkres)", []),
        new("@PierwszyOtwartyOkres", "Pierwszy niezamknięty okres „RRRR/OO” (w pierwszych 7 dniach okresu – poprzedni); wcześniejsze – zamknięte", []),
        new("@JestPoprzedni", "1 – jest poprzedni przebieg do porównania, 0 – brak (pierwszy przebieg albo inne pole kwoty)", []),
        new("@WalutaKosztow", "Waluta pola kwoty ACTUALS (PLN – waluta obiektu; inne pole – przeważająca w danych)", []),
        new("@TypProgramu", "Typ programu (WEWNETRZNY, SAC, CAS)", []),
        new("@Program", "Kod programu", []),
    ];

    /// <summary>
    /// Kolumny wyniku z problemem wiersza i wskazówką (opcjonalne): w programie zamiast opisu wyniku – różne przyczyny w wierszach
    /// jednej reguły (np. netowanie: rozliczenie niepełne / większe niż koszt / brak rozliczenia).
    /// </summary>
    public const string ProblemColumn = "Problem";
    public const string HintColumn = "Wskazowka";

    /// <summary>Kolumny „Popraw w aplikacji”: słownik do poprawy i wartość filtra jego tabeli (bez kolumny w tabeli wyniku).</summary>
    public const string DictionaryColumn = "Slownik";
    public const string FilterColumn = "Filtr";

    /// <summary>Kolumny wyniku wskazujące zakres okna „Zmiany ACWP” („Pokaż zmiany”) i element wyniku – w tej kolejności.</summary>
    public static IReadOnlyList<string> ElementColumns { get; } = ["ElementWBS", "CostElement", "RodzajKosztu", "WP", "MPK"];

    // Słowa kluczowe zmieniające dane, schemat, uprawnienia, transakcję albo sesję – niedozwolone w regule (także w podzapytaniach).
    private static readonly HashSet<string> Forbidden = new(
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "EXECUTE", "DROP", "ALTER", "CREATE", "TRUNCATE", "GRANT", "REVOKE", "DENY", "INTO", "BULK",
        "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "UPDATETEXT", "WRITETEXT", "BACKUP", "RESTORE", "DBCC", "SHUTDOWN", "KILL", "RECONFIGURE",
        "USE", "DECLARE", "SET", "BEGIN", "COMMIT", "ROLLBACK", "SAVE", "WAITFOR", "GO",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Kontrola zapytania reguły (przy zapisie i przed wykonaniem): niepuste, jedno zapytanie SELECT albo WITH … SELECT, bez średnika
    /// i bez instrukcji zmieniających dane, schemat, uprawnienia albo transakcję (INSERT, UPDATE, DELETE, MERGE, EXEC, DROP, ALTER,
    /// CREATE, TRUNCATE, GRANT, SELECT … INTO …), bez procedur sp_ / xp_. null – zapytanie dopuszczalne.
    /// </summary>
    public static string? Check(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "Zapytanie SQL puste";
        var (code, error) = Code(sql);
        if (error is not null)
            return error;
        if (code.Contains(';'))
            return "Średnik niedozwolony – reguła to jedno zapytanie SELECT (bez średnika na końcu)";
        var words = WordPattern().Matches(code).Select(m => m.Value).ToList();
        if (words.Count == 0 || !(Is(words[0], "SELECT") || Is(words[0], "WITH")))
            return "Zapytanie musi zaczynać się od SELECT albo WITH";
        var forbidden = words.Where(w => Forbidden.Contains(w)).Select(w => w.ToUpperInvariant()).Distinct().ToList();
        if (forbidden.Count > 0)
            return $"Niedozwolone w regule: {string.Join(", ", forbidden)} – reguła tylko odczytuje dane (SELECT)";
        var procedures = words.Where(w => w.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) || w.StartsWith("xp_", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        return procedures.Count > 0 ? $"Procedury niedozwolone w regule: {string.Join(", ", procedures)}" : null;
    }

    /// <summary>
    /// Zapytanie do wykonania: tabele danych uruchomienia (withClause – „WITH biezace AS (…), …” bez zapytania) i zapytanie reguły od
    /// nowej linii; zapytanie zaczynające się od WITH dołącza swoje CTE do listy (WITH → przecinek, w tym samym miejscu). Offset – linie
    /// przed zapytaniem reguły (numer linii błędu SQL Server − Offset = linia w zapytaniu reguły).
    /// </summary>
    public static (string Text, int Offset) Compose(string withClause, string sql)
    {
        var (code, _) = Code(sql);
        var first = WordPattern().Match(code);
        var body = first.Success && Is(first.Value, "WITH") ? sql[..first.Index] + ",   " + sql[(first.Index + 4)..] : sql;
        var prefix = withClause.TrimEnd() + "\n";
        return (prefix + body, prefix.Count(c => c == '\n'));
    }

    /// <summary>Tabele danych uruchomienia użyte w zapytaniu (lista reguł w edytorze).</summary>
    public static IReadOnlyList<string> TablesUsed(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];
        var words = WordPattern().Matches(Code(sql).Code).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Tables.Select(t => t.Name).Where(words.Contains).ToList();
    }

    /// <summary>
    /// Opis wyniku: szablon z nazwami kolumn w { } (wielkość liter bez znaczenia; nieznana kolumna zostaje jak w szablonie), a pusty –
    /// „Kolumna: wartość; …”. Wartości – Format.
    /// </summary>
    public static string Describe(string? template, IReadOnlyList<RunRuleColumn> columns, IReadOnlyList<object?> row)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Join("; ", columns.Select((c, i) => $"{c.Name}: {Format(row[i])}"));
        return PlaceholderPattern().Replace(template.Trim(), m =>
        {
            var index = IndexOf(columns, m.Groups[1].Value.Trim());
            return index < 0 ? m.Value : Format(row[index]);
        });
    }

    /// <summary>Nazwy w { } szablonu opisu, których nie ma w kolumnach wyniku (test reguły).</summary>
    public static IReadOnlyList<string> UnknownPlaceholders(string? template, IReadOnlyList<RunRuleColumn> columns) =>
        string.IsNullOrWhiteSpace(template) ? []
            : PlaceholderPattern().Matches(template).Select(m => m.Groups[1].Value.Trim()).Where(n => IndexOf(columns, n) < 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Wartość w opisie: liczby dziesiętne – format polski z 2 miejscami i separatorem tysięcy, całkowite – bez separatora (lata, okresy,
    /// liczby wierszy), daty – RRRR-MM-DD, logiczne – tak / nie, NULL – „(brak)”.
    /// </summary>
    public static string Format(object? value) => value switch
    {
        null or DBNull => "(brak)",
        decimal d => d.ToString("#,0.00", Pl),
        double d => ((decimal)d).ToString("#,0.00", Pl),
        float f => ((decimal)f).ToString("#,0.00", Pl),
        int or long or short or byte => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        bool b => b ? "tak" : "nie",
        DateTime t => t.TimeOfDay == TimeSpan.Zero ? t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateTimeOffset t => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Element wyniku: pierwsza niepusta kolumna ElementColumns (element WBS, cost element, rodzaj kosztu, WP, MPK); null – brak.</summary>
    public static (string Column, string Value)? ElementOf(IReadOnlyList<RunRuleColumn> columns, IReadOnlyList<object?> row)
    {
        foreach (var name in ElementColumns)
            if (IndexOf(columns, name) is var index and >= 0 && row[index] is { } value and not DBNull && Format(value) is { Length: > 0 } text)
                return (columns[index].Name, text);
        return null;
    }

    /// <summary>Wiersz wyniku do zapisu (RUN_Issue.Details): obiekt JSON kolumna → wartość (liczby jako liczby, daty jako tekst RRRR-MM-DD).</summary>
    public static string Details(IReadOnlyList<RunRuleColumn> columns, IReadOnlyList<object?> row)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            for (var i = 0; i < columns.Count; i++)
            {
                json.WritePropertyName(columns[i].Name);
                switch (row[i])
                {
                    case null or DBNull: json.WriteNullValue(); break;
                    case decimal d: json.WriteNumberValue(d); break;
                    case double d: json.WriteNumberValue(d); break;
                    case float f: json.WriteNumberValue(f); break;
                    case int or long or short or byte: json.WriteNumberValue(Convert.ToInt64(row[i], CultureInfo.InvariantCulture)); break;
                    case bool b: json.WriteBooleanValue(b); break;
                    default: json.WriteStringValue(Format(row[i])); break;
                }
            }
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Wiersz wyniku z RUN_Issue.Details: kolumna → tekst (Text) albo liczba (Number); nieczytelny – pusty.</summary>
    public static IReadOnlyList<(string Column, string? Text, decimal? Number)> ReadDetails(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
            return [];
        try
        {
            using var document = JsonDocument.Parse(details);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [];
            return document.RootElement.EnumerateObject().Select(p => p.Value.ValueKind switch
            {
                JsonValueKind.Number => (p.Name, (string?)null, (decimal?)p.Value.GetDecimal()),
                JsonValueKind.String => (p.Name, p.Value.GetString(), null),
                JsonValueKind.True => (p.Name, "tak", null),
                JsonValueKind.False => (p.Name, "nie", null),
                _ => (p.Name, null, null),
            }).ToList();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException)
        {
            return [];
        }
    }

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private static int IndexOf(IReadOnlyList<RunRuleColumn> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static bool Is(string word, string keyword) => string.Equals(word, keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Kod zapytania bez komentarzy (-- i /* */), tekstów w apostrofach ('…', N'…') i nazw w nawiasach ([…], "…") – zastąpione
    /// spacjami (ta sama długość, pozycje bez zmian); błąd – niezamknięty tekst, komentarz albo nawias.
    /// </summary>
    private static (string Code, string? Error) Code(string sql)
    {
        var code = new StringBuilder(sql);
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            int end;
            string? unclosed = null;
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                end = sql.IndexOf('\n', i) is var nl and >= 0 ? nl : sql.Length;
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal) is var close and >= 0 ? close + 2 : -1;
                unclosed = "Niezamknięty komentarz /* … */";
            }
            else if (c is '\'' or '"')
            {
                end = Closing(sql, i, c);
                unclosed = c == '\'' ? "Niezamknięty tekst w apostrofach" : "Niezamknięta nazwa w cudzysłowie";
            }
            else if (c == '[')
            {
                end = sql.IndexOf(']', i + 1) is var close and >= 0 ? close + 1 : -1;
                unclosed = "Niezamknięty nawias [ ]";
            }
            else
            {
                i++;
                continue;
            }
            if (end < 0)
                return ("", unclosed);
            for (var k = i; k < end; k++)
                if (code[k] != '\n')
                    code[k] = ' ';
            i = end;
        }
        return (code.ToString(), null);
    }

    // Koniec tekstu w apostrofach / cudzysłowie (podwojony znak – znak w tekście); -1 – niezamknięty.
    private static int Closing(string sql, int start, char quote)
    {
        for (var k = start + 1; k < sql.Length; k++)
            if (sql[k] == quote)
            {
                if (k + 1 < sql.Length && sql[k + 1] == quote)
                    k++;
                else
                    return k + 1;
            }
        return -1;
    }

    [GeneratedRegex(@"[A-Za-z_@#][A-Za-z0-9_@#$]*")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderPattern();
}
