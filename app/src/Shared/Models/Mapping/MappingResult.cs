using PzlEv.Shared.Models;

namespace PzlEv.Shared.Models.Mapping;

/// <summary>
/// Wynik rozstrzygania dla elementu CES: status, cel P1S (PSPNR i kod WBS; kod bez PSPNR – cel spoza LOG.WBS),
/// skąd wynika i propozycja celu dla elementu bez przypisania.
/// </summary>
public sealed record MappingResult(
    string CesElement,
    string CesProject,
    string Status,
    string TargetPspnr,
    string TargetWbs,
    string Origin,
    bool HasCost,
    bool IsNew,
    string? ProposalPspnr = null,
    string? ProposalWbs = null)
{
    public bool IsMapped => Status != MappingStatuses.Unmapped;

    public string Target => IsMapped ? TargetWbs.Length > 0 ? TargetWbs : TargetPspnr : "";

    public string Proposal => ProposalWbs ?? ProposalPspnr ?? "";

    public string StatusColor => Status switch
    {
        MappingStatuses.Unmapped => HasCost ? "crit" : "warn",
        MappingStatuses.Override => "info",
        _ => "ok",
    };
}

/// <summary>Wynik rozstrzygania wszystkich elementów CES i reguły walidacji (docs/mapowanie-ces-p1s.md, rozdz. 10).</summary>
public sealed record MappingResolution(IReadOnlyList<MappingResult> Results, IReadOnlyList<Issue> Issues);
