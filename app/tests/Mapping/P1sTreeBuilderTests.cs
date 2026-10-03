using PzlEv.Modules.Mapping.Services;
using PzlEv.Shared.Utils.Mapping;
using PzlEv.Tests.TestSupport;
using Xunit;

namespace PzlEv.Tests.Mapping;

/// <summary>Drzewo P1S (docs/zrodla-danych.md, rozdz. 5.3) na małej strukturze LOG.WBS.</summary>
public sealed class P1sTreeBuilderTests
{
    [Fact]
    public void Tree_groups_projorg_by_categories_and_elements_by_parent()
    {
        var tree = P1sTreeBuilder.Build(P1sSample.Elements);

        Assert.Equal(["Internal Work", P1sTreeBuilder.NoCategory], tree.Select(n => n.Title));
        var category = Assert.Single(tree[0].Children);
        Assert.Equal("Development", category.Title);
        var description = Assert.Single(category.Children);
        Assert.Equal("Rozwój", description.Title);
        var projOrg = Assert.Single(description.Children);
        Assert.Equal("AC-I39", projOrg.Element!.WbsElement);

        // AC-I39 → AC-I39.1 → AC-I39.1.01 → .01, .02; element z rodzicem spoza LOG.WBS – pod swoim PROJORG
        Assert.Equal(["AC-I39.1", "AC-I39.9"], projOrg.Children.Select(c => c.Element!.WbsElement));
        var order = projOrg.Children[0].Children.Single();
        Assert.Equal("AC-I39.1.01", order.Element!.WbsElement);
        Assert.Equal(["AC-I39.1.01.01", "AC-I39.1.01.02"], order.Children.Select(c => c.Element!.WbsElement));
        Assert.Equal(6, projOrg.ElementCount);
    }

    [Fact]
    public void Projorg_without_category_goes_to_no_category_group_and_inactive_elements_stay_greyed()
    {
        var tree = P1sTreeBuilder.Build(P1sSample.Elements);

        var noCategory = tree.Single(n => n.Title == P1sTreeBuilder.NoCategory);
        var mc = Assert.Single(noCategory.Children);
        Assert.Equal("MC-00", mc.Element!.WbsElement);
        var inactive = Assert.Single(mc.Children);
        Assert.True(inactive.IsGreyed);   // Z_ACTIVE puste
        var deleted = tree.SelectMany(n => n.Descendants()).Single(n => n.Element?.WbsElement == "AC-I39.1.01.02");
        Assert.True(deleted.IsGreyed);    // LOEKZ = X
        Assert.Equal(P1sSample.Elements.Count, tree.Sum(n => n.ElementCount));   // każdy element dokładnie raz
    }

    [Fact]
    public void Parent_cycle_does_not_lose_or_duplicate_elements()
    {
        var cycle = new[]
        {
            P1sSample.E("1", "2", 2, "X.1", "X", "", "", "", "a"),
            P1sSample.E("2", "1", 2, "X.2", "X", "", "", "", "b"),
        };

        var tree = P1sTreeBuilder.Build(cycle);

        Assert.Equal(2, tree.Sum(n => n.ElementCount));
    }
}
