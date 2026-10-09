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

    /// <summary>
    /// Odczyt na żywo zakresu projektu: PSPNR do tabeli tymczasowej (#pspnr – sesja połączenia, bez praw zapisu w PZLPROD),
    /// godziny i materiały zsumowane po PSPNR w PZLPROD (docs/performance-objectives.md, rozdz. 4.2). Zapytania jak raport
    /// produkcyjny S70MR – zamiast filtra programu (Z_OPIS, SERNR_LO) zakres P1S projektu; wiersze „Paint” (Hangar W20) –
    /// w swoim elemencie.
    /// </summary>
    public IReadOnlyList<ProductionValues> Production(IReadOnlyCollection<string> pspnrs)
    {
        var keys = pspnrs.Select(p => long.TryParse(p.Trim(), out var value) ? value : (long?)null).OfType<long>().Distinct().ToList();
        if (keys.Count == 0)
            return [];
        using var connection = db.Open();
        connection.Execute("CREATE TABLE #pspnr (PSPNR BIGINT NOT NULL PRIMARY KEY);");
        foreach (var chunk in keys.Chunk(1000))   // liczby – literały bezpieczne; 1000 wierszy na INSERT … VALUES
            connection.Execute($"INSERT INTO #pspnr (PSPNR) VALUES {string.Join(", ", chunk.Select(k => $"({k})"))};");

        var hours = connection.Query<HoursRow>(
            $"""
            WITH Produktywnosc AS (
                SELECT IPT, SUM(CzTechPon) / SUM(CzRzecz) AS Factor
                FROM {db.Table("vAHDD_PL_CPI")}
                WHERE DataZakonczeniaOperacji >= DATEADD(YEAR, -1, GETDATE())
                GROUP BY IPT
                HAVING SUM(CzRzecz) > 0 AND SUM(CzTechPon) > 0),
            A AS (
                SELECT p.PSPNR, a.IPT, a.CATS, a.TECH, a.TECH_PON
                FROM {db.Table("vAHDD")} a
                JOIN #pspnr p ON p.PSPNR = TRY_CONVERT(BIGINT, a.PSPNR))
            SELECT CAST(A.PSPNR AS NVARCHAR(50)) AS Pspnr,
                   CAST(SUM(A.CATS) AS DECIMAL(28,8)) AS AcHours,
                   CAST(SUM(A.TECH / ISNULL(P.Factor, 1)) + SUM(ISNULL(K.Share, 0) * A.TECH) AS DECIMAL(28,8)) AS BacHours,
                   CAST(SUM(A.TECH_PON / ISNULL(P.Factor, 1)) + SUM(ISNULL(K.Share, 0) * A.TECH_PON) AS DECIMAL(28,8)) AS EvHours
            FROM A
            LEFT JOIN Produktywnosc P ON P.IPT = A.IPT
            LEFT JOIN (VALUES ('W2', 0.10), ('W3', 0.10), ('W4', 0.10), ('W5', 0.15), ('W6', 0.20)) K (Prefix, Share) ON LEFT(A.IPT, 2) = K.Prefix
            GROUP BY A.PSPNR
            """, commandTimeout: Timeout).ToDictionary(r => r.Pspnr);
        var materials = connection.Query<MaterialRow>(
            $"""
            SELECT CAST(p.PSPNR AS NVARCHAR(50)) AS Pspnr,
                   CAST(SUM(CASE WHEN v.STATUS IN ('DOST', 'WYD') THEN v.NETWR_USD / (1 + ISNULL(v.Z_CLO, 0)) ELSE 0 END) AS DECIMAL(28,8)) AS Delivered
            FROM {db.Table("vAPD")} v
            JOIN #pspnr p ON p.PSPNR = TRY_CONVERT(BIGINT, v.PSPNR)
            GROUP BY p.PSPNR
            """, commandTimeout: Timeout).ToDictionary(r => r.Pspnr);
        return hours.Keys.Union(materials.Keys)
            .Select(k => new ProductionValues(k, hours.GetValueOrDefault(k)?.AcHours, hours.GetValueOrDefault(k)?.BacHours, hours.GetValueOrDefault(k)?.EvHours,
                materials.GetValueOrDefault(k)?.Delivered))
            .ToList();
    }

    private sealed class HoursRow
    {
        public string Pspnr { get; set; } = "";
        public decimal? AcHours { get; set; }
        public decimal? BacHours { get; set; }
        public decimal? EvHours { get; set; }
    }

    private sealed class MaterialRow
    {
        public string Pspnr { get; set; } = "";
        public decimal? Delivered { get; set; }
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
