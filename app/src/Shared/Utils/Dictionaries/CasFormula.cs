using System.Globalization;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Formuła narzutu CAS z klocków (docs/slowniki.md, rozdz. 6.1): koszt bazowy kolumny, pula (narzut innej puli
/// w wierszu), wskaźnik puli, operatory + − × ÷ i nawiasy. Pule i wskaźniki wskazuje identyfikator puli (RowId słownika
/// „Kategorie narzutu CAS”), nie nazwa – zmiana nazwy puli nie zmienia formuły. Zapis w słowniku: klocki rozdzielone spacją
/// („( base + p:12 ) * r:15”).
/// </summary>
public static class CasFormula
{
    public enum Kind { Base, Pool, Rate, Operator, Open, Close }

    /// <summary>Klocek formuły; Id – identyfikator puli (Pool, Rate), Symbol – operator (+ - * /).</summary>
    public sealed record Token(Kind Kind, long Id = 0, char Symbol = ' ')
    {
        public static readonly Token BaseCost = new(Kind.Base);
        public static readonly Token Open = new(Kind.Open);
        public static readonly Token Close = new(Kind.Close);

        public static Token Pool(long id) => new(Kind.Pool, id);
        public static Token Rate(long id) => new(Kind.Rate, id);
        public static Token Op(char symbol) => new(Kind.Operator, Symbol: symbol);

        public bool IsOperand => Kind is Kind.Base or Kind.Pool or Kind.Rate;

        public string Code => Kind switch
        {
            Kind.Base => "base",
            Kind.Pool => $"p:{Id}",
            Kind.Rate => $"r:{Id}",
            Kind.Operator => Symbol.ToString(),
            Kind.Open => "(",
            _ => ")",
        };
    }

    public const string Operators = "+-*/";

    /// <summary>Formuła domyślna puli: koszt bazowy × wskaźnik puli.</summary>
    public static IReadOnlyList<Token> Default(long pool) => [Token.BaseCost, Token.Op('*'), Token.Rate(pool)];

    public static string Serialize(IEnumerable<Token> tokens) => string.Join(" ", tokens.Select(t => t.Code));

    /// <summary>Klocki z zapisu w słowniku; nieznany zapis – null (formuła uszkodzona).</summary>
    public static IReadOnlyList<Token>? Parse(string? text)
    {
        var tokens = new List<Token>();
        foreach (var part in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            Token? token = part switch
            {
                "base" => Token.BaseCost,
                "(" => Token.Open,
                ")" => Token.Close,
                [var op] when Operators.Contains(op) => Token.Op(op),
                _ when part.StartsWith("p:") && Id(part[2..]) is { } pool => Token.Pool(pool),
                _ when part.StartsWith("r:") && Id(part[2..]) is { } rate => Token.Rate(rate),
                _ => null,
            };
            if (token is null)
                return null;
            tokens.Add(token);
        }
        return tokens;
    }

    private static long? Id(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>
    /// Błąd budowy formuły albo null: pusta, składnik obok składnika, operator na początku / końcu / po operatorze,
    /// nawiasy niedomknięte, pusty nawias, pula albo wskaźnik spoza słownika pul (known – identyfikatory pul).
    /// </summary>
    public static string? Error(IReadOnlyList<Token> tokens, IReadOnlySet<long>? known = null)
    {
        if (tokens.Count == 0)
            return "Formuła jest pusta – dodaj klocki.";
        var depth = 0;
        var expectOperand = true;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var at = $"klocek {i + 1}";
            if (token.IsOperand)
            {
                if (!expectOperand)
                    return $"Brak operatora przed klockiem „{token.Code}” ({at}) – wstaw +, −, × albo ÷.";
                if (token.Kind != Kind.Base && known is not null && !known.Contains(token.Id))
                    return $"Kategoria usunięta ze słownika „Kategorie narzutu CAS” ({at}) – usuń klocek.";
                expectOperand = false;
            }
            else if (token.Kind == Kind.Operator)
            {
                if (expectOperand)
                    return i == 0 ? "Formuła nie może zaczynać się od operatora." : $"Dwa operatory obok siebie albo operator po „(” ({at}).";
                expectOperand = true;
            }
            else if (token.Kind == Kind.Open)
            {
                if (!expectOperand)
                    return $"Brak operatora przed „(” ({at}).";
                depth++;
            }
            else
            {
                if (expectOperand)
                    return i > 0 && tokens[i - 1].Kind == Kind.Open ? $"Pusty nawias ({at})." : $"Operator przed „)” ({at}).";
                if (--depth < 0)
                    return $"Nawias „)” bez otwierającego ({at}).";
            }
        }
        if (expectOperand)
            return "Formuła nie może kończyć się operatorem.";
        return depth > 0 ? $"Niedomknięte nawiasy: {depth} – dodaj „)”." : null;
    }

    /// <summary>Pule, których narzut wchodzi do formuły (zależności – kontrola zapętleń).</summary>
    public static IEnumerable<long> Pools(IEnumerable<Token> tokens) => tokens.Where(t => t.Kind == Kind.Pool).Select(t => t.Id).Distinct();

    /// <summary>Formuła do czytania: nazwy pul i wskaźników (name – nazwa puli po identyfikatorze).</summary>
    public static string Display(IEnumerable<Token> tokens, Func<long, string> name) =>
        string.Join(" ", tokens.Select(t => t.Kind switch
        {
            Kind.Base => CasBurden.Base,
            Kind.Pool => name(t.Id),
            Kind.Rate => $"wskaźnik {name(t.Id)}",
            Kind.Operator => t.Symbol switch { '*' => "×", '/' => "÷", '-' => "−", _ => "+" },
            Kind.Open => "(",
            _ => ")",
        })).Replace("( ", "(").Replace(" )", ")");

    /// <summary>
    /// Wartość formuły (jak formuła Excela raportu kosztów CES): cost – koszt bazowy, pool / rate – narzut / wskaźnik puli;
    /// mnożenie i dzielenie przed dodawaniem i odejmowaniem, od lewej; dzielenie przez 0 – 0 (w Excelu #DZIEL/0!).
    /// Formuła ma być poprawna (Error = null).
    /// </summary>
    public static decimal Evaluate(IReadOnlyList<Token> tokens, decimal cost, Func<long, decimal> pool, Func<long, decimal> rate)
    {
        var at = 0;
        decimal Operand()
        {
            var token = tokens[at++];
            if (token.Kind != Kind.Open)
                return token.Kind switch { Kind.Base => cost, Kind.Pool => pool(token.Id), _ => rate(token.Id) };
            var value = Sum();
            at++;   // „)”
            return value;
        }
        decimal Product()
        {
            var value = Operand();
            while (at < tokens.Count && tokens[at] is { Kind: Kind.Operator, Symbol: '*' or '/' } op)
            {
                at++;
                var right = Operand();
                value = op.Symbol == '*' ? value * right : right == 0 ? 0 : value / right;
            }
            return value;
        }
        decimal Sum()
        {
            var value = Product();
            while (at < tokens.Count && tokens[at] is { Kind: Kind.Operator, Symbol: '+' or '-' } op)
            {
                at++;
                var right = Product();
                value = op.Symbol == '+' ? value + right : value - right;
            }
            return value;
        }
        return Sum();
    }

    /// <summary>Formuła Excela: cost – komórka kosztu bazowego, pool / rate – komórka narzutu / wskaźnika puli.</summary>
    public static string Excel(IEnumerable<Token> tokens, string cost, Func<long, string> pool, Func<long, string> rate) =>
        "=" + string.Concat(tokens.Select(t => t.Kind switch
        {
            Kind.Base => cost,
            Kind.Pool => pool(t.Id),
            Kind.Rate => rate(t.Id),
            Kind.Operator => t.Symbol.ToString(),
            Kind.Open => "(",
            _ => ")",
        }));
}
