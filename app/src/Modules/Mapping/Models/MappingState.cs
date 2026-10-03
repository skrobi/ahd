using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Modules.Mapping.Models;

/// <summary>
/// Stan mapowania do ekranu: struktura P1S z PZLPROD (null – niedostępna, powód w P1sError), drzewo, raport mapowań,
/// elementy CES, korekty i wynik rozstrzygania.
/// </summary>
public sealed record MappingState(
    IReadOnlyList<P1sElement>? P1s,
    string? P1sError,
    IReadOnlyList<P1sNode> Tree,
    ReportInfo? Report,
    IReadOnlyList<CesElement> Elements,
    IReadOnlyList<CorrectionRow> Corrections,
    MappingResolution Resolution,
    long? LatestBatchId)
{
    public IReadOnlyList<MappingResult> Results => Resolution.Results;

    public IReadOnlyList<Issue> Issues => Resolution.Issues;

    public P1sElement? FindP1s(string pspnr) =>
        P1s?.FirstOrDefault(e => Shared.Utils.Mapping.MappingKeys.Key(e.Pspnr) == Shared.Utils.Mapping.MappingKeys.Key(pspnr));
}

/// <summary>Wynik zapisu korekty: błędy i ostrzeżenia walidacji albo komunikat.</summary>
public sealed record CorrectionSaveResult(bool Success, IReadOnlyList<Issue> Issues, string Message);
