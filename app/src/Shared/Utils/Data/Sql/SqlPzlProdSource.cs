using Dapper;
using PzlEv.Shared.Models.PzlProd;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// PZLPROD przez SQL (pzl-ev.json, Environments.&lt;Env&gt;.PzlProd; schemat LOG): kolumny czytane jako tekst – zapytanie
/// nie zależy od typów w źródle (np. PSPNR liczbowy albo tekstowy, Z_ACTIVE bit albo znak).
/// </summary>
public sealed class SqlPzlProdSource(SqlDatabase db) : IPzlProdSource
{
    private const int Timeout = 300;

    private string Wbs => db.Table("WBS");

    private string WbsDic => db.Table("WBS_DIC");

    public IReadOnlyList<P1sElement> Elements()
    {
        using var connection = db.Open();
        return connection.Query<ElementRow>(
                $"""
                SELECT CAST(PSPNR AS NVARCHAR(50)) AS Pspnr, CAST(PARENT AS NVARCHAR(50)) AS Parent, TRY_CAST(STUFE AS INT) AS Level,
                       CAST(WBS_ELEMENT AS NVARCHAR(100)) AS WbsElement, CAST(PROJORG AS NVARCHAR(100)) AS ProjOrg,
                       CAST(PROJECT AS NVARCHAR(100)) AS Project, CAST(PROJNAME AS NVARCHAR(400)) AS ProjectName,
                       CAST(LTXA1 AS NVARCHAR(400)) AS Name, CAST(Z_OPIS AS NVARCHAR(400)) AS GroupDescription,
                       CAST(PRCTR AS NVARCHAR(50)) AS ProfitCenter, CAST(Z_KAT_ZBIORCZA AS NVARCHAR(200)) AS CategoryGroup,
                       CAST(Z_KATEGORIA AS NVARCHAR(200)) AS Category, CAST(Z_ACTIVE AS NVARCHAR(10)) AS ActiveFlag,
                       CAST(LOEKZ AS NVARCHAR(10)) AS DeletionFlag
                FROM {Wbs}
                """,
                commandTimeout: Timeout)
            .Select(r => new P1sElement(T(r.Pspnr), T(r.Parent), r.Level, T(r.WbsElement), T(r.ProjOrg), T(r.Project), T(r.ProjectName),
                T(r.Name), T(r.GroupDescription), T(r.ProfitCenter), T(r.CategoryGroup), T(r.Category), T(r.ActiveFlag), T(r.DeletionFlag)))
            .ToList();
    }

    public IReadOnlyList<P1sGroup> Groups()
    {
        using var connection = db.Open();
        return connection.Query<GroupRow>(
                $"""
                SELECT CAST(Z_PROJECT AS NVARCHAR(100)) AS Project, CAST(Z_GRP AS NVARCHAR(50)) AS Grouping,
                       CAST(Z_OPIS AS NVARCHAR(400)) AS Description, CAST(Z_KATEGORIA AS NVARCHAR(200)) AS Category,
                       CAST(Z_KAT_ZBIORCZA AS NVARCHAR(200)) AS CategoryGroup, CAST(Z_INFO AS NVARCHAR(400)) AS Info
                FROM {WbsDic}
                """,
                commandTimeout: Timeout)
            .Select(r => new P1sGroup(T(r.Project), T(r.Grouping), T(r.Description), T(r.Category), T(r.CategoryGroup), T(r.Info)))
            .ToList();
    }

    public PzlProdCheck Check()
    {
        using var connection = db.Open();
        var elements = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {Wbs}", commandTimeout: Timeout);
        var groups = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {WbsDic}", commandTimeout: Timeout);
        var flags = connection.Query<FlagRow>(
                $"""
                SELECT 'Z_ACTIVE' AS [Column], CAST(Z_ACTIVE AS NVARCHAR(10)) AS Value, COUNT(*) AS [Rows] FROM {Wbs} GROUP BY CAST(Z_ACTIVE AS NVARCHAR(10))
                UNION ALL
                SELECT 'LOEKZ', CAST(LOEKZ AS NVARCHAR(10)), COUNT(*) FROM {Wbs} GROUP BY CAST(LOEKZ AS NVARCHAR(10))
                """,
                commandTimeout: Timeout)
            .Select(r => new PzlProdFlagValue(r.Column, T(r.Value), r.Rows))
            .OrderBy(f => f.Column).ThenByDescending(f => f.Rows)
            .ToList();
        return new PzlProdCheck(elements, groups, flags);
    }

    private static string T(string? value) => value?.Trim() ?? "";

    private sealed class ElementRow
    {
        public string? Pspnr { get; set; }
        public string? Parent { get; set; }
        public int? Level { get; set; }
        public string? WbsElement { get; set; }
        public string? ProjOrg { get; set; }
        public string? Project { get; set; }
        public string? ProjectName { get; set; }
        public string? Name { get; set; }
        public string? GroupDescription { get; set; }
        public string? ProfitCenter { get; set; }
        public string? CategoryGroup { get; set; }
        public string? Category { get; set; }
        public string? ActiveFlag { get; set; }
        public string? DeletionFlag { get; set; }
    }

    private sealed class GroupRow
    {
        public string? Project { get; set; }
        public string? Grouping { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public string? CategoryGroup { get; set; }
        public string? Info { get; set; }
    }

    private sealed class FlagRow
    {
        public string Column { get; set; } = "";
        public string? Value { get; set; }
        public int Rows { get; set; }
    }
}
