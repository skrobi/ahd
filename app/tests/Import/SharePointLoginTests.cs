using System.Net;
using PzlEv.Modules.Import.Services;
using Xunit;

namespace PzlEv.Tests.Import;

/// <summary>Adres folderu SharePoint i logowanie jak Office (MS-OFBA) na symulowanej bramie.</summary>
public sealed class SharePointLoginTests
{
    private const string Folder = "https://sp.example.com/sites/RabbitReporting/Shared%20Documents/E456659";
    private const string FolderPath = "/sites/RabbitReporting/Shared%20Documents/E456659/";

    private static SharePointAddress Address => SharePointAddress.Parse(Folder)!;

    /// <summary>Symulowana brama: odpowiedź według metody i ścieżki; reszta – 401.</summary>
    private sealed class FakeGateway(Func<HttpRequestMessage, HttpResponseMessage?> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request) ?? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") });
        }
    }

    private static HttpResponseMessage Redirect(string to) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) }, Content = new StringContent("") };

    [Theory]
    [InlineData("https://sp.example.com/sites/RabbitReporting/Shared%20Documents/E456659")]
    [InlineData("https://sp.example.com/sites/RabbitReporting/Shared%20Documents/Forms/AllItems.aspx?RootFolder=%2Fsites%2FRabbitReporting%2FShared%20Documents%2FE456659&View=x")]
    [InlineData(@"\\sp.example.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659")]
    public void Address_from_browser_link_or_webdav_path(string link)
    {
        var address = SharePointAddress.Parse(link)!;

        Assert.Equal("https://sp.example.com/sites/RabbitReporting", address.SiteUrl);
        Assert.Equal("/sites/RabbitReporting/Shared Documents/E456659", address.Folder);
        Assert.Equal(Folder + "/", address.FolderUrl);
        Assert.Equal(@"\\sp.example.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659", address.Unc);
    }

    [Theory]
    [InlineData(@"C:\dane\RABIT")]
    [InlineData(@"\\serwer\udzial\RABIT")]
    public void Folder_outside_sharepoint_has_no_address(string path) => Assert.Null(SharePointAddress.Parse(path));

    [Fact]
    public void Ofba_answer_gives_login_window_like_office()
    {
        var gateway = new FakeGateway(request =>
            request.Method.Method == "OPTIONS" && request.Headers.TryGetValues(SharePointLogin.AcceptedHeader, out var accepted) && accepted.Single() == "t"
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Headers =
                    {
                        { SharePointLogin.RequiredHeader, "https://gate.example.com/my.policy?ofba=1" },
                        { SharePointLogin.ReturnUrlHeader, "https://gate.example.com/ofba/done/" },
                        { SharePointLogin.DialogSizeHeader, "800x600" },
                    },
                    Content = new StringContent(""),
                }
                : null);
        using var login = new SharePointLogin(gateway);

        var request = login.Prepare(Address);

        Assert.True(request.IsOfba);
        Assert.Equal("https://gate.example.com/my.policy?ofba=1", request.LoginUrl.AbsoluteUri);
        Assert.Equal((800, 600), (request.Width, request.Height));
        Assert.True(request.IsDone(new Uri("https://gate.example.com/ofba/done/?x=1")));
        Assert.False(request.IsDone(new Uri("https://gate.example.com/my.policy")));
        Assert.Equal(FolderPath, Assert.Single(gateway.Requests).RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Webdav_agent_variant_is_tried_when_options_is_redirected()
    {
        var gateway = new FakeGateway(request => request.Method.Method switch
        {
            "OPTIONS" => Redirect("https://gate.example.com/F5Networks-SSO-Req"),
            "PROPFIND" when request.Headers.UserAgent.ToString().Contains("Microsoft-WebDAV-MiniRedir") =>
                new HttpResponseMessage(HttpStatusCode.Forbidden) { Headers = { { SharePointLogin.RequiredHeader, "/login" } }, Content = new StringContent("") },
            _ => null,
        });
        using var login = new SharePointLogin(gateway);

        var request = login.Prepare(Address);

        Assert.True(request.IsOfba);
        Assert.Equal("https://sp.example.com/login", request.LoginUrl.AbsoluteUri);
        Assert.Equal(request.LoginUrl, request.ReturnUrl);   // bez adresu powrotu – adres logowania ([MS-OFBA] 2.2.2)
        Assert.Equal((660, 495), (request.Width, request.Height));
        Assert.Equal(3, gateway.Requests.Count);
    }

    [Fact]
    public void Without_ofba_window_opens_folder_and_ends_on_sharepoint_page()
    {
        using var login = new SharePointLogin(new FakeGateway(_ => Redirect("https://gate.example.com/my.policy")));

        var request = login.Prepare(Address);

        Assert.False(request.IsOfba);
        Assert.Equal(Folder + "/", request.LoginUrl.AbsoluteUri);
        Assert.False(request.IsDone(new Uri("https://gate.example.com/my.policy")));
        Assert.False(request.IsDone(new Uri("https://sp.example.com/my.policy")));
        Assert.True(request.IsDone(new Uri("https://sp.example.com/sites/RabbitReporting/Shared%20Documents/Forms/AllItems.aspx?RootFolder=x")));
    }
}
