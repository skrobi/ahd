using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Dane mapowania CES ↔ P1S czytane przy budowie nakładki (docs/mapowanie-ces-p1s.md): najnowszy raport mapowań,
/// korekty i struktura P1S z PZLPROD (null – niedostępna, powód w P1sError). Bez elementów CES z kosztów – projekt CES
/// elementu nakładki pochodzi z nakładki. Ładowane raz na sesję – rozstrzyganie dla nakładki odbywa się w pamięci.
/// </summary>
public sealed record MappingInputs(
    ReportInfo? Report,
    IReadOnlyList<CorrectionRow> Corrections,
    IReadOnlyList<P1sElement>? P1s,
    string? P1sError)
{
    public static MappingInputs None { get; } = new(null, [], null, null);

    /// <summary>Opis źródła do pokazania przy nakładce.</summary>
    public string Describe =>
        (Report is null ? "Raport mapowań nie został zaimportowany – cel P1S tylko z korekt i Legacy WBS." : $"Mapowanie: raport {Report.Describe}, korekt {Corrections.Count}.")
        + (P1sError is null ? "" : $" {P1sError} – bez poddrzew LOG.WBS.");
}
