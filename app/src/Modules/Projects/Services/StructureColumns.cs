namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Kolumny tabeli struktury projektu w grupach (docs/performance-objectives.md, rozdz. 4.2): WBS Attributes, Baseline
/// (wpisywane – daty i budżety), Schedule, Operational EV (godziny), Materials, Financial EV (koszty) – jedna struktura WBS
/// dla perspektywy operacyjnej i finansowej. Grupy zwijane jak grupowanie kolumn w Excelu (Schedule, Operational EV,
/// Materials, Financial EV – domyślnie zwinięte). Nazewnictwo EVM: BCWS / BCWP / ACWP. Opis kolumny – podpowiedź nagłówka
/// i okno „?”. FromView – wartość z danych (PZLPROD, ACTUALS) albo wyliczona: tylko do odczytu.
/// </summary>
public static class StructureColumns
{
    public const string WbsAttributes = "WBS Attributes";
    public const string Baseline = "Baseline";
    public const string Schedule = "Schedule";
    public const string OperationalEv = "Operational EV";
    public const string Materials = "Materials";
    public const string FinancialEv = "Financial EV";

    public const string Gaps = "Gaps";
    public const string WbsElement = "WbsElement";
    public const string PerformanceObligation = "PerformanceObligation";
    public const string ActualStart = StructureEdits.ActualStart;
    public const string ActualFinish = StructureEdits.ActualFinish;
    public const string Status = "Status";
    public const string OpsBacHours = "OpsBacHours";
    public const string PvHours = "PvHours";
    public const string EvHours = "EvHours";
    public const string PercentComplete = "PercentComplete";
    public const string AcHours = "AcHours";
    public const string EacHours = "EacHours";
    public const string SpiHours = "SpiHours";
    public const string CpiHours = "CpiHours";
    public const string MaterialValue = "MaterialValue";
    public const string MaterialFee = "MaterialFee";
    public const string ActualMaterial = "ActualMaterial";
    public const string PercentDelivered = "PercentDelivered";
    public const string Parts = "Parts";
    public const string NextDeliveryYear = "NextDeliveryYear";
    public const string PvCost = "PvCost";
    public const string EvCost = "EvCost";
    public const string AcwpLabor = "AcwpLabor";
    public const string AcwpMaterial = "AcwpMaterial";
    public const string AcwpOther = "AcwpOther";
    public const string Acwp = "Acwp";
    public const string ActualDlh = "ActualDlh";
    public const string DlhDelta = "DlhDelta";
    public const string Sv = "Sv";
    public const string Cv = "Cv";
    public const string Spi = "Spi";
    public const string Cpi = "Cpi";
    public const string EtcCost = "EtcCost";
    public const string Eac = "Eac";
    public const string Vac = "Vac";
    public const string PercentSpent = "PercentSpent";

    public sealed record Group(string Name, bool Collapsed, string Description);

    public sealed record Column(string Key, string Header, string Group, string Description, double Width = 110, bool Mono = false, bool Right = true,
        bool FromView = false);

    public static IReadOnlyList<Group> Groups { get; } =
    [
        new(WbsAttributes, false, "Atrybuty elementu WBS: drzewo nakładki z rozwinięciem P1S, braki, pakiet pracy, CAM, kategoria, MPK."),
        new(Baseline, false, "Baseline WP – wpisywane: daty planowane, budżet godzin, materiałów i kosztów (z wyceny)."),
        new(Schedule, true, "Harmonogram: daty rzeczywiste (wpisane albo z produkcji), prognoza i stan."),
        new(OperationalEv, true, "Perspektywa operacyjna – godziny z raportu AHD (PZLPROD, na żywo) i wskaźniki godzinowe."),
        new(Materials, true, "Materiały z vAPD (USD przeliczone kursem bieżącym z „Kursy walut” na walutę programu): wartość, fee, dostarczone, do poniesienia, części."),
        new(FinancialEv, true, "Perspektywa finansowa – koszty: BCWS, BCWP, ACWP (praca, materiał, inne), odchylenia, wskaźniki, prognoza."),
    ];

