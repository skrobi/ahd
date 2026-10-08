namespace PzlEv.Shared.Models.Hr;

/// <summary>
/// Pracownik z PZLHRPROD.HR.ORG (wartości jako tekst): USRID – numer znaczka (login, identyfikator CAM), PERNR – numer
/// systemowy, imię, nazwisko, e-mail, MPK (KOSTL), dział (SHORT – skrót, LONG – pełna nazwa), stanowisko (STEXT), pion,
/// znacznik managera.
/// </summary>
public sealed record HrPerson(
    string Usrid,
    string Pernr,
    string FirstName,
    string LastName,
    string Email,
    string CostCenter,
    string DepartmentShort,
    string DepartmentName,
    string Position,
    string Division,
    string IsManager)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
}
