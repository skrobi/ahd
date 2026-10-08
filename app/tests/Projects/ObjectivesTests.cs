using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Files;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Nakładka Performance Objectives: wczytanie z Excela (F4.2), edycja drzewa, kontrola przed zapisem.</summary>
public sealed class ObjectivesTests
{
    private static string Shape(PoTree tree) =>
        string.Join(" ", tree.Flatten().Select(x => $"{x.Depth}:{x.Node.WbsElement ?? x.Node.Name}"));

    [Fact]
    public void Reference_excel_builds_expected_tree()
    {
        var result = PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx"));

        Assert.False(result.HasErrors);
        Assert.Empty(result.Issues);
        Assert.Equal(8, result.Tree.ElementCount);
        Assert.Equal(
            "0:4D06WP 1:4D06WP.RA 2:4D06WP.01 3:4D06WP000001 3:4D06WP000002 2:4D06WP.02 3:4D06WP000003 3:4D06WP000004",
            Shape(result.Tree));
        var ra = result.Tree.Nodes.Single(n => n.WbsElement == "4D06WP.RA");
        Assert.Equal("AC-CAB.6.38", ra.LegacyWbs);
        Assert.Equal("SAC-PZLCABMY10-00001", ra.PerformanceObligation);
        Assert.Equal("6156", ra.SacObjNumber);
        Assert.Equal("4D06WP", ra.ProjectDefinition);
        Assert.Equal("PIDCABIN", ra.ProfitCenter);
        Assert.True(result.Tree.Nodes.Single(n => n.WbsElement == "4D06WP").IsStatistical);
        Assert.True(result.Tree.Nodes.Single(n => n.WbsElement == "4D06WP000004").IsAcctAsstElement);
        Assert.All(result.Tree.Flatten(), x => Assert.Equal(x.Depth + 1, x.Node.Level));
    }

    [Fact]
    public void Missing_columns_duplicates_and_bad_level_are_errors()
    {
        var missing = PerformanceObjectivesReader.Read(new TabularData(["WBS element", "Name"], [], "csv", null, null, null), "a.csv");
        Assert.Contains(missing.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("'Level'"));

        var data = new TabularData(["Level", "WBS element", "Name"],
            [["1", "A", "a"], ["3", "A.1", "jump"], ["2", "A", "dup"], ["x", "B", "bad"]], "csv", null, null, null);
        var result = PerformanceObjectivesReader.Read(data, "b.csv");
        Assert.Contains(result.Issues, i => i.Level == CheckLevel.Warning && i.Message.Contains("Poziom 3 po poziomie 1"));
        Assert.Contains(result.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("Powtórzony WBS element A"));
        Assert.Contains(result.Issues, i => i.Level == CheckLevel.Error && i.Message.Contains("Level 'x'"));
        Assert.Equal("0:A 1:A.1", Shape(result.Tree));
    }

    [Fact]
    public void Tree_editing_moves_groups_and_removes()
    {
        var tree = PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx")).Tree;
        var root = tree.Nodes.Single(n => n.WbsElement == "4D06WP");
        var group = tree.AddVirtual(root.Key, "Inżynieria");
        var e1 = tree.Nodes.Single(n => n.WbsElement == "4D06WP000001");
        var e2 = tree.Nodes.Single(n => n.WbsElement == "4D06WP000002");

        Assert.True(tree.Move(e1.Key, group.Key));
        Assert.True(tree.Move(e2.Key, group.Key));
        Assert.Equal(3, e1.Level);
        Assert.False(tree.Move(group.Key, e1.Key));          // nie pod własnego potomka
        Assert.False(tree.CanMove(root.Key, group.Key));

        Assert.True(tree.Shift(e2.Key, -1));
        Assert.Equal(["4D06WP000002", "4D06WP000001"], tree.Children(group.Key).Select(n => n.WbsElement));

        Assert.True(tree.Remove(group.Key));                  // dzieci przechodzą do rodzica usuniętego węzła
        Assert.All(new[] { e1, e2 }, n => Assert.Equal(root.Key, n.ParentKey));
        Assert.Equal(8, tree.Nodes.Count);

        Assert.True(tree.Outdent(e1.Key));
        Assert.Null(e1.ParentKey);
        Assert.Equal(1, e1.Level);
    }

    [Fact]
    public void Validation_requires_element_and_rejects_element_of_other_project()
    {
        var empty = new PoTree();
        empty.AddVirtual(null, "Węzeł");
        Assert.Contains(ObjectivesValidator.Validate(empty, new Dictionary<string, string>()),
            i => i.Level == CheckLevel.Error && i.Message.Contains("nie zawiera żadnego elementu"));

        var tree = PerformanceObjectivesReader.Read(TestServices.TestData("Projekty", "PO_M28.xlsx")).Tree;
        Assert.DoesNotContain(ObjectivesValidator.Validate(tree, new Dictionary<string, string>()), i => i.Level == CheckLevel.Error);
        var issues = ObjectivesValidator.Validate(tree, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["4d06wp.02"] = "S70I" });
        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Element == "4D06WP.02" && i.Message.Contains("S70I"));
    }
}
