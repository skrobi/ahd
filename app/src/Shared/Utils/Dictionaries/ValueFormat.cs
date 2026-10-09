using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Shared.Utils.Dictionaries;

/// <summary>
/// Zapis kanoniczny wartości słownika i ich wyświetlanie. Kanonicznie: tekst przycięty (spacje niełamliwe
/// zamienione na zwykłe), liczby z kropką, daty RRRR-MM-DD, tak / nie, wybór w pisowni z listy.
/// </summary>
public static class ValueFormat
{
    private static readonly string[] Yes = ["tak", "t", "true", "1", "x", "yes", "y", "prawda"];
    private static readonly string[] No = ["nie", "n", "false", "0", "no", "fałsz"];

    /// <summary>Wartości błędów formuł Excela (#N/A itd., także polskie) – nie są danymi.</summary>
    private static readonly HashSet<string> ExcelErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        "#N/A", "#VALUE!", "#REF!", "#DIV/0!", "#NUM!", "#NAME?", "#NULL!", "#SPILL!", "#CALC!", "#GETTING_DATA",
        "#ARG!", "#ADR!", "#DZIEL/0!", "#LICZBA!", "#NAZWA?", "#ZERO!", "#WARTOŚĆ!", "#BRAK!",
    };

    /// <summary>Liczba miejsc po przecinku przechowywana w bazie (DECIMAL(28,8)).</summary>
    private const int Decimals = 8;

    /// <summary>Normalizuje wartość; zwraca false i opis błędu, gdy wartość nie pasuje do typu kolumny.</summary>
    public static bool TryNormalize(DictColumn column, string? input, out string? canonical, out string? error)
    {
        error = null;
        canonical = Clean(input);
        if (canonical is null)
            return true;
        if (ExcelErrors.Contains(canonical))
        {
            error = $"'{canonical}' – błąd formuły Excela, nie wartość";
            return false;
        }

        switch (column.Type)
        {
            case ColumnType.Text:
                if (column.PadNumericTo is { } length && canonical.All(char.IsAsciiDigit) && canonical.Length < length)
                    canonical = canonical.PadLeft(length, '0');
                return true;

            case ColumnType.Integer:
                if (PolishNumber.TryParseInteger(canonical, out var integer))
                {
                    canonical = integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                error = $"'{input}' – oczekiwano liczby całkowitej";
                return false;

            case ColumnType.Decimal:
                if (PolishNumber.TryParse(canonical, out var number))
                {
                    // Jak w bazie – inaczej ponowne wczytanie tego samego pliku pokazywałoby zmianę (np. =1/3,95).
                    canonical = PolishNumber.ToCanonical(Math.Round(number, Decimals, MidpointRounding.AwayFromZero));
                    return true;
                }
                error = $"'{input}' – oczekiwano liczby";
                return false;

            case ColumnType.Date:
                if (DateText.TryParse(canonical, out var date))
                {
                    canonical = DateText.ToCanonical(date);
                    return true;
                }
                error = $"'{input}' – oczekiwano daty RRRR-MM-DD";
                return false;

            case ColumnType.Boolean:
                var lower = canonical.ToLowerInvariant();
                if (Yes.Contains(lower)) { canonical = "tak"; return true; }
                if (No.Contains(lower)) { canonical = "nie"; return true; }
                error = $"'{input}' – oczekiwano tak / nie";
                return false;

            case ColumnType.Choice:
                var value = canonical;
                var choice = column.Choices?.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
                if (choice is not null)
                {
                    canonical = choice;
                    return true;
                }
                error = $"'{input}' – dozwolone: {string.Join(", ", column.Choices ?? [])}";
                return false;

            default:
                return true;
        }
    }

    /// <summary>Tekst do wyświetlenia i edycji (liczby w formacie polskim).</summary>
    public static string Display(DictColumn column, string? canonical)
    {
        if (canonical is null)
            return "";
        if (column.Type == ColumnType.Decimal && PolishNumber.TryParse(canonical, out var number))
            return PolishNumber.ToDisplay(number);
        return canonical;
    }

    /// <summary>Wartość do zapisu w Excelu: liczba, data albo tekst (klucze zawsze tekst).</summary>
    public static object? ToExcel(DictColumn column, string? canonical)
    {
        if (canonical is null)
            return null;
        return column.Type switch
        {
            ColumnType.Integer when !column.Key && PolishNumber.TryParseInteger(canonical, out var i) => i,
            ColumnType.Decimal when PolishNumber.TryParse(canonical, out var d) => d,
            ColumnType.Date when DateText.TryParse(canonical, out var date) => date,
            _ => canonical,
        };
    }

    /// <summary>Czyszczenie automatyczne: spacje na początku / końcu i niełamliwe; pusta wartość = brak.</summary>
    public static string? Clean(string? input)
    {
        if (input is null)
            return null;
        var cleaned = input.Replace(' ', ' ').Replace(' ', ' ').Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
