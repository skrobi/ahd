namespace PzlEv.Shared.Models.PzlProd;

/// <summary>
/// Wartości produkcyjne elementu P1S (PSPNR) z PZLPROD – odczyt na żywo (docs/zrodla-danych.md, rozdz. 6;
/// docs/performance-objectives.md, rozdz. 4.2): godziny z LOG.vAHDD przeliczone przez produktywność IPT z ostatnich
/// 12 miesięcy (LOG.vAHDD_PL_CPI) z godzinami jakości DJK (% godzin TECH według stanowiska: W2–W4 10%, W5 15%, W6 20%):
/// BacHours = TECH / produktywność + DJK, EvHours = TECH_PON / produktywność + DJK, AcHours = CATS; materiały z LOG.vAPD:
/// ActualMaterial – wartość dostarczona / wydana (STATUS DOST, WYD) w USD bez narzutu Z_CLO.
/// </summary>
public sealed record ProductionValues(string Pspnr, decimal? AcHours, decimal? BacHours, decimal? EvHours, decimal? ActualMaterial);

/// <summary>
/// Dane produkcyjne projektu: wartości po PSPNR (klucz – MappingKeys.Key), chwila odczytu i błąd (PZLPROD niedostępny,
/// brak widoku) – ekran działa dalej bez kolumn produkcyjnych. DatesNote – dlaczego brak dat rzeczywistych (Actual Start /
/// Finish – kolumny dat vAHDD do ustalenia, O10).
/// </summary>
public sealed record ProductionData(IReadOnlyDictionary<string, ProductionValues> ByPspnr, DateTimeOffset ReadAt, string? Error = null)
{
    public const string DatesNote = "Actual Start / Actual Finish – z godzin AHD (pierwsza data godzin CATS, data osiągnięcia EV = BAC); kolumny dat LOG.vAHDD do ustalenia (O10) – na razie puste.";

    public static ProductionData Unavailable(string error, DateTimeOffset at) => new(new Dictionary<string, ProductionValues>(), at, error);
}
