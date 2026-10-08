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
}
