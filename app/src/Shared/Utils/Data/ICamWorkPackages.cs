namespace PzlEv.Shared.Utils.Data;

/// <summary>CAM: numer (kolumna CAM słownika „WP i CAM” – numer znaczka), nazwisko i e-mail ze słownika „Osoby” (brak – numer, bez adresu).</summary>
public sealed record CamPerson(string Code, string Name, string? Email = null)
{
    public string Label => Name == Code ? Code : $"{Name} ({Code})";
}

/// <summary>
/// WP przypisany CAM-owi w programie – dane struktury programu (docs/performance-objectives.md, rozdz. 4.2) dla modułu CAM:
/// daty baseline i prognozy, budżet (godziny, materiały, koszt), BCWS / BCWP (podgląd EV struktury), ACWP (razem, robocizna,
/// materiały), godziny poniesione (CATS, a bez produkcji – Actual DLH z ACTUALS), ETC (wpis w „Harmonogram i budżet”
/// albo z produkcji), ETC z produkcji, materiały niedostarczone vAPD według roku dostawy, stawka godziny według roku (Labor Rate ×
/// Overhead ze „Stawki wydziałów” MPK WP, przeliczona do waluty programu). Kwoty w walucie programu (zestaw walutowy ACTUALS);
/// ToPln / ToUsd – przelicznik waluty programu (kurs bieżący, null – brak kursu); DataNote – braki danych (np. bez PZLPROD);
/// ProductionEtcByYear – ETC z produkcji według planowanego roku poniesienia (vAHDD R; 0 – bez roku); PlanStart / PlanFinish –
/// MIN / MAX DATA_PLAN zleceń WP (prognoza terminów, gdy nie jest wpisana); Finished – WP zakończony (status struktury z dat
/// rzeczywistych) – nie wymaga estymacji w cyklu; WeeklyCycle – program estymuje co tydzień (przebieg programu – RunDefinitions.CamWeekly);
/// ActualStart / ActualFinish – daty rzeczywiste WP ze struktury (wpisane w „Harmonogram i budżet” albo z produkcji).
/// </summary>
public sealed record CamWorkPackage(
    string Project,
    string ProjectName,
    string Wp,
    string Name,
    string Cam,
    string? Mpk,
    string? Currency,
    string? BaselineStart,
    string? BaselineFinish,
    string? ForecastStart,
    string? ForecastFinish,
    decimal BacHours,
    decimal BacMaterial,
    decimal Bac,
    decimal? Bcws,
    decimal? Bcwp,
    decimal Acwp,
    decimal AcwpLabor,
    decimal AcwpMaterial,
    decimal? ActualHours,
    bool Production,
    decimal? ProductionEtcHours,
    decimal? EtcHours,
    decimal? MaterialEtc,
    decimal? EtcCost,
    IReadOnlyDictionary<int, decimal> UndeliveredByYear,
    IReadOnlyDictionary<int, decimal> HourRates,
    decimal? ToPln,
    decimal? ToUsd,
    string? DataNote,
    IReadOnlyDictionary<int, decimal>? ProductionEtcByYear = null,
    string? PlanStart = null,
    string? PlanFinish = null,
    bool Finished = false,
    bool WeeklyCycle = false,
    string? ActualStart = null,
    string? ActualFinish = null);

/// <summary>
/// Moduł CAM – WP CAM-ów we wszystkich programach i przekazanie zatwierdzonej estymacji do struktury programu (słownik
/// „Harmonogram i budżet”). Rejestruje moduł Projekty (AppServices.Contracts), używa moduł CAM.
/// </summary>
public interface ICamWorkPackages
{
    /// <summary>Osoby przypisane jako CAM w „WP i CAM” programów, po nazwisku.</summary>
    IReadOnlyList<CamPerson> Cams();

    /// <summary>WP CAM-a we wszystkich programach (struktura każdego programu z jego danymi); program bez danych – pominięty z notatką w DataNote.</summary>
    IReadOnlyList<CamWorkPackage> WorkPackages(string cam);

    /// <summary>
    /// WP z przypisanym CAM-em w programie (project null – we wszystkich programach) – estymacje wymagane w cyklu i postęp: struktura bez
    /// kosztów ACTUALS (ACWP, waluta programu – puste), z datami, statusem i produkcją.
    /// </summary>
    IReadOnlyList<CamWorkPackage> ProjectWorkPackages(string? project);

    /// <summary>
    /// Zatwierdzona estymacja WP do „Harmonogram i budżet” programu: ETC HOURS, ETC MATERIAL (waluta currency), Forecast Start /
    /// Koniec, Actual Start / Koniec (daty rzeczywiste podane przez CAM-a) – nowa wersja wiersza WP (historia słownika); null – kolumna bez
    /// zmiany. Zwraca błąd zapisu albo null.
    /// </summary>
    string? ApplyEstimate(string project, string wp, decimal etcHours, decimal materialEtc, string? currency, DateOnly? forecastStart,
        DateOnly? forecastFinish, DateOnly? actualStart = null, DateOnly? actualFinish = null);
}
