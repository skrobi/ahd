using System.Globalization;

namespace PzlEv.Shared.Models.Pipeline;

/// <summary>Miara programu w regułach uzgodnień (docs/pipeline-fazy.md, rozdz. 4.1): nazwa w formule ([Nazwa]) i opis.</summary>
public sealed record RunMeasure(string Name, string Description);

/// <summary>
/// Miary programu dostępne w regułach uzgodnień – podstawowy zestaw (wartości liczy przebieg, RunReconciliations w module
/// Przebiegi); nowa miara = pozycja tu i jej wartość w RunReconciliations.Measures.
/// </summary>
public static class RunMeasures
{
    public static RunMeasure GrossCost { get; } = new("Koszt brutto", "Koszt ACTUALS bez rozliczenia wychodzącego, w walucie kosztów przebiegu (CAS – po narzutach CAS); jak ACWP struktury.");
    public static RunMeasure Settlement { get; } = new("Rozliczenie", "Suma wierszy rozliczeniowych (cost element „Rozliczeniowy”), dane źródłowe ACTUALS.");
    public static RunMeasure ClosedBalance { get; } = new("Saldo okresów zamkniętych", "Suma wszystkich wierszy programu w okresach zamkniętych, dane źródłowe ACTUALS (netowanie do 0).");
    public static RunMeasure CurrentPeriodCost { get; } = new("Koszt bieżącego okresu", "Koszt brutto w bieżącym okresie kalendarza okresów.");
    public static RunMeasure PreviousGrossCost { get; } = new("Koszt brutto poprzedniego przebiegu", "Koszt brutto ostatniego uruchomienia wcześniejszego tygodnia (brak – reguła pominięta).");
    public static RunMeasure Rows { get; } = new("Wiersze", "Liczba wierszy danych ACTUALS programu.");
    public static RunMeasure PreviousRows { get; } = new("Wiersze poprzedniego przebiegu", "Liczba wierszy ostatniego uruchomienia wcześniejszego tygodnia (brak – reguła pominięta).");
    public static RunMeasure NoWbsCost { get; } = new("Koszt bez elementu WBS", "Koszt brutto wierszy bez elementu WBS.");
    public static RunMeasure Bac { get; } = new("BAC", "Suma BAC (budżet kosztowy) WP w „Harmonogram i budżet” – w walucie wpisów, bez przeliczenia.");
    public static RunMeasure BacHours { get; } = new("BAC HOURS", "Suma BAC HOURS (godziny) WP w „Harmonogram i budżet”.");
    public static RunMeasure BacMaterial { get; } = new("BAC MATERIAL", "Suma BAC MATERIAL WP w „Harmonogram i budżet” – w walucie wpisów, bez przeliczenia.");
    public static RunMeasure WpCount { get; } = new("Liczba WP", "Liczba WP w „WP i CAM”.");
    public static RunMeasure WpWithoutBudgetCount { get; } = new("Liczba WP bez budżetu", "Liczba WP bez BAC, BAC HOURS i BAC MATERIAL w „Harmonogram i budżet”.");

    /// <summary>Rodzaje kosztu (Cost Category, kolumna Rodzaj kosztu; puste – Other) – miary ACWP, przyrostu i średniego przyrostu.</summary>
    public static IReadOnlyList<string> Kinds { get; } =
        [Utils.Dictionaries.GlobalDictionaries.LaborKind, Utils.Dictionaries.GlobalDictionaries.MaterialKind, Utils.Dictionaries.GlobalDictionaries.OtherKind];

    public static string KindAcwp(string kind) => $"ACWP {kind}";

    public static string KindIncrement(string kind) => $"Przyrost okresu {kind}";

    public static string KindAverageIncrement(string kind) => $"Średni przyrost {kind}";

    public static IReadOnlyList<RunMeasure> All { get; } =
    [
        GrossCost, Settlement, ClosedBalance, CurrentPeriodCost, PreviousGrossCost, Rows, PreviousRows, NoWbsCost, Bac, BacHours, BacMaterial,
        WpCount, WpWithoutBudgetCount,
        .. Kinds.Select(k => new RunMeasure(KindAcwp(k), $"Koszt brutto rodzaju {k} narastająco (jak ACWP {k} struktury).")),
        .. Kinds.Select(k => new RunMeasure(KindIncrement(k), $"Koszt brutto rodzaju {k} zaksięgowany w bieżącym okresie kalendarza.")),
        .. Kinds.Select(k => new RunMeasure(KindAverageIncrement(k), $"Średni koszt rodzaju {k} na okres w 6 okresach przed bieżącym.")),
    ];

