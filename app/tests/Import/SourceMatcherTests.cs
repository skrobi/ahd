using PzlEv.Modules.Import.Services;
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
}
