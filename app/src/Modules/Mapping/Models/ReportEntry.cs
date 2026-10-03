namespace PzlEv.Modules.Mapping.Models;

/// <summary>
/// Wiersz raportu mapowań SAP↔CES (docs/mapowanie-ces-p1s.md, rozdz. 2) sprowadzony do rozstrzygania: element CES
/// (wbs_ces, a w wierszu src = CES – wbs) → element P1S (pspnr_sap, a w wierszu src = SAP – pspnr) oraz odpowiednik
/// projektu CES (project_ces → project_sap). Puste pola – wiersz nie wnosi danego przypisania.
/// </summary>
public sealed record ReportEntry(int RowNumber, string Source, string CesElement, string CesProject, string TargetPspnr, string TargetWbs, string SapProject);

/// <summary>Najnowszy zaimportowany plik raportu mapowań i jego wiersze.</summary>
public sealed record ReportInfo(long FileId, string FileName, DateTimeOffset ImportedAt, IReadOnlyList<ReportEntry> Entries)
{
    public string Describe => $"{FileName} (import {ImportedAt.ToLocalTime():yyyy-MM-dd HH:mm}, wierszy {Entries.Count})";
}
