namespace PzlEv.Shared.Models.PzlProd;

/// <summary>
/// Element WBS P1S z PZLPROD.LOG.WBS (docs/zrodla-danych.md, rozdz. 5.1) – wartości jako tekst, bez zmian względem
/// źródła (poza obcięciem spacji). Aktywność: LOEKZ niepuste = usunięty; Z_ACTIVE puste, 0 albo N = nieaktywny
/// (faktyczne wartości pokazuje Diagnostyka). Element nieaktywny albo usunięty zostaje w drzewie – może mieć koszty.
/// </summary>
public sealed record P1sElement(
    string Pspnr,
    string Parent,
    int? Level,
    string WbsElement,
    string ProjOrg,
    string Project,
    string ProjectName,
    string Name,
    string GroupDescription,
    string ProfitCenter,
    string CategoryGroup,
    string Category,
    string ActiveFlag,
    string DeletionFlag)
{
    public bool IsDeleted => DeletionFlag.Length > 0;

    public bool IsInactive => ActiveFlag.Length == 0 || ActiveFlag is "0" || ActiveFlag.Equals("N", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nieaktywny albo usunięty – w drzewie wyszarzony, jako cel korekty WARNING.</summary>
    public bool IsGreyed => IsDeleted || IsInactive;

    /// <summary>Opis do wyświetlenia: tekst elementu, a bez niego nazwa projektu.</summary>
    public string Description => Name.Length > 0 ? Name : ProjectName;

    public string Label => Description.Length > 0 ? $"{WbsElement} – {Description}" : WbsElement;
}
