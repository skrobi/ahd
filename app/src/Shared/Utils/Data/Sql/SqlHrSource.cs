using Dapper;
using PzlEv.Shared.Models.Hr;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// PZLHRPROD przez SQL (pzl-ev.json, Environments.&lt;Env&gt;.PzlHrProd; schemat HR, tabela ORG): kolumny czytane jako
/// tekst, tylko pracownicy z niepustym USRID.
/// </summary>
public sealed class SqlHrSource(SqlDatabase db) : IHrSource
{
    public IReadOnlyList<HrPerson> Persons()
    {
        using var connection = db.Open();
        return connection.Query<Row>(
                $"""
                SELECT CAST(USRID AS NVARCHAR(128)) AS Usrid, CAST(PERNR AS NVARCHAR(20)) AS Pernr, CAST(VORNA AS NVARCHAR(100)) AS FirstName,
                       CAST(NACHN AS NVARCHAR(100)) AS LastName, CAST(EMAIL AS NVARCHAR(200)) AS Email, CAST(KOSTL AS NVARCHAR(40)) AS CostCenter,
                       CAST(SHORT AS NVARCHAR(100)) AS DepartmentShort, CAST(LONG AS NVARCHAR(400)) AS DepartmentName,
                       CAST(STEXT AS NVARCHAR(200)) AS Position, CAST(PION AS NVARCHAR(100)) AS Division, CAST(ISMANAGER AS NVARCHAR(10)) AS IsManager
                FROM {db.Table("ORG")}
                WHERE USRID IS NOT NULL AND LTRIM(RTRIM(CAST(USRID AS NVARCHAR(128)))) <> ''
                """,
                commandTimeout: 300)
            .Select(r => new HrPerson(T(r.Usrid), T(r.Pernr), T(r.FirstName), T(r.LastName), T(r.Email), T(r.CostCenter), T(r.DepartmentShort),
                T(r.DepartmentName), T(r.Position), T(r.Division), T(r.IsManager)))
            .ToList();
    }

    private static string T(string? value) => value?.Trim() ?? "";

    private sealed class Row
    {
        public string? Usrid { get; set; }
        public string? Pernr { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? CostCenter { get; set; }
        public string? DepartmentShort { get; set; }
        public string? DepartmentName { get; set; }
        public string? Position { get; set; }
        public string? Division { get; set; }
        public string? IsManager { get; set; }
    }
}
