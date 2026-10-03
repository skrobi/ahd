using PzlEv.Shared.Models.Mapping;

namespace PzlEv.Shared.Utils.Mapping;

/// <summary>
/// Klucze porównań mapowania: kody i PSPNR bez spacji i bez rozróżniania wielkości liter; numer złożony z cyfr bez zer
/// wiodących (PSPNR z PZLPROD bywa zapisany „00012345”, a w Excelu „12345”).
/// </summary>
public static class MappingKeys
{
    public static string Key(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length > 0 && text.All(char.IsAsciiDigit))
        {
            var trimmed = text.TrimStart('0');
            return trimmed.Length == 0 ? "0" : trimmed;
        }
        return text.ToUpperInvariant();
    }

    /// <summary>
    /// Wiersz raportu → przypisania: element CES (wbs_ces, a w wierszu src = CES także wbs) → element P1S (pspnr_sap,
    /// a w wierszu src = SAP także pspnr) i projekt CES → project_sap (docs/mapowanie-ces-p1s.md, rozdz. 2–3).
    /// </summary>
    public static ReportEntry Entry(ReportRow row)
    {
        var source = T(row.Src).ToUpperInvariant();
        var cesElement = T(row.WbsCes) is { Length: > 0 } wbsCes ? wbsCes : source == "CES" ? T(row.Wbs) : "";
        var targetPspnr = T(row.PspnrSap) is { Length: > 0 } pspnrSap ? pspnrSap : source == "SAP" ? T(row.Pspnr) : "";
        var targetWbs = T(row.WbsSap) is { Length: > 0 } wbsSap ? wbsSap : source == "SAP" ? T(row.Wbs) : "";
        return new ReportEntry(row.RowNumber, source, cesElement, T(row.ProjectCes), targetPspnr, targetWbs, T(row.ProjectSap));
    }

    private static string T(string? value) => value?.Trim() ?? "";
}
