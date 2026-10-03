using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Pipeline;
using Xunit;

namespace PzlEv.Tests.Projects;

/// <summary>Dane podstawowe projektu (F4.1): kod unikalny i nadający się na nazwę folderu.</summary>
public sealed class ProjectRulesTests
{
    [Fact]
    public void Project_with_existing_code_is_rejected()
    {
        var issues = ProjectRules.ValidateBasics("M28", "Modernizacja", ProjectTypes.Internal, ["S70I", "m28"]);
        Assert.Contains(issues, i => i.Level == CheckLevel.Error && i.Message.Contains("już istnieje"));
    }

    [Theory]
    [InlineData("M28", true)]
    [InlineData("S70I-2026_A", true)]
    [InlineData("", false)]
    [InlineData("M 28", false)]
    [InlineData("M28\\X", false)]
    [InlineData("-M28", false)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", false)]
    public void Code_must_fit_folder_and_file_names(string code, bool valid)
    {
        var issues = ProjectRules.ValidateBasics(code, "Nazwa", ProjectTypes.Sac, []);
        Assert.Equal(valid, issues.All(i => i.Level != CheckLevel.Error));
    }

    [Fact]
    public void Name_and_type_are_required()
    {
        var issues = ProjectRules.ValidateBasics("M28", " ", "XYZ", []);
        Assert.Contains(issues, i => i.Element == "Nazwa");
        Assert.Contains(issues, i => i.Element == "Typ");
        Assert.Equal("M28", ProjectRules.NormalizeCode(" m28 "));
    }
}
