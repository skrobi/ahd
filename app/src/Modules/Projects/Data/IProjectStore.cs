using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Dictionaries;

namespace PzlEv.Modules.Projects.Data;

/// <summary>
/// Magazyn projektów – kontrakt przyszłych procedur i widoków (META_Project, META_PerformanceObjective). Zapis jest
/// jedną operacją: wszystko albo nic. Dane nie są usuwane – zmiana tworzy nową wersję, usunięcie zamyka bieżącą.
/// </summary>
public interface IProjectStore
{
    IReadOnlyList<ProjectInfo> Projects();

    ProjectInfo? Find(string code);

    /// <summary>Bieżąca nakładka Performance Objectives projektu.</summary>
    PoTree Objectives(string code);

    /// <summary>Elementy CES z nakładek innych projektów → kod projektu (O46: element w co najwyżej jednej nakładce).</summary>
    IReadOnlyDictionary<string, string> WbsOwners(string exceptCode);

    /// <summary>Elementy P1S ze słowników „WP i CAM” innych projektów → kod projektu.</summary>
    IReadOnlyDictionary<string, string> P1sOwners(string exceptCode);

    /// <summary>
    /// Rejestruje projekt z nakładką. Odrzucony (StoreResult.Conflict), gdy kod jest zajęty albo element CES należy
    /// już do nakładki innego projektu.
    /// </summary>
    StoreResult Create(string code, string name, string type, PoTree objectives);

    /// <summary>
    /// Zapisuje docelowy stan nakładki: nowe węzły, zmienione (nowa wersja) i usunięte (zamknięcie wersji). Węzeł
    /// zmieniony w międzyczasie przez kogoś innego albo element CES innego projektu = konflikt, nic nie zapisano.
    /// </summary>
    StoreResult SaveObjectives(string code, PoTree objectives);

    /// <summary>
    /// Raport kosztów projektu (procedura REP_ProjectCosts, migracja 013): nagłówki (kolumny okresu i lat zależą od
    /// danych) i wiersze. value – pole kwoty parsera ACTUALS (np. ValueObjCrcy).
    /// </summary>
    (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows) CostReport(string code, string value);

    /// <summary>
    /// Koszt rzeczywisty (ACWP) projektu po WBS elemencie CES z ostatniego importu ACTUALS – suma całego zrzutu, bez
    /// wykluczeń projektu (procedura REP_ProjectCostsByElement, migracja 016).
    /// </summary>
    IReadOnlyDictionary<string, decimal> CostsByElement(string code, string value);

    /// <summary>
    /// Koszt (pole kwoty value) i godziny (pole hours, np. TotalQuantity) po elemencie CES jednym wywołaniem
    /// REP_ProjectCostsByElement (migracja 036) – ACWP i Actual DLH struktury; ACWP Labor / Material (migracja 038). asOf – stan danych
    /// (pliki zaimportowane do tej chwili – zatwierdzony przebieg, migracja 054); null – ostatni import.
    /// </summary>
    ElementActuals CostsAndHoursByElement(string code, string value, string hours, DateTimeOffset? asOf = null);

    /// <summary>
    /// Koszt bazowy projektu CAS po elemencie WBS × Cost Element × rok × okres w PLN, z Grouping i opisami raportu kosztów
    /// (procedura REP_ProjectCasCosts, migracja 045) – podstawa kosztu po narzutach (CasCosts). asOf – jak CostsAndHoursByElement.
    /// </summary>
    IReadOnlyList<CasCostRow> CasCosts(string code, DateTimeOffset? asOf = null);

    /// <summary>
    /// Ostatni przebieg projektu z zatwierdzonymi danymi ACTUALS (tabele modułu Przebiegi): zakończony etap „Zatwierdzenie ACTUALS”,
    /// a w uruchomieniu bez tego etapu – zakończony pierwszy etap walidacji (walidacja ACTUALS, także z zaakceptowanymi błędami); kod,
    /// numer uruchomienia, stan danych (koniec przypięcia stanu) i kto zatwierdził; null – brak.
    /// </summary>
    ApprovedActuals? ApprovedActuals(string code);

    /// <summary>Kody walut z kolumny currencyField (ObjectCurrency, ReportCurrency) w ostatnim imporcie ACTUALS projektu.</summary>
    IReadOnlyList<string> ProjectCurrencies(string code, string currencyField);

    /// <summary>Kody walut z kolumny currencyField w ostatnim imporcie ACTUALS dla kodów Project definition / WBS element (projekt jeszcze niezapisany – kreator).</summary>
    IReadOnlyList<string> CurrenciesOf(IReadOnlyCollection<string> definitions, string currencyField);

    /// <summary>
    /// Pliki ACTUALS, z których są koszty (najnowsza wersja każdego pliku – CAN_LatestFiles): data raportu w RABIT, import
    /// i ostatnie sprawdzenie pliku w RABIT bez zmian treści (META_SourceFileSeen: pominięty / duplikat).
    /// </summary>
    IReadOnlyList<ActualsFile> ActualsFiles();
}
