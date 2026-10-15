using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Strona P1S nakładki: Legacy WBS z Performance Objectives i poddrzewa LOG.WBS (docs/performance-objectives.md, rozdz. 2, 4.1).</summary>
public sealed class ObjectivesMappingTests
{
    private static PoTree Objectives() => PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx")).Tree;

    private static P1sElement P1s(string pspnr, string parent, string wbs) =>
        new(pspnr, parent, null, wbs, "AC-CAB", "AC-CAB", "kabina", wbs, "", "", "", "", "X", "");

    /// <summary>LOG.WBS: pod AC-CAB.6.38 element AC-CAB.6.38.07 i pod nim XYZ-1 (kod spoza wzorca kropek).</summary>
    private static P1sInputs Inputs(bool withPzlProd = true) => new(
        withPzlProd ? [P1s("1", "", "AC-CAB"), P1s("100", "1", "AC-CAB.6.38"), P1s("101", "100", "AC-CAB.6.38.07"), P1s("102", "101", "XYZ-1"), P1s("200", "1", "AC-CAB.9.01")] : null,
        withPzlProd ? null : "Brak połączenia z PZLPROD");

    [Fact]
    public void Scope_contains_legacy_wbs_and_their_log_wbs_subtrees()
    {
        var scope = ProjectService.Scope(Objectives(), Inputs());

        Assert.Equal("AC-CAB.6.38", scope.RootOf("XYZ-1"));                     // pod Legacy WBS w LOG.WBS, kod spoza wzorca
        Assert.Equal("AC-CAB.6.38.03", scope.RootOf("AC-CAB.6.38.03.05"));       // pod Legacy WBS z Excela
        Assert.Contains("XYZ-1", scope.Elements());
        Assert.Null(ProjectService.Scope(Objectives(), Inputs(withPzlProd: false)).RootOf("XYZ-1"));

        var context = new ProjectDictionaryContext(scope, new Dictionary<string, string>(), new HashSet<string> { "Anna Nowak" }, null);
        var spec = ProjectDictionaries.For(ProjectDictionaries.WpCam, context);
        var rows = new[] { new DictRow(null, null, new Dictionary<string, string?> { ["Element P1S"] = "XYZ-1", ["WP"] = "WP-1", ["CAM"] = "Anna Nowak", ["Cost Category"] = null }) };
        Assert.DoesNotContain(DictionaryValidator.Validate(spec, rows), i => i.Level == CheckLevel.Error);
    }

    [Fact]
    public void Analytic_base_attaches_wp_through_legacy_wbs()
    {
        var tree = Objectives();
        var legacy = tree.Nodes.Single(n => n.WbsElement == "4D06WP000004").LegacyWbs!;
        DictRow[] wp = [new(null, null, new Dictionary<string, string?> { ["Element P1S"] = legacy, ["WP"] = "WP-9", ["CAM"] = "Anna Nowak" })];

        var analytic = ProjectService.Analytic(tree, wp, [], Inputs());

        Assert.Equal(["WP-9"], analytic.Rows.Single(r => r.WbsElement == "4D06WP000004").Wps);
        Assert.Contains(legacy, analytic.Rows.Single(r => r.WbsElement == "4D06WP000004").P1s);
    }

    /// <summary>Baza analityczna (kreator): BAC MATERIAL w walucie projektu – wiersz w innej walucie przeliczony, bez kursu – pusty i brak.</summary>
    [Fact]
    public void Analytic_base_converts_budget_to_project_currency()
    {
        var tree = Objectives();
        var legacy = tree.Nodes.Single(n => n.WbsElement == "4D06WP000004").LegacyWbs!;
        DictRow[] wp = [new(null, null, new Dictionary<string, string?> { ["Element P1S"] = legacy, ["WP"] = "WP-9", ["CAM"] = "Anna Nowak" })];
        DictRow Budget(string? currency) => new(null, null, new Dictionary<string, string?>
            { ["WP"] = "WP-9", ["BAC HOURS"] = "10", ["BAC MATERIAL"] = "100", ["BAC"] = "1000", ["Waluta"] = currency });
        var rates = new EtcRates(new Dictionary<string, decimal>(), 2026, "PLN", 1, 4m, null,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 4m, ["PLN"] = 1m });

        var usd = ProjectService.Analytic(tree, wp, [Budget("USD")], Inputs(), rates);
        Assert.Equal((10m, 400m, "PLN"), (usd.BacHours, usd.BacMaterial, usd.Currency));
        Assert.Empty(usd.CurrencyGaps!);
        Assert.Equal(100m, ProjectService.Analytic(tree, wp, [Budget(null)], Inputs(), rates).BacMaterial);   // pusta waluta – waluta projektu
        var eur = ProjectService.Analytic(tree, wp, [Budget("EUR")], Inputs(), rates);
        Assert.Equal(0m, eur.BacMaterial);
        Assert.Contains("WP WP-9: budżet w EUR – brak kursu EUR", Assert.Single(eur.CurrencyGaps!));
        Assert.Equal(100m, ProjectService.Analytic(tree, wp, [Budget("USD")], Inputs()).BacMaterial);   // bez kursów – bez przeliczenia
    }

    [Fact]
    public void Readiness_reports_p1s_side()
    {
        var tree = Objectives();
        Assert.Equal(CheckLevel.Pass, ObjectivesMapping.Check(tree, Inputs()).Level);

        var manual = new PoTree();
        manual.AddElement(null, "9Z99", "bez Legacy WBS", null);
        var issue = ObjectivesMapping.Check(manual, Inputs());
        Assert.Equal(CheckLevel.Warning, issue.Level);
        Assert.Contains("9Z99", issue.Message);

        Assert.Equal(CheckLevel.Warning, ObjectivesMapping.Check(tree, Inputs(false)).Level);
    }
}
