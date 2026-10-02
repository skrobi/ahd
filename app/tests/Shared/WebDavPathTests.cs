using PzlEv.Shared.Utils.Files;
using Xunit;

namespace PzlEv.Tests.Shared;

public class WebDavPathTests
{
    private const string Expected = @"\\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659";

    [Theory]
    [InlineData("https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659")]
    [InlineData("https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659/")]
    [InlineData("https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/Forms/AllItems.aspx?RootFolder=%2Fsites%2FRabbitReporting%2FShared%20Documents%2FE456659&View=x")]
    [InlineData("https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/Forms/AllItems.aspx?id=%2Fsites%2FRabbitReporting%2FShared%20Documents%2FE456659")]
    [InlineData(@"  \\lmsp4-intl.external.lmco.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659  ")]
    public void SharePoint_links_become_webdav_unc_paths(string input) => Assert.Equal(Expected, WebDavPath.ToUnc(input));

    [Fact]
    public void Local_folder_is_unchanged() => Assert.Equal(@"C:\RABIT\Do_importu", WebDavPath.ToUnc(@"C:\RABIT\Do_importu"));
}
