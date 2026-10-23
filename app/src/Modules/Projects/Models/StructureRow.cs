namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Wiersz struktury projektu (ekran Projekt, zakładka Struktura – docs/performance-objectives.md, rozdz. 4.2): węzeł
/// nakładki albo element P1S rozwinięty pod nim (LOG.WBS). P1s – kod P1S wiersza (klucz słownika „WP i CAM”);
/// Wp / Cam / CostCategory – przypisanie tego kodu; Wps i sumy – poddrzewo (każdy WP liczony raz). Gap – brak do
/// uzupełnienia (null – brak braków). Note – skąd jest wiersz P1S (np. cel mapowania, spoza LOG.WBS). Acwp – koszt
/// rzeczywisty z ostatniego importu ACTUALS: węzeł nakładki – elementy CES jego poddrzewa, element P1S – koszt
/// przypisany do WP poddrzewa (StructureBuilder). Bac – budżet kosztowy (BAC) WP poddrzewa.
/// Operational EV (z PZLPROD na żywo, null – brak danych): OpsBacHours, EvHours, AcHours – suma elementów P1S poddrzewa
/// (vAHDD; OpsBacHours – TECH Hours); PvHours – TECH Hours WP poddrzewa (produkcyjne BacHours, nie BAC Hours z „Harmonogram
/// i budżet”) rozłożone liniowo na dni robocze baseline do dnia stanu. ActualMaterial – materiały dostarczone (vAPD).
/// Financial EV: PvCost = BAC Cost × udział dni roboczych baseline, EvCost = BAC Cost × (EV Hours / TECH Hours) WP, najwyżej
/// BAC Cost; ActualDlh – godziny rzeczywiste (Total Quantity) z ostatniego importu ACTUALS, przypisane
/// jak Acwp. ActualStart / ActualFinish – daty WP wpisane w „Harmonogram i budżet” (Actual Start / Koniec;
/// ActualStartManual, ActualFinishManual), a bez nich vAHDD (GSTRI, LTRMI; wydzielenie – DATA_REAL); koniec, gdy
/// wszystkie zlecenia zamknięte. ProductionEtcHours – ETC z produkcji (bez wpisu ETC HOURS), UndeliveredByYear – materiały
/// niedostarczone vAPD według roku dostawy w walucie projektu, ProductionEtcByYear – ETC z produkcji według planowanego roku
/// poniesienia (vAHDD R; 0 – bez roku), PlanStart / PlanFinish – MIN / MAX DATA_PLAN operacji (propozycja estymacji CAM).
/// CamEstimate / CamTone – estymacja CAM WP w bieżącym cyklu (ProjectService.WithCamEstimates): opis statusu i kolor wiersza.
/// </summary>
public sealed record StructureRow(
    string Id,
    string? ParentId,
    int Depth,
    GridRowKind Kind,
    long? NodeKey,
    bool IsVirtual,
    string Name,
    string? WbsElement,
    string? P1s,
    string? Note,
    bool IsGreyed,
    string? Wp,
    string? Cam,
    string? CostCategory,
    IReadOnlyList<string> Wps,
    decimal BacHours,
    decimal BacMaterial,
    string? Start,
    string? Finish,
    string? Gap,
    bool HasChildren,
    decimal Acwp = 0,
    decimal Bac = 0,
    string? ActualStart = null,
    string? ActualFinish = null,
    decimal? OpsBacHours = null,
    decimal? PvHours = null,
    decimal? EvHours = null,
    decimal? AcHours = null,
    decimal? ActualMaterial = null,
    decimal? PvCost = null,
    decimal? EvCost = null,
    decimal ActualDlh = 0,
    bool ActualStartManual = false,
    bool ActualFinishManual = false,
    string? PerformanceObligation = null,
    string? Mpk = null,
    string? ForecastStart = null,
    string? ForecastFinish = null,
    string? Status = null,
    decimal? EtcHours = null,
    bool EtcHoursManual = false,
    decimal? MaterialValue = null,
    decimal? MaterialFee = null,
    decimal? MaterialEtc = null,
    bool MaterialEtcManual = false,
    int? Parts = null,
    int? PartsDelivered = null,
    int? NextDeliveryYear = null,
    decimal AcwpLabor = 0,
    decimal AcwpMaterial = 0,
    decimal? EtcCost = null,
    decimal? ProductionEtcHours = null,
    IReadOnlyDictionary<int, decimal>? UndeliveredByYear = null,
    IReadOnlyDictionary<int, decimal>? ProductionEtcByYear = null,
    string? PlanStart = null,
    string? PlanFinish = null,
    string? CamEstimate = null,
    string? CamTone = null)
{
    // Wskaźniki EVM (docs/performance-objectives.md, rozdz. 4.2) – z wartości wiersza; brak danych albo dzielenie przez 0 – null.

    /// <summary>ACWP poza pracą i materiałem (rodzaj kosztu w Cost Category nieustawiony).</summary>
    public decimal AcwpOther => Acwp - AcwpLabor - AcwpMaterial;

    public decimal? EacHours => AcHours is null && EtcHours is null ? null : (AcHours ?? 0) + (EtcHours ?? 0);

    public decimal? SpiHours => Ratio(EvHours, PvHours);

    public decimal? CpiHours => Ratio(EvHours, AcHours);

    public decimal? PercentDelivered => Ratio(ActualMaterial, MaterialValue) * 100;

    /// <summary>Godziny zaksięgowane w CES (Actual DLH) − godziny CATS.</summary>
    public decimal? DlhDelta => ActualDlh == 0 && AcHours is null ? null : ActualDlh - (AcHours ?? 0);

    public decimal? Sv => EvCost - PvCost;

    public decimal? Cv => EvCost is { } ev ? ev - Acwp : null;

    public decimal? Spi => Ratio(EvCost, PvCost);

    public decimal? Cpi => Ratio(EvCost, Acwp);

    /// <summary>EAC = ACWP + ETC Cost (koszt pozostały z godzin × stawka MPK i materiałów do poniesienia).</summary>
    public decimal? Eac => EtcCost is { } etc ? Acwp + etc : null;

    public decimal? Vac => Bac != 0 && Eac is { } eac ? Bac - eac : null;

    public decimal? PercentSpent => Bac != 0 ? Acwp / Bac * 100 : null;

    private static decimal? Ratio(decimal? value, decimal? total) => value is { } v && total is { } t && t != 0 ? v / t : null;

    /// <summary>Budżet i daty wiersza to wartości jego własnego WP (także gdy pod nim są inne WP – np. wydzielenia).</summary>
    public bool OwnsBudget => Wp is not null;

    /// <summary>
    /// WP, którego budżet i daty pokazuje wiersz i do którego trafia ich zmiana: własny WP wiersza, a bez niego – jedyny WP
    /// poddrzewa (np. węzeł nakładki nad elementem P1S z WP). Bez własnego WP i z kilkoma WP w poddrzewie (suma) – null.
    /// </summary>
    public string? BudgetWp => Wp ?? (Wps.Count == 1 ? Wps[0] : null);

    /// <summary>
    /// Zaawansowanie godzin z produkcji (%) – EV Hours ÷ TECH Hours × 100 (jak EV ÷ BAC w raporcie operacyjnym S70MR);
    /// brak godzin TECH – null.
    /// </summary>
    public decimal? PercentComplete => OpsBacHours is > 0 && EvHours is { } ev ? ev / OpsBacHours.Value * 100 : null;
}
