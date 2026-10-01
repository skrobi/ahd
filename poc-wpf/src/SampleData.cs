using PzlEv.Test.Models;

namespace PzlEv.Test;

/// <summary>
/// Dane przykładowe Pulpitu – przeliczone z funkcji seed()/vPulpit() prototypu
/// (prototyp/pzl-ev-prototyp.html). Statyczne: aplikacja testowa nie łączy się z bazą.
/// </summary>
public static class SampleData
{
    public const string Environment = "TEST · PZL_EV_TEST";
    public const string User = @"PZL\a.wisniewska";
    public const string Eyebrow = "Tydzień 40 · wtorek 29.09.2026 · okres 2026-09";
    public const string AppVersion = "0.4.0";
    public const int SchemaVersion = 13;
    public const int DictState = 1432;

    // Kolejność etapów przebiegu tygodniowego: data, dict, val, join, fin, prod, fill, camval, ev.
    private static IReadOnlyList<StageDot> Dots(params string[] levels)
        => levels.Select(l => new StageDot(l)).ToList();

    public static IReadOnlyList<GlobalCard> GlobalCards() =>
    [
        new("Import RABIT", "G1–G2",
            "Ostatni import IMP-0187 · 28.09 08:10 · Magdalena Zielińska",
            [new Pill("ok", "6 zaimportowane"), new Pill("warn", "1 nierozpoznany")]),
        new("Mapowanie CES↔P1S", "G3",
            "24 WBS CES · 9 aktywnych reguł i wyjątków",
            [new Pill("ok", "INHERITED 15"), new Pill("info", "OVERRIDE 1"),
             new Pill("ready", "MAPPED 2"), new Pill("crit", "UNMAPPED 6")]),
        new("Baza słowników", "MS SQL",
            "Słowniki, mapowania i konfiguracja w centralnej bazie",
            [new Pill("ready", $"stan #{DictState}"), new Pill("muted", "edycja w aplikacji")]),
    ];

    public static IReadOnlyList<ZakresCard> ZakresCards() =>
    [
        new("S70i", "Sikorsky (SAC)", "S-70i – kontrakt SAC", null,
            "R-S70I-2026-09-T40", new Pill("warn", "Wymaga akcji"),
            Dots("ok", "ok", "ok", "ok", "ok", "ok", "warn", "muted", "muted"),
            "Tydzień 40 · 2026-09: Uzupełnienie braków – wymaga akcji",
            "Ostatnio: Magdalena Zielińska"),
        new("F16", "CAS Compliance", "F-16 – pakiety strukturalne",
            new Pill("crit", "3 UNMAPPED z propozycją w zakresie"),
            "R-F16-2026-09-T39", new Pill("ok", "Zakończony"),
            Dots("ok", "ok", "ok", "ok", "ok", "ok", "ok", "ok", "ok"),
            "Tydzień 39 · 2026-09: Zakończony",
            "Ostatnio: Anna Wiśniewska"),
        new("PULA-WEW", "Wewnętrzny", "Pula projektów wewnętrznych · pula", null,
            "R-PULA-2026-09-T40", new Pill("warn", "Wymaga akcji"),
            Dots("ok", "ok", "ok", "ok", "warn", "muted", "muted", "muted", "muted"),
            "Tydzień 40 · 2026-09: Pliki dla finansów – wymaga akcji",
            "Ostatnio: Anna Wiśniewska"),
    ];

    public static IReadOnlyList<AttentionItem> Attention() =>
    [
        new(new Pill("crit", "GLOBAL"), "6 WBS CES bez mapowania (UNMAPPED), koszt 364 490 PLN"),
        new(new Pill("warn", "S70i"), "Tydzień 40 · 2026-09: Uzupełnienie braków – wymaga akcji"),
        new(new Pill("warn", "F16"), "Brak przebiegu tygodniowego T40"),
        new(new Pill("info", "PULA-WEW"), "Tydzień 40 · 2026-09: pliki dla finansów czekają na zatwierdzenie"),
    ];

    public static IReadOnlyList<EventItem> Events() =>
    [
        new("28.09 15:40", "S70i", "Magdalena Zielińska", "Pobrano zaawansowanie z produkcji: 2 WP bez wartości do uzupełnienia"),
        new("28.09 11:02", "PULA-WEW", "Anna Wiśniewska", "Wygenerowano pliki dla finansów – oczekują na zatwierdzenie"),
        new("28.09 08:31", "S70i", "Magdalena Zielińska", "Rozpoczęto przebieg tygodniowy T40"),
        new("28.09 08:12", "GLOBAL", "Magdalena Zielińska", "Mapowanie po imporcie IMP-0187: 2 WBS odziedziczone, 5 UNMAPPED"),
        new("28.09 08:10", "GLOBAL", "Magdalena Zielińska", "Import IMP-0187: 6 plików zaimportowanych, 1 nierozpoznany"),
        new("21.09 14:10", "F16", "Anna Wiśniewska", "Zakończono przebieg T39 – EV wstępne R1"),
    ];
}
