namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Dane potrzebne do walidacji słowników projektu (docs/slowniki.md, rozdz. 5.2–5.3):
/// LegacyWbs – odpowiedniki P1S elementów nakładki (zakres projektu po stronie P1S; element P1S należy do projektu,
/// gdy jest równy Legacy WBS albo leży pod nim, np. AC-CAB.6.38.01 pod AC-CAB.6.38);
/// P1sOwners – elementy P1S ze słowników „WP i CAM” innych projektów → kod projektu;
/// Persons – konta AD i nazwiska ze słownika globalnego Osoby;
/// Wps – WP ze słownika „WP i CAM” projektu (null – słownika nie wczytano).
/// </summary>
public sealed record ProjectDictionaryContext(
    IReadOnlyCollection<string> LegacyWbs,
    IReadOnlyDictionary<string, string> P1sOwners,
    IReadOnlySet<string> Persons,
    IReadOnlySet<string>? Wps)
{
    /// <summary>Najdłuższy Legacy WBS nakładki obejmujący element P1S (równy albo nadrzędny); null – poza zakresem.</summary>
    public string? ScopeOf(string p1sElement) => ScopeOf(LegacyWbs, p1sElement);

    public static string? ScopeOf(IEnumerable<string> legacyWbs, string p1sElement) =>
        legacyWbs
            .Where(l => p1sElement.Equals(l, StringComparison.OrdinalIgnoreCase) || p1sElement.StartsWith(l + ".", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.Length)
            .FirstOrDefault();
}
