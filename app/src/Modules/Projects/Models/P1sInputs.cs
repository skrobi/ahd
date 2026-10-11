using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Struktura P1S z PZLPROD czytana przy budowie nakładki (null – niedostępna, powód w P1sError). Ładowana raz na sesję.
/// </summary>
public sealed record P1sInputs(IReadOnlyList<P1sElement>? P1s, string? P1sError)
{
    public static P1sInputs None { get; } = new(null, null);

    /// <summary>Powód braku struktury P1S do pokazania przy nakładce (pusty – dostępna).</summary>
    public string Describe => P1sError is null ? "" : $"{P1sError} – bez poddrzew LOG.WBS.";
}
