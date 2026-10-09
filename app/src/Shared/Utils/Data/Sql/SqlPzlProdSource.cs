using System.IO;
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
    /// Odczyt na żywo zakresu projektu – zapytanie sql/pzlprod/produkcja.sql (wbudowane): PSPNR, reguły elementów wirtualnych
    /// i parametry do tabel tymczasowych sesji (bez praw zapisu w PZLPROD), wynik – godziny i daty po elemencie (P1S albo
    /// wirtualny) i materiały po PSPNR (docs/zrodla-danych.md, rozdz. 6).
    /// </summary>
    public IReadOnlyList<ProductionValues> Production(IReadOnlyCollection<string> pspnrs, IReadOnlyCollection<VirtualRule> rules, ProductionParameters parameters)
    {
        static long? Number(string value) => long.TryParse(value.Trim(), out var number) ? number : null;
        var keys = pspnrs.Select(Number).OfType<long>().Distinct().ToList();
        if (keys.Count == 0)
            return [];
        using var connection = db.Open();
        connection.Execute(
            """
            CREATE TABLE #pspnr (PSPNR BIGINT NOT NULL PRIMARY KEY);
            CREATE TABLE #rule (Code NVARCHAR(100) NOT NULL, PSPNR BIGINT NOT NULL, SWBS NVARCHAR(100) NULL, CPLGR NVARCHAR(100) NULL, ARBPL NVARCHAR(100) NULL);
            CREATE TABLE #djk (Prefix NVARCHAR(20) NOT NULL, Share DECIMAL(9,6) NOT NULL);
            CREATE TABLE #delivered (Status NVARCHAR(20) NOT NULL);
            """);
        foreach (var chunk in keys.Chunk(1000))   // liczby – literały bezpieczne; 1000 wierszy na INSERT … VALUES
            connection.Execute($"INSERT INTO #pspnr (PSPNR) VALUES {string.Join(", ", chunk.Select(k => $"({k})"))};");
        connection.Execute("INSERT INTO #rule (Code, PSPNR, SWBS, CPLGR, ARBPL) VALUES (@Code, @Pspnr, @Swbs, @Cplgr, @Arbpl);",
            rules.Where(r => Number(r.Pspnr) is not null).Select(r => new { r.Code, Pspnr = Number(r.Pspnr), r.Swbs, r.Cplgr, r.Arbpl }));
        connection.Execute("INSERT INTO #djk (Prefix, Share) VALUES (@Prefix, @Share);", parameters.Djk.Select(d => new { d.Prefix, d.Share }));
        connection.Execute("INSERT INTO #delivered (Status) VALUES (@Status);", parameters.DeliveredStatuses.Select(s => new { Status = s }));

        using var results = connection.QueryMultiple(_query.Value, new { months = parameters.ProductivityMonths, divideZClo = parameters.DivideByZClo },
            commandTimeout: Timeout);
        var hours = results.Read<HoursRow>().ToList();
        var materials = results.Read<MaterialRow>().ToDictionary(r => r.Pspnr);
        var values = hours.Select(h => new ProductionValues(h.Pspnr, h.AcHours, h.BacHours, h.EvHours,
                h.VirtualCode is null ? materials.GetValueOrDefault(h.Pspnr)?.Delivered : null, h.VirtualCode,
                h.ActualStart is { } start ? DateOnly.FromDateTime(start) : null, h.ActualFinish is { } finish ? DateOnly.FromDateTime(finish) : null))
            .ToList();
        values.AddRange(materials.Values.Where(m => !hours.Any(h => h.VirtualCode is null && h.Pspnr == m.Pspnr))
            .Select(m => new ProductionValues(m.Pspnr, null, null, null, m.Delivered)));
        return values;
    }

    /// <summary>Zapytanie z pliku sql/pzlprod/produkcja.sql z nazwami widoków PZLPROD (schemat z konfiguracji).</summary>
    private readonly Lazy<string> _query = new(() =>
    {
        using var stream = typeof(SqlPzlProdSource).Assembly.GetManifestResourceStream("PzlEv.PzlProd.produkcja.sql")
            ?? throw new InvalidOperationException("Brak wbudowanego zapytania PzlEv.PzlProd.produkcja.sql");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Replace("$(vAHDD_PL_CPI)", db.Table("vAHDD_PL_CPI"))
            .Replace("$(vAHDD)", db.Table("vAHDD"))
            .Replace("$(vAPD)", db.Table("vAPD"));
    });

    private sealed class HoursRow
    {
        public string Pspnr { get; set; } = "";
        public string? VirtualCode { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }
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
