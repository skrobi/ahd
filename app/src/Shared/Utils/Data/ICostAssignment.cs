namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Przypisanie kosztu elementu CES (element WBS danych ACTUALS) do WP – logika struktury programu: element nakładki → WP na jego kodzie
/// P1S, a bez niego – jedyny WP pod elementem (poddrzewo LOG.WBS); Wp null – koszt bez przypisania, Gap – powód (CostGaps).
/// </summary>
public sealed record CostTarget(string WbsElement, string? Wp, string? Cam, string? CostCategory, string? Gap);

/// <summary>Wynik przypisania: element → WP (każdy element kosztów raz); Note – brak danych (np. struktura P1S niedostępna) albo null.</summary>
public sealed record CostAssignment(IReadOnlyList<CostTarget> Targets, string? Note);

/// <summary>Powody kosztu bez przypisania do WP (struktura programu, etap przebiegu „Łączenie źródeł”).</summary>
public static class CostGaps
{
    public const string NoWp = "element nakładki bez WP";
    public const string Ambiguous = "kilka WP pod elementem nakładki (reguła rozdziału – O45)";
    public const string Outside = "element CES spoza nakładki Performance Objectives";
    public const string NoElement = "wiersze bez elementu WBS";
}

/// <summary>
/// Przypisanie kosztów elementów CES do WP, CAM i Cost Category według struktury programu (nakładka, LOG.WBS, „WP i CAM”) – kontrakt
/// modułu Projekty dla etapu przebiegu „Łączenie źródeł” (docs/pipeline-fazy.md, P3). Rejestruje go moduł Projekty.
/// </summary>
public interface ICostAssignment
{
    /// <summary>Przypisanie elementów z costs (element WBS → koszt) w programie project.</summary>
    CostAssignment Assign(string project, IReadOnlyDictionary<string, decimal> costs);
}
