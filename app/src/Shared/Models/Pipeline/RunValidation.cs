using System.Globalization;
using System.Text;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Shared.Models.Pipeline;

/// <summary>
/// Reguła walidacji danych przebiegu zapisana formułami (słownik „Reguły walidacji”, okno Kontrole – docs/pipeline-fazy.md,
/// rozdz. 4.1): kontrola (wiersz macierzy wyników), filtr wierszy, grupowanie, warunek grupy, kwota, komunikat, komunikat OK
/// (gdy żadna grupa nie spełnia warunku – wynik INFO), najwyżej N wyników (od największej kwoty), poziom, próg ([Próg]).
/// </summary>
public sealed record RunValidationRule(string Name, string Check, bool IsActive, string? Filter, string? GroupBy, string Condition, string? Amount,
    string Message, string? OkMessage, int Top, CheckLevel Level, decimal? Threshold, string? Description);

/// <summary>Wynik reguły walidacji: poziom, element (pierwsze pole grupowania), komunikat, kwota.</summary>
public sealed record RunValidationResult(CheckLevel Level, string? Element, string Message, decimal? Amount);

/// <summary>
/// Dane reguł walidacji: wiersze przypięte w uruchomieniu (cała pula ACTUALS programu, przed narzutami) i w poprzednim
/// uruchomieniu (null – pierwszy przebieg), cost elementy Cost Category programu, okresy zamknięte (przed ClosedBefore), okres
/// bieżący, waluta kosztów (null – przeważająca w danych).
/// </summary>
public sealed record RunValidationData(IReadOnlyList<RunBalanceRow> Current, IReadOnlyList<RunBalanceRow>? Previous,
    IReadOnlySet<string> CostElementsInDictionary, (int Year, int Period) ClosedBefore, (int Year, int Period) Today, string? Currency);

/// <summary>Pole albo funkcja języka reguł – podpowiedź w edytorze reguł.</summary>
public sealed record RunValidationTerm(string Name, string Kind, string Description);

/// <summary>
/// Język reguł walidacji: pola wiersza ([Kwota], [Element WBS] …), wartości przebiegu ([Próg], [Jest poprzedni] …), liczby
/// (przecinek albo kropka), teksty w cudzysłowie, + − * /, &amp; (łączenie tekstu), = &lt;&gt; &lt; &lt;= &gt; &gt;=, ORAZ, LUB, NIE,
/// funkcje (argumenty po średniku): ABS, JEŻELI, PUSTE, ZAOKR, TEKST; agregaty grupy: SUMA, LICZBA, MAKS, MIN, LISTA, NAJWIĘKSZE –
/// z przedrostkiem POPRZ. na danych poprzedniego przebiegu (ta sama grupa), CAŁOŚĆ. na wszystkich wierszach po filtrze.
/// </summary>
public static class RunValidationLanguage
{
    public const string Previous = "POPRZ.";
    public const string Whole = "CAŁOŚĆ.";

    /// <summary>Pola wiersza danych przypiętych.</summary>
    public static IReadOnlyList<RunValidationTerm> RowFields { get; } =
    [
        new("Element WBS", "pole", "Element WBS wiersza (pusty – sieci, zlecenia przypisane do programu)."),
        new("Cost element", "pole", "Numer elementu kosztowego."),
        new("Rok", "pole", "Rok obrachunkowy."),
        new("Okres", "pole", "Okres SAP (1–16)."),
        new("Rok/okres", "pole", "Tekst „RRRR/OO” (okresy 13–16 jak 12) – do grupowania, porównań i komunikatów."),
        new("Rozliczeniowy", "pole", "Cost element rozliczeniowy (Cost Category, kolumna Rozliczeniowy) – rozliczenie wychodzące."),
        new("Waluta", "pole", "Waluta kwoty wiersza."),
        new("Wiersze", "pole", "Liczba wierszy źródłowych ACTUALS w pozycji (pozycja = WBS × cost element × rok × okres × rozliczenie × waluta)."),
        new("Kwota", "pole", "Kwota pozycji (pole kwoty ACTUALS programu, przed narzutami CAS)."),
        new("Utworzono", "pole", "Najpóźniejsza data utworzenia wiersza w pozycji."),
        new("Okres zamknięty", "pole", "Okres przed bieżącym okresem kalendarza (w pierwszych 7 dniach okresu – przed poprzednim)."),
        new("Okres przyszły", "pole", "Okres po bieżącym okresie kalendarza."),
        new("Okres bieżący", "pole", "Bieżący okres kalendarza okresów."),
        new("W Cost Category", "pole", "Cost element jest w słowniku Cost Category programu (globalny i zmiany programu)."),
    ];

