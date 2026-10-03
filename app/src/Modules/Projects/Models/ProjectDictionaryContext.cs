namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Dane potrzebne do walidacji słowników projektu (docs/slowniki.md, rozdz. 5.2–5.3):
/// Scope – zakres projektu po stronie P1S (Legacy WBS i cele mapowania elementów nakładki z poddrzewami LOG.WBS);
/// P1sOwners – elementy P1S ze słowników „WP i CAM” innych projektów → kod projektu;
/// Persons – konta AD i nazwiska ze słownika globalnego Osoby;
/// Wps – WP ze słownika „WP i CAM” projektu (null – słownika nie wczytano).
/// </summary>
public sealed record ProjectDictionaryContext(
    P1sScope Scope,
    IReadOnlyDictionary<string, string> P1sOwners,
    IReadOnlySet<string> Persons,
    IReadOnlySet<string>? Wps);
