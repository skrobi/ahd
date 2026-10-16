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

    /// <summary>Port inny niż domyślny: składnia WebDAV Windows \\host@SSL@port\… – ta sama, którą czyta SharePointAddress.</summary>
    [Fact]
    public void Non_default_port_uses_webdav_port_syntax()
    {
        var unc = WebDavPath.ToUnc("https://sp.example.com:8443/sites/Rabbit/Shared%20Documents/E1");
        Assert.Equal(@"\\sp.example.com@SSL@8443\DavWWWRoot\sites\Rabbit\Shared Documents\E1", unc);
        Assert.Equal("https://sp.example.com:8443", PzlEv.Modules.Import.Services.SharePointAddress.Parse(unc)!.Origin);
    }

    [Fact]
    public void Local_folder_is_unchanged() => Assert.Equal(@"C:\RABIT\Do_importu", WebDavPath.ToUnc(@"C:\RABIT\Do_importu"));
}