    public static RunMeasure? ByName(string name) => All.FirstOrDefault(m => string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Warunek reguły uzgodnień: lewa strona względem prawej z tolerancją.</summary>
    public static IReadOnlyList<string> Comparisons { get; } = ["=", "<=", ">=", "<>"];

    /// <summary>Warunek spełniony: = – różnica ≤ tolerancja; &lt;= / &gt;= – z tolerancją; &lt;&gt; – różnica &gt; tolerancja.</summary>
    public static bool Holds(string comparison, decimal left, decimal right, decimal tolerance) => comparison.Trim() switch
    {
        "=" => Math.Abs(left - right) <= tolerance,
        "<=" => left <= right + tolerance,
        ">=" => left >= right - tolerance,
        "<>" => Math.Abs(left - right) > tolerance,
        _ => false,
    };
}

/// <summary>
/// Formuła reguły uzgodnień: miary w nawiasach kwadratowych ([Koszt brutto]), liczby (przecinek albo kropka dziesiętna),
/// + − * / i nawiasy, np. „[BAC] * 1,1 - [Koszt brutto]”. Parse – formuła albo opis błędu; Evaluate – wartość albo opis,
/// czego brakuje (miara bez wartości, dzielenie przez 0).
/// </summary>
public sealed class RunFormula
{
    private abstract record Node;
    private sealed record Number(decimal Value) : Node;
    private sealed record Measure(string Name) : Node;
    private sealed record Negate(Node Operand) : Node;
    private sealed record Binary(char Op, Node Left, Node Right) : Node;

    private readonly Node _root;

    private RunFormula(Node root, string text, IReadOnlyList<string> measures)
    {
        _root = root;
        Text = text;
        Measures = measures;
    }

    public string Text { get; }

    /// <summary>Miary użyte w formule (nazwy z katalogu RunMeasures).</summary>
    public IReadOnlyList<string> Measures { get; }

    public static (RunFormula? Formula, string? Error) Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "pusta formuła");
        var parser = new Parser(text);
        try
        {
            var root = parser.Expression();
            parser.SkipSpaces();
            if (!parser.AtEnd)
                return (null, $"nieoczekiwany znak „{text[parser.Position]}” (pozycja {parser.Position + 1})");
            return (new RunFormula(root, text.Trim(), parser.Used.Distinct().ToList()), null);
        }
        catch (FormatException ex)
        {
            return (null, ex.Message);
        }
    }

    public (decimal? Value, string? Error) Evaluate(Func<string, decimal?> measure)
    {
        try
        {
            return (Value(_root), null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }

        decimal Value(Node node) => node switch
        {
            Number n => n.Value,
            Measure m => measure(m.Name) ?? throw new InvalidOperationException($"brak wartości miary [{m.Name}]"),
            Negate n => -Value(n.Operand),
            Binary { Op: '+' } b => Value(b.Left) + Value(b.Right),
            Binary { Op: '-' } b => Value(b.Left) - Value(b.Right),
            Binary { Op: '*' } b => Value(b.Left) * Value(b.Right),
            Binary b => Value(b.Right) is var divisor && divisor == 0
                ? throw new InvalidOperationException("dzielenie przez 0")
                : Value(b.Left) / divisor,
            _ => throw new InvalidOperationException("nieznany element formuły"),
        };
    }

    /// <summary>Parser zstępujący: wyrażenie = składnik {(+|−) składnik}; składnik = czynnik {(*|/) czynnik}; czynnik = liczba | [miara] | (wyrażenie) | −czynnik.</summary>
    private sealed class Parser(string text)
    {
        public int Position { get; private set; }

        public List<string> Used { get; } = [];

        public bool AtEnd => Position >= text.Length;

        public void SkipSpaces()
        {
            while (!AtEnd && char.IsWhiteSpace(text[Position]))
                Position++;
        }

        private bool Accept(char c)
        {
            SkipSpaces();
            if (AtEnd || text[Position] != c)
                return false;
            Position++;
            return true;
        }

        public Node Expression()
        {
            var node = Term();
            while (true)
            {
                if (Accept('+')) node = new Binary('+', node, Term());
                else if (Accept('-')) node = new Binary('-', node, Term());
                else return node;
            }
        }

        private Node Term()
        {
            var node = Factor();
            while (true)
            {
                if (Accept('*')) node = new Binary('*', node, Factor());
                else if (Accept('/')) node = new Binary('/', node, Factor());
                else return node;
            }
        }

        private Node Factor()
        {
            SkipSpaces();
            if (AtEnd)
                throw new FormatException("formuła urwana – brak liczby, miary albo nawiasu na końcu");
            if (Accept('-'))
                return new Negate(Factor());
            if (Accept('('))
            {
                var inner = Expression();
                if (!Accept(')'))
                    throw new FormatException("brak nawiasu zamykającego „)”");
                return inner;
            }
            if (Accept('['))
            {
                var end = text.IndexOf(']', Position);
                if (end < 0)
                    throw new FormatException("brak „]” po nazwie miary");
                var name = text[Position..end].Trim();
                Position = end + 1;
                var measure = RunMeasures.ByName(name)
                    ?? throw new FormatException($"nieznana miara [{name}] – dostępne: {string.Join(", ", RunMeasures.All.Select(m => m.Name))}");
                Used.Add(measure.Name);
                return new Measure(measure.Name);
            }
            var start = Position;
            while (!AtEnd && (char.IsDigit(text[Position]) || text[Position] is ',' or '.'))
                Position++;
            if (start == Position)
                throw new FormatException($"nieoczekiwany znak „{text[Position]}” (pozycja {Position + 1})");
            var digits = text[start..Position].Replace(',', '.');
            return decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                ? new Number(value)
                : throw new FormatException($"niepoprawna liczba „{text[start..Position]}”");
        }
    }
}
