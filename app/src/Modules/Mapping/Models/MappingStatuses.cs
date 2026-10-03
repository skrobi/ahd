namespace PzlEv.Modules.Mapping.Models;

/// <summary>Wynik rozstrzygania mapowania elementu CES (docs/mapowanie-ces-p1s.md, rozdz. 5) – wyliczany, nie zapisywany.</summary>
public static class MappingStatuses
{
    /// <summary>Korekta elementu CES (z uzasadnieniem).</summary>
    public const string Override = "OVERRIDE";

    /// <summary>Przypisanie z raportu mapowań.</summary>
    public const string Report = "REPORT";

    /// <summary>WBS spoza raportu – cel projektu CES (z korekty projektu CES albo z raportu).</summary>
    public const string Inherited = "INHERITED";

    /// <summary>Brak celu.</summary>
    public const string Unmapped = "UNMAPPED";
}

/// <summary>Rodzaj korekty mapowania (docs/mapowanie-ces-p1s.md, rozdz. 4).</summary>
public static class CorrectionKinds
{
    public const string Element = "ELEMENT";

    public const string Project = "PROJEKT";

    public static string Label(string kind) => kind == Element ? "korekta elementu CES" : "korekta projektu CES";
}
