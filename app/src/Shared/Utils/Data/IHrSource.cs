using PzlEv.Shared.Models.Hr;

namespace PzlEv.Shared.Utils.Data;

/// <summary>Odczyt pracowników z PZLHRPROD (HR.ORG, tylko odczyt) – źródło słownika Osoby.</summary>
public interface IHrSource
{
    /// <summary>Pracownicy z niepustym USRID.</summary>
    IReadOnlyList<HrPerson> Persons();
}
