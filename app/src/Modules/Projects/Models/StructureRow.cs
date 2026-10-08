namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Wiersz struktury projektu (ekran Projekt, zakładka Struktura – docs/performance-objectives.md, rozdz. 4.2): węzeł
/// nakładki albo element P1S rozwinięty pod nim (LOG.WBS). P1s – kod P1S wiersza (klucz słownika „WP i CAM”);
/// Wp / Cam / CostCategory – przypisanie tego kodu; Wps i sumy – poddrzewo (każdy WP liczony raz). Gap – brak do
/// uzupełnienia (null – brak braków). Note – skąd jest wiersz P1S (np. cel mapowania, spoza LOG.WBS).
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
    bool HasChildren)
{
    /// <summary>Budżet i daty wiersza to wartości jego własnego WP (bez innych WP w poddrzewie) – można je edytować.</summary>
    public bool OwnsBudget => Wp is not null && Wps.Count == 1 && string.Equals(Wps[0], Wp, StringComparison.OrdinalIgnoreCase);
}
