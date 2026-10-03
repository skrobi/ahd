using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models.Db;
using Xunit;

namespace PzlEv.Tests.Import;

public class SourceMatcherTests
{
    [Theory]
    [InlineData("~$ACTUALS_PAF_01.xlsx", true)]
    [InlineData(".ukryty.csv", true)]
    [InlineData("ACTUALS_PAF_01.xlsx.part", true)]
    [InlineData("ACTUALS_PAF_01.xlsx", false)]
    public void Temporary_files_are_ignored(string name, bool ignored) => Assert.Equal(ignored, SourceMatcher.IsIgnored(name));

    private static SourceDefinitionRow Definition(string code, string prefix) =>
        new(1, 1, 1, code, prefix, "", "", true, DateTimeOffset.Now, "", null, null);

    [Theory]
    [InlineData("ACTUALS_*")]
    [InlineData("ACTUALS_")]
    [InlineData("actuals_ *")]
    public void Trailing_asterisk_in_prefix_means_starts_with(string prefix)
    {
        var match = SourceMatcher.Match("ACTUALS_PAF2_B6_AC1.xlsx", [Definition("ACTUALS", prefix)]);
        Assert.Equal("ACTUALS", match?.Code);
    }

    [Fact]
    public void Longest_prefix_wins_also_with_asterisk()
    {
        var match = SourceMatcher.Match("ACTUALS_PAF2_B6_AC1.xlsx", [Definition("ALL", "ACTUALS_*"), Definition("PAF2", "ACTUALS_PAF2")]);
        Assert.Equal("PAF2", match?.Code);
    }

    [Fact]
    public void No_match_returns_null() => Assert.Null(SourceMatcher.Match("RAPORT.xlsx", [Definition("ACTUALS", "ACTUALS_*")]));
}
