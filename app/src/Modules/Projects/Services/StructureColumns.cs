namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Kolumny tabeli struktury projektu w grupach (docs/performance-objectives.md, rozdz. 4.2): WBS Attributes, Schedule,
/// Operational EV (godziny), Materials, Financial EV (koszty) – jedna struktura WBS dla perspektywy operacyjnej
/// i finansowej. Grupy zwijane jak grupowanie kolumn w Excelu (Schedule, Operational EV, Financial EV – domyślnie
/// zwinięte). Opis kolumny – podpowiedź nagłówka i „Opis kolumn” pod tabelą. FromView – wartość z widoku / danych
/// (PZLPROD, ACTUALS) albo wyliczona: tylko do odczytu.
/// </summary>
public static class StructureColumns
{
    public const string WbsAttributes = "WBS Attributes";
    public const string Schedule = "Schedule";
    public const string OperationalEv = "Operational EV";
    public const string Materials = "Materials";
    public const string FinancialEv = "Financial EV";

    public const string ActualStart = "ActualStart";
    public const string ActualFinish = "ActualFinish";
    public const string OpsBacHours = "OpsBacHours";
    public const string PvHours = "PvHours";
    public const string EvHours = "EvHours";
    public const string AcHours = "AcHours";
    public const string ActualMaterial = "ActualMaterial";
    public const string PvCost = "PvCost";
    public const string EvCost = "EvCost";
    public const string Acwp = "Acwp";
    public const string WbsElement = "WbsElement";

    public sealed record Group(string Name, bool Collapsed, string Description);

    public sealed record Column(string Key, string Header, string Group, string Description, double Width = 110, bool Mono = false, bool Right = true,
        bool FromView = false);

    public static IReadOnlyList<Group> Groups { get; } =
    [
        new(WbsAttributes, false, "Atrybuty elementu WBS: drzewo nakładki z rozwinięciem P1S, pakiet pracy, CAM, kategoria."),
        new(Schedule, true, "Harmonogram: baseline (wpisywany) i daty rzeczywiste (z produkcji)."),
        new(OperationalEv, true, "Perspektywa operacyjna – godziny z raportu AHD (PZLPROD, na żywo)."),
        new(Materials, false, "Materiały: budżet i wartość dostarczona."),
        new(FinancialEv, true, "Perspektywa finansowa – koszty: budżet, wartość planowana i wypracowana, koszt rzeczywisty."),
    ];

    public static IReadOnlyList<Column> All { get; } =
    [
        new(StructureEdits.Name, "WBS Name", WbsAttributes, "Nazwa elementu: węzeł nakładki Performance Objectives albo element P1S; poziom – wcięcie w drzewie (Alt+→ / Alt+← rozwija i zwija).", 360, Right: false),
        new(WbsElement, "CES Element", WbsAttributes, "Element WBS CES (klucz nakładki, np. 4D06WP000001) – z eksportu SAP; tylko do odczytu.", 130, Mono: true, Right: false, FromView: true),
        new(StructureEdits.P1s, "Legacy Element (P1S)", WbsAttributes, "Kod elementu P1S (Legacy WBS węzła albo element LOG.WBS); w węźle nakładki edytowalny (Legacy WBS).", 160, Mono: true, Right: false),
        new(StructureEdits.Wp, "WP", WbsAttributes, "Work Package – znacznik elementu P1S, który ma koszty i budżet (kod WP = kod elementu P1S).", 50, Right: false),
        new(StructureEdits.Cam, "CAM", WbsAttributes, "Cost Account Manager WP – lista osób (słownik Osoby z HR); WP bez CAM blokuje przebieg.", 180, Right: false),
        new(StructureEdits.CostCategory, "Cost Category", WbsAttributes, "Kategoria WP jako rodzaj kosztu do raportu po kategoriach – słownik projektu „Kategorie WBS”.", 150, Right: false),

        new(StructureEdits.Start, "Baseline Start", Schedule, "Planowany start WP (baseline, RRRR-MM-DD) – „Harmonogram i budżet”.", 110, Mono: true, Right: false),
        new(StructureEdits.Finish, "Baseline Finish", Schedule, "Planowany koniec WP (baseline, RRRR-MM-DD) – „Harmonogram i budżet”.", 110, Mono: true, Right: false),
        new(ActualStart, "Actual Start", Schedule, "Rzeczywisty start – pierwsza data godzin (CATS) w AHD; kolumny dat vAHDD do ustalenia (O10) – na razie puste.", 110, Mono: true, Right: false, FromView: true),
        new(ActualFinish, "Actual Finish", Schedule, "Rzeczywisty koniec – data, gdy EV Hours osiągnęło BAC Hours (AHD); kolumny dat vAHDD do ustalenia (O10) – na razie puste.", 110, Mono: true, Right: false, FromView: true),

        new(OpsBacHours, "BAC Hours", OperationalEv, "Budżet godzin z AHD (vAHDD): TECH ÷ produktywność IPT (12 mies.) + godziny jakości DJK; może różnić się od BAC Hours baseline. Suma elementów P1S poddrzewa.", FromView: true),
        new(PvHours, "PV Hours", OperationalEv, "Godziny do wypracowania wg harmonogramu baseline: BAC Hours (AHD) WP rozłożone liniowo na dni robocze (pn–pt) od Baseline Start do Baseline Finish, stan na dziś.", FromView: true),
        new(EvHours, "EV Hours", OperationalEv, "Godziny zarobione wg raportu AHD (vAHDD): TECH_PON ÷ produktywność IPT + DJK.", FromView: true),
        new(AcHours, "AC Hours", OperationalEv, "Godziny rzeczywiste – zaraportowane godziny CATS (vAHDD).", FromView: true),

        new(StructureEdits.BacMaterial, "BAC Material", Materials, "Budżet materiałów WP – „Harmonogram i budżet”."),
        new(ActualMaterial, "Actual Material", Materials, "Materiały dostarczone / wydane (vAPD, STATUS DOST, WYD) – wartość w USD bez narzutu Z_CLO.", FromView: true),

        new(StructureEdits.BacHours, "BAC Hours baseline", FinancialEv, "Budżet godzin w baseline projektu – „Harmonogram i budżet” (wpisywany)."),
        new(StructureEdits.Bac, "BAC Cost", FinancialEv, "Budżet kosztowy WP (kwota) – „Harmonogram i budżet”."),
        new(PvCost, "PV Cost", FinancialEv, "Wartość planowana: BAC Cost × udział dni roboczych baseline, które minęły (stan na dziś).", FromView: true),
        new(EvCost, "EV Cost", FinancialEv, "Wartość wypracowana: BAC Cost × (EV Hours ÷ BAC Hours z AHD), najwyżej BAC Cost.", FromView: true),
        new(Acwp, "ACWP", FinancialEv, "Koszt rzeczywisty narastająco (PLN) z ostatniego importu ACTUALS, bez wykluczeń i rozliczenia wychodzącego.", FromView: true),
    ];

    public static Column Get(string key) => All.First(c => c.Key == key);
}
