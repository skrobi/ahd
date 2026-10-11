using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Struktura P1S z PZLPROD czytana przy budowie nakładki (null – niedostępna, powód w P1sError). Ładowana raz na sesję.
/// </summary>
public sealed record P1sInputs(IReadOnlyList<P1sElement>? P1s, string? P1sError)
{
    public static P1sInputs None { get; } = new(null, null);

    /// <summary>Opis źródła do pokazania przy nakładce.</summary>
    public string Describe => P1sError is null ? "Strona P1S: Legacy WBS z Performance Objectives i LOG.WBS (PZLPROD)." : $"{P1sError} – bez poddrzew LOG.WBS.";
}
