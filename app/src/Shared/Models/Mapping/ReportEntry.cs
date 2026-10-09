namespace PzlEv.Shared.Models.Mapping;

/// <summary>
/// Wiersz raportu mapowań SAP↔CES (docs/mapowanie-ces-p1s.md, rozdz. 2) sprowadzony do rozstrzygania: element CES
/// (wbs_ces, a w wierszu src = CES – wbs) → element P1S (pspnr_sap, a w wierszu src = SAP – pspnr) oraz odpowiednik
/// projektu CES (project_ces → project_sap). Puste pola – wiersz nie wnosi danego przypisania. RowNumber – kolejność
/// w słowniku (przy kilku celach obowiązuje pierwszy wiersz); Label – wiersz słownika po kluczu (src pspnr).
/// </summary>
public sealed record ReportEntry(int RowNumber, string Source, string Pspnr, string CesElement, string CesProject, string TargetPspnr, string TargetWbs, string SapProject)
{
    public string Label => $"{Source} {Pspnr}".Trim();
}

/// <summary>Bieżąca zawartość słownika „Raport mapowań CES ↔ P1S” i ostatnia zmiana (kto, kiedy).</summary>
public sealed record ReportInfo(DateTimeOffset ChangedAt, string ChangedBy, IReadOnlyList<ReportEntry> Entries)
{
    public string Describe => $"słownik, wierszy {Entries.Count} (zmiana {ChangedAt.ToLocalTime():yyyy-MM-dd HH:mm}, {ChangedBy})";
}