    /// <summary>Wartości przebiegu (te same dla wszystkich wierszy).</summary>
    public static IReadOnlyList<RunValidationTerm> ContextFields { get; } =
    [
        new("Próg", "przebieg", "Próg reguły (puste – 0); w szablonie przebiegu można go zmienić na kafelku etapu."),
        new("Jest poprzedni", "przebieg", "Jest poprzedni przebieg do porównania (ostatnie uruchomienie wcześniejszego tygodnia z tym samym polem kwoty)."),
        new("Waluta kosztów", "przebieg", "Waluta pola kwoty ACTUALS (PLN – waluta obiektu; inne pole – przeważająca w danych)."),
        new("Bieżący okres", "przebieg", "Bieżący okres kalendarza, tekst „RRRR/OO”."),
        new("Pierwszy otwarty okres", "przebieg", "Pierwszy niezamknięty okres, tekst „RRRR/OO” (wcześniejsze – zamknięte)."),
    ];

    public static IReadOnlyList<RunValidationTerm> Functions { get; } =
    [
        new("SUMA(wartość; warunek)", "agregat", "Suma wartości wierszy grupy (warunek opcjonalny), np. SUMA([Kwota]; NIE [Rozliczeniowy])."),
        new("LICZBA(warunek)", "agregat", "Liczba pozycji grupy (warunek opcjonalny); wiersze źródłowe – SUMA([Wiersze])."),
        new("MAKS(wartość; warunek)", "agregat", "Największa wartość (liczba, tekst, data) w grupie; brak – puste."),
        new("MIN(wartość; warunek)", "agregat", "Najmniejsza wartość w grupie; brak – puste."),
        new("LISTA(wartość; n)", "agregat", "Różne wartości w grupie po przecinku (najwyżej n, domyślnie 5)."),
        new("NAJWIĘKSZE(klucz; wartość; n; warunek)", "agregat", "n kluczy (domyślnie 3) z największą |sumą| wartości, np. NAJWIĘKSZE([Cost element]; [Kwota]; 3)."),
        new("POPRZ.SUMA(…), POPRZ.LICZBA(…) …", "agregat", "Agregat na danych poprzedniego przebiegu – ta sama grupa i filtr; brak poprzedniego – 0 / puste."),
        new("CAŁOŚĆ.SUMA(…), CAŁOŚĆ.LICZBA(…) …", "agregat", "Agregat na wszystkich wierszach po filtrze (np. saldo całego programu)."),
        new("ABS(x)", "funkcja", "Wartość bezwzględna."),
        new("JEŻELI(warunek; tak; nie)", "funkcja", "Wartość zależna od warunku."),
        new("PUSTE(x)", "funkcja", "Brak wartości albo pusty tekst."),
        new("ZAOKR(x; n)", "funkcja", "Zaokrąglenie do n miejsc."),
        new("TEKST(x)", "funkcja", "Wartość jako tekst (liczby – format polski)."),
        new("a & b", "operator", "Łączenie tekstów."),
        new("ORAZ, LUB, NIE", "operator", "Warunki logiczne; PRAWDA, FAŁSZ – stałe."),
    ];

    /// <summary>Kontrola składni reguły: każda formuła, pola grupowania, pola spoza grupowania poza agregatem, agregaty w filtrze.</summary>
    public static IReadOnlyList<string> Validate(RunValidationRule rule) => RunValidationProgram.Compile(rule).Errors;

    /// <summary>Wyniki reguły na danych; Error – reguła niepoprawna albo błąd obliczenia (wtedy bez wyników).</summary>
    public static (IReadOnlyList<RunValidationResult> Results, string? Error) Evaluate(RunValidationRule rule, RunValidationData data, decimal? threshold = null)
    {
        var program = RunValidationProgram.Compile(rule);
        if (program.Errors.Count > 0)
            return ([], string.Join("; ", program.Errors));
        try
        {
            return (program.Run(data, threshold ?? rule.Threshold, rule.Level, rule.Top), null);
        }
        catch (InvalidOperationException ex)
        {
            return ([], ex.Message);
        }
    }

