namespace PzlEv.Shared.Models.PzlProd;

/// <summary>
/// Wartości produkcyjne elementu P1S (PSPNR) albo wydzielenia (VirtualCode – np. AC-R39.1.18.03.25.PAINT.W20) z PZLPROD –
/// odczyt na żywo (docs/zrodla-danych.md, rozdz. 6; docs/performance-objectives.md, rozdz. 4.2): godziny z LOG.vAHDD
/// przeliczone przez produktywność IPT (LOG.vAHDD_PL_CPI) z godzinami jakości DJK (% godzin TECH według stanowiska –
/// słownik „Wskaźniki DJK”): BacHours = TECH / produktywność + DJK, EvHours = TECH_PON / produktywność + DJK, AcHours = CATS;
/// ActualStart – najwcześniejszy rzeczywisty start zlecenia (GSTRI; wydzielenie – DATA_REAL operacji), ActualFinish –
/// najpóźniejszy rzeczywisty koniec (LTRMI; wydzielenie – DATA_REAL), gdy wszystkie zlecenia zamknięte (STAT = DONE);
/// materiały z LOG.vAPD (element P1S, nie wydzielenie): ActualMaterial – wartość dostarczona (statusy z „Parametry produkcji”),
/// MaterialValue – wartość wszystkich części, MaterialFee – narzut Z_CLO, Parts / PartsDelivered – liczba numerów części,
/// NextDeliveryYear – najbliższy rok planowanej dostawy części niedostarczonych; EtcHours = TECH_DO_PON / produktywność + DJK;
/// UndeliveredByYear – wartość części niedostarczonych według roku planowanej dostawy (0 – bez daty; USD jak MaterialValue);
/// PlanStart / PlanFinish – najwcześniejsza / najpóźniejsza planowana data operacji (DATA_PLAN); EtcByYear – ETC godzin według
/// planowanego roku poniesienia (kolumna R; 0 – bez roku).
/// </summary>
public sealed record ProductionValues(string Pspnr, decimal? AcHours, decimal? BacHours, decimal? EvHours, decimal? ActualMaterial,
    string? VirtualCode = null, DateOnly? ActualStart = null, DateOnly? ActualFinish = null, decimal? EtcHours = null,
    decimal? MaterialValue = null, decimal? MaterialFee = null, int? Parts = null, int? PartsDelivered = null, int? NextDeliveryYear = null,
    IReadOnlyDictionary<int, decimal>? UndeliveredByYear = null, DateOnly? PlanStart = null, DateOnly? PlanFinish = null,
    IReadOnlyDictionary<int, decimal>? EtcByYear = null);

/// <summary>
/// Wydzielenie godzin z elementu P1S (węzeł wirtualny nakładki z kodem ELEMENT_P1S.NAZWA.CPLGR) z PSPNR elementu: operacje
/// vAHDD elementu zgodne z regułą (Cplgr – prefiks grupy stanowisk; Swbs, Arbpl – puste = dowolne) należą do wydzielenia.
/// </summary>
public sealed record VirtualRule(string Code, string Pspnr, string? Swbs, string? Cplgr, string? Arbpl);

/// <summary>
/// Parametry odczytu produkcji (słowniki globalne „Wskaźniki DJK” i „Parametry produkcji”): udział DJK według prefiksu
/// grupy stanowisk (IPT), okno produktywności IPT w miesiącach, statusy vAPD liczone jako dostarczone, dzielenie wartości
/// przez (1 + Z_CLO). Default – wartości raportu produkcyjnego S70MR (gdy słowniki są puste).
/// </summary>
public sealed record ProductionParameters(IReadOnlyList<(string Prefix, decimal Share)> Djk, int ProductivityMonths,
    IReadOnlyList<string> DeliveredStatuses, bool DivideByZClo, bool IsDefault = false)
{
    public static ProductionParameters Default { get; } =
        new([("W2", 0.10m), ("W3", 0.10m), ("W4", 0.10m), ("W5", 0.15m), ("W6", 0.20m)], 12, ["DOST", "WYD"], true, IsDefault: true);
}

/// <summary>
/// Dane produkcyjne projektu: wartości po kluczu (Key – PSPNR elementu P1S albo VirtualKey wydzielenia), chwila
/// odczytu i błąd (PZLPROD niedostępny, brak widoku) – ekran działa dalej bez kolumn produkcyjnych; Note – uwagi (np.
/// parametry domyślne, element nadrzędny spoza LOG.WBS).
/// </summary>
public sealed record ProductionData(IReadOnlyDictionary<string, ProductionValues> ByPspnr, DateTimeOffset ReadAt, string? Error = null, string? Note = null)
{
    public static ProductionData Unavailable(string error, DateTimeOffset at) => new(new Dictionary<string, ProductionValues>(), at, error);

    /// <summary>Klucz wydzielenia w ByPspnr (kod – MappingKeys.Key).</summary>
    public static string VirtualKey(string code) => "v:" + code;
}