    public static IReadOnlyList<Column> All { get; } =
    [
        new(StructureEdits.Name, "WBS Name", WbsAttributes, "Nazwa elementu: węzeł nakładki Performance Objectives albo element P1S; poziom – wcięcie w drzewie (Alt+→ / Alt+← rozwija i zwija).", 360, Right: false),
        new(Gaps, "Braki", WbsAttributes, "Braki do uzupełnienia: element bez WP, WP bez budżetu, WP bez CAM / MPK, koszt bez WP – szczegóły w podpowiedzi ⚠.", 60, Right: false, FromView: true),
        new(WbsElement, "CES Element", WbsAttributes, "Element WBS CES (klucz nakładki, np. 4D06WP000001) – z eksportu SAP; tylko do odczytu.", 130, Mono: true, Right: false, FromView: true),
        new(StructureEdits.P1s, "Legacy Element (P1S)", WbsAttributes, "Kod elementu P1S (Legacy WBS węzła albo element LOG.WBS); w węźle nakładki edytowalny (Legacy WBS).", 160, Mono: true, Right: false),
        new(PerformanceObligation, "Performance Obligation", WbsAttributes, "Zobowiązanie wykonania SAC elementu nakładki (eksport SAP).", 170, Mono: true, Right: false, FromView: true),
        new(StructureEdits.Wp, "WP", WbsAttributes, "Work Package – znacznik elementu P1S, który ma koszty i budżet (kod WP = kod elementu P1S).", 50, Right: false),
        new(StructureEdits.Cam, "CAM", WbsAttributes, "Cost Account Manager WP – lista osób (słownik Osoby z HR); WP bez CAM blokuje przebieg.", 180, Right: false),
        new(StructureEdits.CostCategory, "Cost Category", WbsAttributes, "Kategoria WP jako rodzaj kosztu do raportu po kategoriach – słownik programu „Kategorie WBS”.", 150, Right: false),
        new(StructureEdits.Mpk, "MPK", WbsAttributes, "MPK WP – słownik „Stawki wydziałów”; stawka ETC Cost (Labor Rate × Overhead z bieżącego roku).", 150, Right: false),

        new(StructureEdits.Start, "Baseline Start", Baseline, "Planowany start WP (baseline, RRRR-MM-DD) – „Harmonogram i budżet”.", 110, Mono: true, Right: false),
        new(StructureEdits.Finish, "Baseline Finish", Baseline, "Planowany koniec WP (baseline, RRRR-MM-DD) – „Harmonogram i budżet”.", 110, Mono: true, Right: false),
        new(StructureEdits.BacHours, "BAC Hours", Baseline, "Budżet godzin WP – „Harmonogram i budżet” (wpisywany)."),
        new(StructureEdits.BacMaterial, "BAC Material", Baseline, "Budżet materiałów WP – „Harmonogram i budżet”, w walucie programu (wpisany w innej walucie – przeliczony kursem bieżącym)."),
        new(StructureEdits.Bac, "BAC Cost", Baseline, "Budżet kosztowy WP z wyceny (oferty) – „Harmonogram i budżet”; w walucie programu (wpisany w innej walucie – przeliczony kursem bieżącym); podstawa BCWS, BCWP, VAC, % Spent."),

        new(ActualStart, "Actual Start", Schedule, "Rzeczywisty start WP – wpisany w „Harmonogram i budżet” (Actual Start), a bez niego z produkcji: najwcześniejszy GSTRI zleceń (wydzielenie – DATA_REAL). Wyczyszczenie komórki przywraca datę z produkcji.", 110, Mono: true, Right: false),
        new(ActualFinish, "Actual Finish", Schedule, "Rzeczywisty koniec WP – wpisany (Actual Koniec), a bez niego z produkcji: najpóźniejszy LTRMI, gdy wszystkie zlecenia zamknięte (inaczej puste). Wyczyszczenie komórki przywraca datę z produkcji.", 110, Mono: true, Right: false),
        new(StructureEdits.ForecastStart, "Forecast Start", Schedule, "Prognozowany start WP – „Harmonogram i budżet” (wpisywany).", 110, Mono: true, Right: false),
        new(StructureEdits.ForecastFinish, "Forecast Finish", Schedule, "Prognozowany koniec WP – „Harmonogram i budżet” (wpisywany).", 110, Mono: true, Right: false),
        new(Status, "Status", Schedule, "Stan z dat rzeczywistych: zakończony (Actual Finish), w toku (Actual Start), nie rozpoczęty.", 110, Right: false, FromView: true),

        new(OpsBacHours, "TECH Hours", OperationalEv, "Budżet godzin z AHD (vAHDD): TECH ÷ produktywność IPT + godziny jakości DJK. Suma elementów P1S poddrzewa.", FromView: true),
        new(PvHours, "BCWS Hours", OperationalEv, "Godziny planowane do dziś: TECH Hours WP rozłożone liniowo na dni robocze (pn–pt) baseline.", FromView: true),
        new(EvHours, "BCWP Hours", OperationalEv, "Godziny wypracowane wg AHD (vAHDD): TECH_PON ÷ produktywność IPT + DJK.", FromView: true),
        new(PercentComplete, "% Complete", OperationalEv, "Zaawansowanie z produkcji: BCWP Hours ÷ TECH Hours × 100.", 90, FromView: true),
        new(AcHours, "ACWP Hours (CATS)", OperationalEv, "Godziny rzeczywiste zaraportowane w CATS (vAHDD).", FromView: true),
        new(StructureEdits.EtcHours, "ETC Hours", OperationalEv, "Godziny do wykonania – wpisane w „Harmonogram i budżet” (ETC HOURS), a bez nich z AHD: TECH_DO_PON ÷ produktywność + DJK. Wyczyszczenie przywraca wartość z produkcji."),
        new(EacHours, "EAC Hours", OperationalEv, "ACWP Hours (CATS) + ETC Hours.", FromView: true),
        new(SpiHours, "SPI (h)", OperationalEv, "BCWP Hours ÷ BCWS Hours.", 80, FromView: true),
        new(CpiHours, "CPI (h)", OperationalEv, "BCWP Hours ÷ ACWP Hours (CATS).", 80, FromView: true),

        new(MaterialValue, "Material Value", Materials, "Wartość wszystkich części elementu (vAPD): Σ NETWR_USD ÷ (1 + Z_CLO) – bez fee („Parametry produkcji”); USD → waluta programu.", FromView: true),
        new(MaterialFee, "Material Fee", Materials, "Narzut Z_CLO: Σ NETWR_USD − Σ NETWR_USD ÷ (1 + Z_CLO); USD → waluta programu.", FromView: true),
        new(ActualMaterial, "Material Delivered", Materials, "Wartość części dostarczonych i wydanych (statusy z „Parametry produkcji”), bez fee; USD → waluta programu.", FromView: true),
        new(StructureEdits.MaterialEtc, "Material ETC", Materials, "Materiały do poniesienia – wpisane w „Harmonogram i budżet” (ETC MATERIAL, waluta programu), a bez nich Material Value − Material Delivered. Wyczyszczenie przywraca wartość z produkcji."),
        new(PercentDelivered, "% Delivered", Materials, "Material Delivered ÷ Material Value × 100.", 90, FromView: true),
        new(Parts, "PN / Delivered", Materials, "Liczba numerów części (MATNR): wszystkich / dostarczonych.", 100, FromView: true),
        new(NextDeliveryYear, "Next Delivery", Materials, "Najbliższy rok planowanej dostawy części niedostarczonych (EINDT_MAX).", 90, FromView: true),

        new(PvCost, "BCWS", FinancialEv, "Koszt planowany do dziś: BAC Cost × udział dni roboczych baseline, które minęły.", FromView: true),
        new(EvCost, "BCWP", FinancialEv, "Koszt wypracowany: BAC Cost × (BCWP Hours ÷ TECH Hours), najwyżej BAC Cost.", FromView: true),
        new(AcwpLabor, "ACWP Hours", FinancialEv, "Koszt rzeczywisty pracy – cost elementy z rodzajem kosztu Hours (słownik Cost Category). Dwuklik – okno „Zmiany ACWP”.", FromView: true),
        new(AcwpMaterial, "ACWP Materials", FinancialEv, "Koszt rzeczywisty materiałów – cost elementy z rodzajem kosztu Materials (słownik Cost Category). Dwuklik – okno „Zmiany ACWP”.", FromView: true),
        new(AcwpOther, "ACWP Other", FinancialEv, "Pozostały koszt rzeczywisty – cost elementy z rodzajem Other albo bez rodzaju kosztu. Dwuklik – okno „Zmiany ACWP”.", FromView: true),
        new(Acwp, "ACWP", FinancialEv, "Koszt rzeczywisty narastająco z ostatniego importu ACTUALS (zestaw walutowy – „Parametry programu”), bez wykluczeń i rozliczenia wychodzącego = ACWP Hours + ACWP Materials + ACWP Other. Dwuklik – okno „Zmiany ACWP”.", FromView: true),
        new(ActualDlh, "Actual DLH", FinancialEv, "Godziny zaksięgowane w CES – kolumna „Total Quantity” ACTUALS; te same wiersze i przypisanie do WP co ACWP.", FromView: true),
        new(DlhDelta, "Δ DLH vs CATS", FinancialEv, "Actual DLH − ACWP Hours (CATS).", FromView: true),
        new(Sv, "SV", FinancialEv, "BCWP − BCWS.", FromView: true),
        new(Cv, "CV", FinancialEv, "BCWP − ACWP.", FromView: true),
        new(Spi, "SPI", FinancialEv, "BCWP ÷ BCWS.", 80, FromView: true),
        new(Cpi, "CPI", FinancialEv, "BCWP ÷ ACWP.", 80, FromView: true),
        new(EtcCost, "ETC Cost", FinancialEv, "Koszt do poniesienia: ETC Hours × Labor Rate × Overhead MPK (bieżący rok) + Material ETC – w walucie programu (kurs ze słownika „Kursy walut” obowiązujący dziś).", FromView: true),
        new(Eac, "EAC", FinancialEv, "ACWP + ETC Cost.", FromView: true),
        new(Vac, "VAC", FinancialEv, "BAC Cost − EAC.", FromView: true),
        new(PercentSpent, "% Spent", FinancialEv, "ACWP ÷ BAC Cost × 100.", 90, FromView: true),
    ];

    public static Column Get(string key) => All.First(c => c.Key == key);
}