    /// <summary>Wartość w komunikacie: liczby – format polski (2 miejsca po przecinku, bez zbędnych zer), daty – RRRR-MM-DD, logiczne – tak / nie.</summary>
    public static string Show(object? value) => value switch
    {
        null => "",
        decimal d => PolishNumber.ToDisplay(Math.Round(d, 2)),
        bool b => b ? "tak" : "nie",
        DateTime t => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    internal static string PeriodText(int year, int period) => $"{year}/{Math.Min(period, 12):00}";
}

/// <summary>Reguła walidacji po kompilacji: drzewa wyrażeń części reguły i ich wykonanie na danych.</summary>
internal sealed class RunValidationProgram
{
    private abstract record Node;
    private sealed record Const(object? Value) : Node;
    private sealed record Field(string Name) : Node;
    private sealed record Unary(string Op, Node Operand) : Node;
    private sealed record Binary(string Op, Node Left, Node Right) : Node;
    private sealed record Call(string Name, string? Scope, IReadOnlyList<Node> Args) : Node;
    private sealed record Template(IReadOnlyList<object> Parts) : Node;   // string albo Node

    private static readonly HashSet<string> Aggregates = new(["SUMA", "LICZBA", "MAKS", "MIN", "LISTA", "NAJWIĘKSZE"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Scalars = new(["ABS", "JEŻELI", "PUSTE", "ZAOKR", "TEKST"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RowFieldNames = new(RunValidationLanguage.RowFields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ContextFieldNames = new(RunValidationLanguage.ContextFields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

    private Node? _filter;
    private IReadOnlyList<string> _groupBy = [];
    private Node? _condition;
    private Node? _amount;
    private Node? _message;
    private Node? _ok;

    public List<string> Errors { get; } = [];

    public static RunValidationProgram Compile(RunValidationRule rule)
    {
        var program = new RunValidationProgram();
        program._groupBy = program.ParseGroupBy(rule.GroupBy);
        program._filter = program.Part("Filtr", rule.Filter, template: false, rowLevel: true, required: false);
        program._condition = program.Part("Warunek", rule.Condition, template: false, rowLevel: false, required: true);
        program._amount = program.Part("Kwota", rule.Amount, template: false, rowLevel: false, required: false);
        program._message = program.Part("Komunikat", rule.Message, template: true, rowLevel: false, required: true);
        program._ok = program.Part("Komunikat OK", rule.OkMessage, template: true, rowLevel: false, required: false, whole: true);
        return program;
    }

    private IReadOnlyList<string> ParseGroupBy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var fields = new List<string>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part.StartsWith('[') && part.EndsWith(']') ? part[1..^1].Trim() : part;
            if (!RowFieldNames.Contains(name))
                Errors.Add($"Grupuj: „{part}” – pole wiersza w nawiasach kwadratowych, kilka – po średniku (np. [Element WBS]; [Rok/okres])");
            else
                fields.Add(RunValidationLanguage.RowFields.First(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Name);
        }
        return fields;
    }

    private Node? Part(string label, string? text, bool template, bool rowLevel, bool required, bool whole = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required)
                Errors.Add($"{label}: puste");
            return null;
        }
        try
        {
            var parser = new Parser(text);
            var node = template ? parser.Template() : parser.Whole();
            Check(label, node, rowLevel, whole, insideAggregate: false);
            return node;
        }
        catch (FormatException ex)
        {
            Errors.Add($"{label}: {ex.Message}");
            return null;
        }
    }

    // Kontrola pól i agregatów: filtr – bez agregatów; poza agregatem w grupie – tylko pola grupowania i wartości przebiegu.
    private void Check(string label, Node node, bool rowLevel, bool whole, bool insideAggregate)
    {
        switch (node)
        {
            case Field f when ContextFieldNames.Contains(f.Name):
                break;
            case Field f when !RowFieldNames.Contains(f.Name):
                Errors.Add($"{label}: nieznane pole [{f.Name}]");
                break;
            case Field f when !rowLevel && !insideAggregate && (whole || !_groupBy.Contains(f.Name, StringComparer.OrdinalIgnoreCase)):
                Errors.Add($"{label}: pole [{f.Name}] poza grupowaniem – użyj agregatu, np. SUMA([{f.Name}]) albo LISTA([{f.Name}])");
                break;
            case Unary u:
                Check(label, u.Operand, rowLevel, whole, insideAggregate);
                break;
            case Binary b:
                Check(label, b.Left, rowLevel, whole, insideAggregate);
                Check(label, b.Right, rowLevel, whole, insideAggregate);
                break;
            case Template t:
                foreach (var part in t.Parts.OfType<Node>())
                    Check(label, part, rowLevel, whole, insideAggregate);
                break;
            case Call c when Aggregates.Contains(c.Name):
                if (insideAggregate)
                    Errors.Add($"{label}: agregat {c.Name} wewnątrz innego agregatu");
                else if (rowLevel)
                    Errors.Add($"{label}: agregat {c.Name} w filtrze – filtr dotyczy pojedynczego wiersza");
                var (min, max) = c.Name.ToUpperInvariant() switch
                {
                    "LICZBA" => (0, 1), "LISTA" => (1, 2), "NAJWIĘKSZE" => (2, 4), _ => (1, 2),
                };
                if (c.Args.Count < min || c.Args.Count > max)
                    Errors.Add($"{label}: {c.Name} – {min}–{max} argumentów");
                foreach (var arg in c.Args)
                    Check(label, arg, rowLevel: true, whole, insideAggregate: true);
                break;
            case Call c when Scalars.Contains(c.Name):
                if (c.Scope is not null)
                    Errors.Add($"{label}: {c.Scope}{c.Name} – przedrostek tylko przy agregatach");
                var expected = c.Name.ToUpperInvariant() switch { "JEŻELI" => 3, "ZAOKR" => 2, _ => 1 };
                if (c.Args.Count != expected)
                    Errors.Add($"{label}: {c.Name} – {expected} argument(y)");
                foreach (var arg in c.Args)
                    Check(label, arg, rowLevel, whole, insideAggregate);
                break;
            case Call c:
                Errors.Add($"{label}: nieznana funkcja {c.Scope}{c.Name}");
                break;
        }
    }

    // ---------- wykonanie ----------

    private sealed record Row(RunBalanceRow Source, RunValidationData Data);

    private sealed class Group(IReadOnlyDictionary<string, object?> key, List<Row> rows, List<Row> previous, List<Row> all)
    {
        public IReadOnlyDictionary<string, object?> Key { get; } = key;
        public List<Row> Rows { get; } = rows;
        public List<Row> Previous { get; } = previous;
        public List<Row> All { get; } = all;
    }

    private sealed record Env(RunValidationData Data, decimal? Threshold, Row? Row, Group? Group);

    public IReadOnlyList<RunValidationResult> Run(RunValidationData data, decimal? threshold, CheckLevel level, int top)
    {
        var context = new Env(data, threshold, null, null);
        bool Keep(Row row) => _filter is null || Truth(Eval(_filter, context with { Row = row }));
        var current = data.Current.Select(r => new Row(r, data)).Where(Keep).ToList();
        var previous = (data.Previous ?? []).Select(r => new Row(r, data)).Where(Keep).ToList();
        var comparer = StringComparer.OrdinalIgnoreCase;
        string KeyText(Row row) => string.Join("\u001f", _groupBy.Select(f => RunValidationLanguage.Show(RowValue(f, row))));
        List<Group> groups;
        if (_groupBy.Count == 0)
            groups = [new Group(new Dictionary<string, object?>(), current, previous, current)];
        else
        {
            var now = current.GroupBy(KeyText, comparer).ToDictionary(g => g.Key, g => g.ToList(), comparer);
            var then = previous.GroupBy(KeyText, comparer).ToDictionary(g => g.Key, g => g.ToList(), comparer);
            groups = now.Keys.Union(then.Keys, comparer).Select(k =>
            {
                var sample = now.TryGetValue(k, out var rows) ? rows[0] : then[k][0];
                return new Group(_groupBy.ToDictionary(f => f, f => RowValue(f, sample), comparer), rows ?? [], then.GetValueOrDefault(k) ?? [], current);
            }).ToList();
        }
        var results = new List<(RunValidationResult Result, string Key)>();
        foreach (var group in groups)
        {
            var env = context with { Group = group };
            if (!Truth(Eval(_condition!, env)))
                continue;
            var amount = _amount is null ? null : Number(Eval(_amount, env));
            var element = _groupBy.Count == 0 ? null : RunValidationLanguage.Show(group.Key[_groupBy[0]]) is { Length: > 0 } e ? e : null;
            results.Add((new RunValidationResult(level, element, RunValidationLanguage.Show(Eval(_message!, env)), amount),
                string.Join(" ", _groupBy.Select(f => RunValidationLanguage.Show(group.Key[f])))));
        }
        var ordered = results.OrderByDescending(r => r.Result.Amount is { } a ? Math.Abs(a) : -1).ThenBy(r => r.Key, StringComparer.CurrentCulture)
            .Select(r => r.Result);
        var list = (top > 0 ? ordered.Take(top) : ordered).ToList();
        if (list.Count == 0 && _ok is not null)
            list.Add(new RunValidationResult(CheckLevel.Pass, null,
                RunValidationLanguage.Show(Eval(_ok, context with { Group = new Group(new Dictionary<string, object?>(), current, previous, current) })), null));
        return list;
    }

    private static object? RowValue(string field, Row row)
    {
        var r = row.Source;
        var d = row.Data;
        var period = (r.FiscalYear, Math.Min(r.Period, 12));
        return field switch
        {
            "Element WBS" => r.WbsElement,
            "Cost element" => r.CostElement,
            "Rok" => (decimal)r.FiscalYear,
            "Okres" => (decimal)r.Period,
            "Rok/okres" => RunValidationLanguage.PeriodText(r.FiscalYear, r.Period),
            "Rozliczeniowy" => r.IsSettlement,
            "Waluta" => r.Currency,
            "Wiersze" => (decimal)r.Rows,
            "Kwota" => r.Amount,
            "Utworzono" => r.LastCreatedOn,
            "Okres zamknięty" => period.CompareTo(d.ClosedBefore) < 0,
            "Okres przyszły" => period.CompareTo(d.Today) > 0,
            "Okres bieżący" => period.CompareTo(d.Today) == 0,
            "W Cost Category" => r.CostElement.Length > 0 && d.CostElementsInDictionary.Contains(r.CostElement),
            _ => throw new InvalidOperationException($"nieznane pole [{field}]"),
        };
    }

    private static object? ContextValue(string field, Env env) => field switch
    {
        "Próg" => env.Threshold ?? 0m,
        "Jest poprzedni" => env.Data.Previous is not null,
        "Waluta kosztów" => env.Data.Currency ?? env.Data.Current.Where(r => r.Currency.Length > 0).GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(r => r.Rows)).Select(g => g.Key).FirstOrDefault(),
        "Bieżący okres" => RunValidationLanguage.PeriodText(env.Data.Today.Year, env.Data.Today.Period),
        "Pierwszy otwarty okres" => RunValidationLanguage.PeriodText(env.Data.ClosedBefore.Year, env.Data.ClosedBefore.Period),
        _ => throw new InvalidOperationException($"nieznane pole [{field}]"),
    };

    private static string Canonical(string name, IEnumerable<RunValidationTerm> terms) =>
        terms.First(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).Name;

    private object? Eval(Node node, Env env)
    {
        switch (node)
        {
            case Const c:
                return c.Value;
            case Field f when ContextFieldNames.Contains(f.Name):
                return ContextValue(Canonical(f.Name, RunValidationLanguage.ContextFields), env);
            case Field f when env.Row is { } row:
                return RowValue(Canonical(f.Name, RunValidationLanguage.RowFields), row);
            case Field f when env.Group is { } group && group.Key.TryGetValue(f.Name, out var keyValue):
                return keyValue;
            case Field f:
                throw new InvalidOperationException($"pole [{f.Name}] poza grupowaniem");
            case Template t:
                var text = new StringBuilder();
                foreach (var part in t.Parts)
                    text.Append(part is Node n ? RunValidationLanguage.Show(Eval(n, env)) : (string)part);
                return text.ToString();
            case Unary { Op: "-" } u:
                return Number(Eval(u.Operand, env)) is { } v ? -v : null;
            case Unary u:
                return !Truth(Eval(u.Operand, env));
            case Binary { Op: "ORAZ" } b:
                return Truth(Eval(b.Left, env)) && Truth(Eval(b.Right, env));
            case Binary { Op: "LUB" } b:
                return Truth(Eval(b.Left, env)) || Truth(Eval(b.Right, env));
            case Binary { Op: "&" } b:
                return RunValidationLanguage.Show(Eval(b.Left, env)) + RunValidationLanguage.Show(Eval(b.Right, env));
            case Binary b when b.Op is "+" or "-" or "*" or "/":
            {
                var (l, r) = (Number(Eval(b.Left, env)), Number(Eval(b.Right, env)));
                if (l is null || r is null)
                    return null;
                return b.Op switch { "+" => l + r, "-" => l - r, "*" => l * r, _ => r == 0 ? null : l / r };
            }
            case Binary b:
                return Compare(b.Op, Eval(b.Left, env), Eval(b.Right, env));
            case Call c when Aggregates.Contains(c.Name):
                return Aggregate(c, env);
            case Call c:
                return Scalar(c, env);
            default:
                throw new InvalidOperationException("nieznany element formuły");
        }
    }

    private object? Scalar(Call c, Env env)
    {
        switch (c.Name.ToUpperInvariant())
        {
            case "ABS":
                return Number(Eval(c.Args[0], env)) is { } a ? Math.Abs(a) : null;
            case "JEŻELI":
                return Truth(Eval(c.Args[0], env)) ? Eval(c.Args[1], env) : Eval(c.Args[2], env);
            case "PUSTE":
                return Eval(c.Args[0], env) is null or "";
            case "ZAOKR":
                return Number(Eval(c.Args[0], env)) is { } x && Number(Eval(c.Args[1], env)) is { } n ? Math.Round(x, (int)Math.Clamp(n, 0, 10)) : null;
            default:
                return RunValidationLanguage.Show(Eval(c.Args[0], env));
        }
    }

    private object? Aggregate(Call c, Env env)
    {
        if (env.Group is not { } group)
            throw new InvalidOperationException($"agregat {c.Name} poza grupą");
        var rows = c.Scope switch
        {
            RunValidationLanguage.Previous => group.Previous,
            RunValidationLanguage.Whole => group.All,
            _ => group.Rows,
        };
        var name = c.Name.ToUpperInvariant();
        var conditionIndex = name switch { "LICZBA" => 0, "NAJWIĘKSZE" => 3, "LISTA" => -1, _ => 1 };
        IEnumerable<Row> Selected() => conditionIndex >= 0 && c.Args.Count > conditionIndex
            ? rows.Where(r => Truth(Eval(c.Args[conditionIndex], env with { Row = r }))) : rows;
        object? Value(Row r, int index) => Eval(c.Args[index], env with { Row = r });
        switch (name)
        {
            case "SUMA":
                return Selected().Sum(r => Number(Value(r, 0)) ?? 0m);
            case "LICZBA":
                return (decimal)Selected().Count();
            case "MAKS":
            case "MIN":
            {
                var values = Selected().Select(r => Value(r, 0)).Where(v => v is not null and not "").ToList();
                if (values.Count == 0)
                    return null;
                var sorted = values.Order(Comparer<object?>.Create((x, y) => Order(x, y))).ToList();
                return name == "MAKS" ? sorted[^1] : sorted[0];
            }
            case "LISTA":
            {
                var limit = c.Args.Count > 1 ? (int)(Number(Eval(c.Args[1], env)) ?? 5) : 5;
                var values = rows.Select(r => RunValidationLanguage.Show(Value(r, 0))).Where(v => v.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCulture).ToList();
                return string.Join(", ", values.Take(Math.Max(limit, 1))) + (values.Count > limit ? "…" : "");
            }
            default:   // NAJWIĘKSZE
            {
                var limit = c.Args.Count > 2 ? (int)(Number(Eval(c.Args[2], env)) ?? 3) : 3;
                var top = Selected().GroupBy(r => RunValidationLanguage.Show(Value(r, 0)), StringComparer.OrdinalIgnoreCase)
                    .Select(g => (g.Key, Sum: g.Sum(r => Number(Value(r, 1)) ?? 0m))).Where(x => x.Sum != 0)
                    .OrderByDescending(x => Math.Abs(x.Sum)).Take(Math.Max(limit, 1))
                    .Select(x => $"{(x.Key.Length == 0 ? "(puste)" : x.Key)} {RunValidationLanguage.Show(x.Sum)}");
                return string.Join(", ", top);
            }
        }
    }

    private static decimal? Number(object? value) => value switch
    {
        null => null,
        decimal d => d,
        bool b => b ? 1 : 0,
        string s when PolishNumber.TryParse(s, out var p) => p,
        _ => null,
    };

    private static bool Truth(object? value) => value switch
    {
        bool b => b,
        decimal d => d != 0,
        string s => s.Length > 0,
        _ => false,
    };

    private static int Order(object? x, object? y) => (x, y) switch
    {
        (decimal a, decimal b) => a.CompareTo(b),
        (DateTime a, DateTime b) => a.CompareTo(b),
        (bool a, bool b) => a.CompareTo(b),
        _ => string.Compare(RunValidationLanguage.Show(x), RunValidationLanguage.Show(y), StringComparison.OrdinalIgnoreCase),
    };

    private static bool Compare(string op, object? left, object? right)
    {
        if (left is null || right is null)
            return op switch { "=" => left is null && right is null, "<>" => !(left is null && right is null), _ => false };
        if (left is decimal || right is decimal)
        {
            var (l, r) = (Number(left), Number(right));
            if (l is null || r is null)
                return op == "<>";
            left = l;
            right = r;
        }
        var order = Order(left, right);
        return op switch
        {
            "=" => order == 0, "<>" => order != 0, "<" => order < 0, "<=" => order <= 0, ">" => order > 0, _ => order >= 0,
        };
    }

    // ---------- parser ----------

    /// <summary>
    /// Parser zstępujący: wyrażenie = LUB; LUB = ORAZ {LUB ORAZ}; ORAZ = NIE {ORAZ NIE}; NIE = [NIE] porównanie; porównanie = łączenie
    /// [op łączenie]; łączenie = suma {&amp; suma}; suma = iloczyn {(+|−) iloczyn}; iloczyn = czynnik {(*|/) czynnik}; czynnik = liczba |
    /// "tekst" | [pole] | funkcja(arg; …) | (wyrażenie) | −czynnik | PRAWDA | FAŁSZ.
    /// </summary>
    private sealed class Parser(string text)
    {
        private int _pos;

        public Node Whole()
        {
            var node = Or();
            Skip();
            if (_pos < text.Length)
                throw new FormatException($"nieoczekiwany znak „{text[_pos]}” (pozycja {_pos + 1})");
            return node;
        }

        /// <summary>Szablon komunikatu: tekst z wyrażeniami w nawiasach klamrowych, np. „Element {[Element WBS]}: {SUMA([Kwota])}”.</summary>
        public Node Template()
        {
            var parts = new List<object>();
            var literal = new StringBuilder();
            while (_pos < text.Length)
            {
                var ch = text[_pos];
                if (ch == '{')
                {
                    if (literal.Length > 0)
                    {
                        parts.Add(literal.ToString());
                        literal.Clear();
                    }
                    var end = Closing(_pos + 1);
                    var inner = new Parser(text[(_pos + 1)..end]);
                    try
                    {
                        parts.Add(inner.Whole());
                    }
                    catch (FormatException ex)
                    {
                        throw new FormatException($"{{{text[(_pos + 1)..end]}}} – {ex.Message}");
                    }
                    _pos = end + 1;
                }
                else
                {
                    literal.Append(ch);
                    _pos++;
                }
            }
            if (literal.Length > 0)
                parts.Add(literal.ToString());
            return new Template(parts);
        }

        // Zamykający nawias klamrowy wyrażenia w szablonie (z pominięciem tekstów w cudzysłowie).
        private int Closing(int from)
        {
            var quoted = false;
            for (var i = from; i < text.Length; i++)
            {
                if (text[i] == '"')
                    quoted = !quoted;
                else if (text[i] == '}' && !quoted)
                    return i;
            }
            throw new FormatException($"brak „}}” po pozycji {from}");
        }

        private Node Or()
        {
            var left = And();
            while (Keyword("LUB", "OR"))
                left = new Binary("LUB", left, And());
            return left;
        }

        private Node And()
        {
            var left = Not();
            while (Keyword("ORAZ", "AND"))
                left = new Binary("ORAZ", left, Not());
            return left;
        }

        private Node Not() => Keyword("NIE", "NOT") ? new Unary("NIE", Not()) : Comparison();

        private Node Comparison()
        {
            var left = Concat();
            Skip();
            foreach (var op in new[] { "<=", ">=", "<>", "=", "<", ">" })
                if (string.CompareOrdinal(text, _pos, op, 0, op.Length) == 0)
                {
                    _pos += op.Length;
                    return new Binary(op, left, Concat());
                }
            return left;
        }

        private Node Concat()
        {
            var left = Sum();
            while (Symbol('&'))
                left = new Binary("&", left, Sum());
            return left;
        }

        private Node Sum()
        {
            var left = Product();
            while (true)
            {
                if (Symbol('+'))
                    left = new Binary("+", left, Product());
                else if (Symbol('-'))
                    left = new Binary("-", left, Product());
                else
                    return left;
            }
        }

        private Node Product()
        {
            var left = Factor();
            while (true)
            {
                if (Symbol('*'))
                    left = new Binary("*", left, Factor());
                else if (Symbol('/'))
                    left = new Binary("/", left, Factor());
                else
                    return left;
            }
        }

        private Node Factor()
        {
            Skip();
            if (_pos >= text.Length)
                throw new FormatException("niepełne wyrażenie");
            var ch = text[_pos];
            if (ch == '-')
            {
                _pos++;
                return new Unary("-", Factor());
            }
            if (ch == '(')
            {
                _pos++;
                var inner = Or();
                if (!Symbol(')'))
                    throw new FormatException($"brak „)” (pozycja {_pos + 1})");
                return inner;
            }
            if (ch == '[')
            {
                var end = text.IndexOf(']', _pos);
                if (end < 0)
                    throw new FormatException($"brak „]” po pozycji {_pos + 1}");
                var name = text[(_pos + 1)..end].Trim();
                _pos = end + 1;
                return new Field(name);
            }
            if (ch == '"')
            {
                var end = text.IndexOf('"', _pos + 1);
                if (end < 0)
                    throw new FormatException($"brak zamykającego cudzysłowu po pozycji {_pos + 1}");
                var value = text[(_pos + 1)..end];
                _pos = end + 1;
                return new Const(value);
            }
            if (char.IsDigit(ch))
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] is ',' or '.'))
                    _pos++;
                var literal = text[start.._pos].Replace(',', '.');
                return decimal.TryParse(literal, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? new Const(number) : throw new FormatException($"niepoprawna liczba „{text[start.._pos]}”");
            }
            if (char.IsLetter(ch))
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] is '_' or '.'))
                    _pos++;
                var word = text[start.._pos];
                if (word.Equals("PRAWDA", StringComparison.OrdinalIgnoreCase) || word.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                    return new Const(true);
                if (word.Equals("FAŁSZ", StringComparison.OrdinalIgnoreCase) || word.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                    return new Const(false);
                string? scope = null;
                foreach (var prefix in new[] { RunValidationLanguage.Previous, RunValidationLanguage.Whole })
                    if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        scope = prefix;
                        word = word[prefix.Length..];
                    }
                if (!Symbol('('))
                    throw new FormatException($"„{text[start.._pos]}” – oczekiwano funkcji z nawiasem albo pola w nawiasach kwadratowych");
                var args = new List<Node>();
                Skip();
                if (!Symbol(')'))
                {
                    do
                        args.Add(Or());
                    while (Symbol(';'));
                    if (!Symbol(')'))
                        throw new FormatException($"{word}: brak „)” albo „;” między argumentami (pozycja {_pos + 1})");
                }
                return new Call(word.ToUpperInvariant(), scope, args);
            }
            throw new FormatException($"nieoczekiwany znak „{ch}” (pozycja {_pos + 1})");
        }

        private void Skip()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
                _pos++;
        }

        private bool Symbol(char symbol)
        {
            Skip();
            if (_pos < text.Length && text[_pos] == symbol)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private bool Keyword(params string[] words)
        {
            Skip();
            foreach (var word in words)
                if (_pos + word.Length <= text.Length && string.Compare(text, _pos, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                    && (_pos + word.Length == text.Length || !char.IsLetterOrDigit(text[_pos + word.Length])))
                {
                    _pos += word.Length;
                    return true;
                }
            return false;
        }
    }
}
