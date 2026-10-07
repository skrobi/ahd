using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Mapping;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Models.PzlProd;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Strona P1S nakładki z globalnego mapowania CES ↔ P1S (docs/performance-objectives.md, rozdz. 2, 4.1).</summary>
public sealed class ObjectivesMappingTests
{
    private static PoTree Objectives() => PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx")).Tree;

    private static P1sElement P1s(string pspnr, string parent, string wbs) =>
        new(pspnr, parent, null, wbs, "AC-CAB", "AC-CAB", "kabina", wbs, "", "", "", "", "X", "");

    /// <summary>
    /// Raport: 4D06WP.RA → AC-CAB.6.38 (PSPNR 100), projekt CES 4D06WP → AC-CAB; korekta elementu 4D06WP000004 →
    /// AC-CAB.9.01. LOG.WBS: pod AC-CAB.6.38 element AC-CAB.6.38.07 i pod nim XYZ-1 (kod spoza wzorca kropek).
    /// </summary>
    private static MappingInputs Inputs(bool withPzlProd = true) => new(
        new ReportInfo(DateTimeOffset.Now, "PZL\\anowak",
        [
            new ReportEntry(2, "CES", "70002", "4D06WP.RA", "4D06WP", "100", "AC-CAB.6.38", ""),
            new ReportEntry(3, "CES", "70003", "4D06WP", "4D06WP", "1", "AC-CAB", "AC-CAB"),
        ]),
        [
            new CorrectionRow(1, 10, 1, CorrectionKinds.Element, "4D06WP000004", "200", "AC-CAB.9.01", null, "zmiana", DateTime.Today, null,
                DateTimeOffset.Now, "PZL\\anowak", null, null),
        ],
        withPzlProd ? [P1s("1", "", "AC-CAB"), P1s("100", "1", "AC-CAB.6.38"), P1s("101", "100", "AC-CAB.6.38.07"), P1s("102", "101", "XYZ-1"), P1s("200", "1", "AC-CAB.9.01")] : null,
        withPzlProd ? null : "Brak połączenia z PZLPROD");

    private static MappingResult Of(PoTree tree, IReadOnlyDictionary<long, MappingResult> mapping, string wbs) =>
        mapping[tree.Nodes.Single(n => n.WbsElement == wbs).Key];

    [Fact]
    public void Elements_of_objectives_are_resolved_like_mapping_screen()
    {
        var tree = Objectives();
        var mapping = ProjectService.Resolve(tree, Inputs());

        Assert.Equal((MappingStatuses.Report, "AC-CAB.6.38"), (Of(tree, mapping, "4D06WP.RA").Status, Of(tree, mapping, "4D06WP.RA").Target));
        Assert.Equal((MappingStatuses.Override, "AC-CAB.9.01"), (Of(tree, mapping, "4D06WP000004").Status, Of(tree, mapping, "4D06WP000004").Target));
        // Elementu nie ma w raporcie – dziedziczy odpowiednik projektu CES z kolumny Project definition.
        Assert.Equal((MappingStatuses.Inherited, "AC-CAB"), (Of(tree, mapping, "4D06WP000002").Status, Of(tree, mapping, "4D06WP000002").Target));
        Assert.Equal(8, mapping.Count);

        // Element dodany ręcznie bez Project definition – projekt CES najbliższego elementu nad nim.
        var manual = tree.AddElement(tree.Nodes.Single(n => n.WbsElement == "4D06WP.02").Key, "4D06WP000099", "ręczny", null);
        var withManual = ProjectService.Resolve(tree, Inputs());
        Assert.Equal((MappingStatuses.Inherited, "AC-CAB"), (withManual[manual.Key].Status, withManual[manual.Key].Target));

        var none = ProjectService.Resolve(tree, MappingInputs.None);
        Assert.All(none.Values, r => Assert.Equal(MappingStatuses.Unmapped, r.Status));
    }

    [Fact]
    public void Scope_contains_mapping_targets_and_their_log_wbs_subtrees()
    {
        var scope = ProjectService.Scope(Objectives(), Inputs());

        Assert.Equal("AC-CAB.6.38", scope.RootOf("XYZ-1"));                     // pod celem w LOG.WBS, kod spoza wzorca
        Assert.Equal("AC-CAB.9.01", scope.RootOf("ac-cab.9.01"));               // cel korekty
        Assert.Equal("AC-CAB.6.38.03", scope.RootOf("AC-CAB.6.38.03.05"));       // pod Legacy WBS z Excela
        Assert.Contains("XYZ-1", scope.Elements());
        Assert.Null(ProjectService.Scope(Objectives(), Inputs(withPzlProd: false)).RootOf("XYZ-1"));

        var context = new ProjectDictionaryContext(scope, new Dictionary<string, string>(), new HashSet<string> { "Anna Nowak" }, null);
        var spec = ProjectDictionaries.For(ProjectDictionaries.WpCam, context);
        var rows = new[] { new DictRow(null, null, new Dictionary<string, string?> { ["Element P1S"] = "XYZ-1", ["WP"] = "WP-1", ["CAM"] = "Anna Nowak", ["Cost Category"] = null }) };
        Assert.DoesNotContain(DictionaryValidator.Validate(spec, rows), i => i.Level == CheckLevel.Error);
    }

    [Fact]
    public void Analytic_base_attaches_wp_through_mapping()
    {
        var tree = Objectives();
        DictRow[] wp = [new(null, null, new Dictionary<string, string?> { ["Element P1S"] = "AC-CAB.9.01", ["WP"] = "WP-9", ["CAM"] = "Anna Nowak" })];

        var analytic = ProjectService.Analytic(tree, wp, [], Inputs());

        Assert.Equal(["WP-9"], analytic.Rows.Single(r => r.WbsElement == "4D06WP000004").Wps);     // cel korekty, nie Legacy WBS
        Assert.Contains("AC-CAB.9.01", analytic.Rows.Single(r => r.WbsElement == "4D06WP000004").P1s);
    }

    [Fact]
    public void Readiness_reports_p1s_side()
    {
        var tree = Objectives();
        Assert.Equal(CheckLevel.Pass, ObjectivesMapping.Check(tree, ProjectService.Resolve(tree, Inputs()), Inputs()).Level);

        var noReport = MappingInputs.None;
        Assert.Contains("jest pusty", ObjectivesMapping.Check(tree, ProjectService.Resolve(tree, noReport), noReport).Message);

        var manual = new PoTree();
        manual.AddElement(null, "9Z99", "bez mapowania", null);
        var issue = ObjectivesMapping.Check(manual, ProjectService.Resolve(manual, Inputs()), Inputs());
        Assert.Equal(CheckLevel.Warning, issue.Level);
        Assert.Contains("9Z99", issue.Message);

        Assert.Equal(CheckLevel.Warning, ObjectivesMapping.Check(tree, ProjectService.Resolve(tree, Inputs(false)), Inputs(false)).Level);
    }
}
