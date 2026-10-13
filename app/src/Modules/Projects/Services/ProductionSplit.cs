using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Mapping;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Wydzielenie godzin z elementu P1S (docs/performance-objectives.md, rozdz. 4.2): węzeł wirtualny nakładki z kodem P1S
/// (Legacy WBS) w konwencji ELEMENT_P1S.NAZWA.CPLGR, np. AC-R39.1.18.03.25.PAINT.W20 – operacje vAHDD elementu
/// AC-R39.1.18.03.25 z grupą stanowisk (CPLGR) zaczynającą się od W20 należą do węzła i są odejmowane od elementu.
/// Element – najdłuższy prefiks kodu (do kropki), który jest w LOG.WBS; za nim dokładnie dwa człony: nazwa i CPLGR.
/// </summary>
public static class ProductionSplit
{
    public sealed record Split(string Code, string Element, string Pspnr, string Name, string Cplgr);

    /// <summary>Wydzielenie z kodu albo null (kod jest elementem LOG.WBS, brak elementu w LOG.WBS, inna liczba członów).</summary>
    public static Split? Parse(string? code, IReadOnlyDictionary<string, P1sElement> byCode)
    {
        if (string.IsNullOrWhiteSpace(code) || byCode.ContainsKey(MappingKeys.Key(code)))
            return null;
        code = code.Trim();
        for (var dot = code.LastIndexOf('.'); dot > 0; dot = code.LastIndexOf('.', dot - 1))
        {
            if (!byCode.TryGetValue(MappingKeys.Key(code[..dot]), out var element))
                continue;
            var rest = code[(dot + 1)..].Split('.');
            return rest is [{ Length: > 0 } name, { Length: > 0 } cplgr] ? new Split(code, element.WbsElement, element.Pspnr, name, cplgr) : null;
        }
        return null;
    }

    /// <summary>Opis reguły do podpowiedzi i komunikatów.</summary>
    public static string Describe(Split split) =>
        $"wydzielenie z {split.Element}: operacje vAHDD z CPLGR {split.Cplgr}… (z DJK stanowiska); reszta godzin zostaje na {split.Element}";

    /// <summary>Indeks LOG.WBS po kodzie (MappingKeys.Key).</summary>
    public static IReadOnlyDictionary<string, P1sElement> Index(IReadOnlyList<P1sElement>? p1s) =>
        (p1s ?? []).GroupBy(e => MappingKeys.Key(e.WbsElement)).ToDictionary(g => g.Key, g => g.First());
}
