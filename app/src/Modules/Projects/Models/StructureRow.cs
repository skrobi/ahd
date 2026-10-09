namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Wiersz struktury projektu (ekran Projekt, zakładka Struktura – docs/performance-objectives.md, rozdz. 4.2): węzeł
/// nakładki albo element P1S rozwinięty pod nim (LOG.WBS). P1s – kod P1S wiersza (klucz słownika „WP i CAM”);
/// Wp / Cam / CostCategory – przypisanie tego kodu; Wps i sumy – poddrzewo (każdy WP liczony raz). Gap – brak do
/// uzupełnienia (null – brak braków). Note – skąd jest wiersz P1S (np. cel mapowania, spoza LOG.WBS). Acwp – koszt
/// rzeczywisty z ostatniego importu ACTUALS: węzeł nakładki – elementy CES jego poddrzewa, element P1S – koszt
/// przypisany do WP poddrzewa (StructureBuilder). Bac – budżet kosztowy (BAC) WP poddrzewa.
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
    decimal Bac = 0)
{
    /// <summary>Budżet i daty wiersza to wartości jego własnego WP (bez innych WP w poddrzewie).</summary>
    public bool OwnsBudget => Wp is not null && Wps.Count == 1 && string.Equals(Wps[0], Wp, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// WP, którego budżet i daty pokazuje wiersz i do którego trafia ich zmiana: jedyny WP poddrzewa – własny WP wiersza
    /// albo jedyny WP pod nim (np. węzeł nakładki nad elementem P1S z WP). Kilka WP w poddrzewie (suma) – null.
    /// </summary>
    public string? BudgetWp => Wps.Count == 1 ? Wps[0] : null;
}
