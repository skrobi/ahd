using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Tests.TestSupport;

/// <summary>
/// Mała struktura P1S do testów (układ LOG.WBS – docs/zrodla-danych.md, rozdz. 5.1): PROJORG AC-I39 z kategoriami
/// i elementami do poziomu 4 (jeden usunięty), PROJORG MC-00 bez kategorii, element z rodzicem spoza LOG.WBS.
/// PSPNR z zerami wiodącymi jak w PZLPROD.
/// </summary>
public static class P1sSample
{
    public static IReadOnlyList<P1sElement> Elements { get; } =
    [
        E("00001000", "", 1, "AC-I39", "AC-I39", "Internal Work", "Development", "Rozwój", "Projekt AC-I39"),
        E("00001001", "00001000", 2, "AC-I39.1", "AC-I39", "Internal Work", "Development", "Rozwój", "Pakiet 1"),
        E("00001002", "00001001", 3, "AC-I39.1.01", "AC-I39", "Internal Work", "Development", "Rozwój", "Zlecenie 01"),
        E("00001003", "00001002", 4, "AC-I39.1.01.01", "AC-I39", "Internal Work", "Development", "Rozwój", "ENG"),
        E("00001004", "00001002", 4, "AC-I39.1.01.02", "AC-I39", "Internal Work", "Development", "Rozwój", "MFG", deleted: "X"),
        E("00002000", "", 1, "MC-00", "MC-00", "", "", "", "Projekt MC-00"),
        E("00002001", "00002000", 2, "MC-00.001", "MC-00", "", "", "", "Grupa 001", active: ""),
        E("00009999", "00008888", 3, "AC-I39.9", "AC-I39", "Internal Work", "Development", "Rozwój", "Bez rodzica"),
    ];

    public static P1sElement E(string pspnr, string parent, int level, string wbs, string projOrg, string categoryGroup, string category,
        string description, string name, string active = "X", string deleted = "") =>
        new(pspnr, parent, level, wbs, projOrg, projOrg, "", name, description, "PIDS70OM", categoryGroup, category, active, deleted);
}
