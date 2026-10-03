using System.Text.RegularExpressions;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Dane podstawowe projektu (docs/funkcjonalnosc.md, F01, krok 1): kod unikalny – używany w nazwach folderów,
/// plików i przebiegów, więc tylko wielkie litery, cyfry, „-” i „_” (do 20 znaków, META_Project.Code); nazwa; typ.
/// </summary>
public static partial class ProjectRules
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;

    /// <summary>Kod w zapisie kanonicznym: bez spacji na końcach, wielkimi literami.</summary>
    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public static List<Issue> ValidateBasics(string code, string name, string type, IEnumerable<string> existingCodes)
    {
        var issues = new List<Issue>();
        if (code.Length == 0)
            issues.Add(Issue.Error("Podaj kod projektu", "Kod"));
        else if (code.Length > CodeMaxLength)
            issues.Add(Issue.Error($"Kod może mieć najwyżej {CodeMaxLength} znaków", "Kod"));
        else if (!CodePattern().IsMatch(code))
            issues.Add(Issue.Error("Kod: tylko litery A–Z, cyfry, „-” i „_” (używany w nazwach folderów i plików)", "Kod"));
        else if (existingCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
            issues.Add(Issue.Error($"Projekt o kodzie {code} już istnieje", "Kod"));

        if (name.Trim().Length == 0)
            issues.Add(Issue.Error("Podaj nazwę projektu", "Nazwa"));
        else if (name.Trim().Length > NameMaxLength)
            issues.Add(Issue.Error($"Nazwa może mieć najwyżej {NameMaxLength} znaków", "Nazwa"));

        if (!ProjectTypes.IsKnown(type))
            issues.Add(Issue.Error("Wybierz typ projektu", "Typ"));
        return issues;
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]*$")]
    private static partial Regex CodePattern();
}
