using System.Net;
using System.Text;
using PzlEv.Modules.Import.Models;
using PzlEv.Modules.Import.Services;
using Xunit;

namespace PzlEv.Tests.Import;

/// <summary>Test dostępu do SharePoint (metody A–H) na symulowanym serwerze SharePoint.</summary>
public sealed class RabitAccessTestTests
{
    private const string Folder = "https://sp.example.com/sites/RabbitReporting/Shared%20Documents/E456659";
    private const string FolderPath = "/sites/RabbitReporting/Shared%20Documents/E456659";

    private static AccessTestInput Input(string file = "") => new(Folder, file, AccessTestInput.RabitListId, AccessTestInput.RabitViewId);

    /// <summary>Symulowany SharePoint: odpowiedź według metody i ścieżki; zapisuje żądania.</summary>
    private sealed class FakeSharePoint : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public FakeSharePoint On(string method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _routes[$"{method} {path}"] = respond;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            return Task.FromResult(_routes.TryGetValue(key, out var respond)
                ? respond(request)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Headers = { WwwAuthenticate = { new("Negotiate"), new("NTLM") } }, Content = new StringContent("") });
        }
    }

    private static HttpResponseMessage Xml(string xml, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") };

    private const string Multistatus = """
        <?xml version="1.0" encoding="utf-8"?>
        <D:multistatus xmlns:D="DAV:">
          <D:response><D:href>https://sp.example.com/sites/RabbitReporting/Shared%20Documents/E456659</D:href>
            <D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
          <D:response><D:href>/sites/RabbitReporting/Shared%20Documents/E456659/ACTUALS_PAF2_B6_AC1.xlsx</D:href>
            <D:propstat><D:prop><D:getcontentlength>1234</D:getcontentlength><D:resourcetype/></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
          <D:response><D:href>/sites/RabbitReporting/Shared%20Documents/E456659/Archiwum/</D:href>
            <D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
        </D:multistatus>
        """;

    private const string Rows = """
        <xml xmlns:s="uuid:BDC6E3F0-6DA3-11d1-A2A3-00AA00C14882" xmlns:rs="urn:schemas-microsoft-com:rowset" xmlns:z="#RowsetSchema">
          <rs:data>
            <z:row ows_FSObjType="1;#1" ows_FileRef="1;#sites/RabbitReporting/Shared Documents/E456659/Archiwum" ows_LinkFilename="Archiwum" />
            <z:row ows_FSObjType="2;#0" ows_FileRef="2;#sites/RabbitReporting/Shared Documents/E456659/ACTUALS_PAF2_B6_AC1.xlsx" ows_LinkFilename="ACTUALS_PAF2_B6_AC1.xlsx" />
          </rs:data>
        </xml>
        """;

    [Theory]
    [InlineData("https://sp.example.com/sites/RabbitReporting/Shared%20Documents/E456659")]
    [InlineData("https://sp.example.com/sites/RabbitReporting/Shared%20Documents/Forms/AllItems.aspx?RootFolder=%2Fsites%2FRabbitReporting%2FShared%20Documents%2FE456659&View=x")]
    [InlineData(@"\\sp.example.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659")]
    public void Address_from_browser_link_or_webdav_path(string link)
    {
        var address = SharePointAddress.Parse(link)!;

        Assert.Equal("https://sp.example.com/sites/RabbitReporting", address.SiteUrl);
        Assert.Equal("/sites/RabbitReporting/Shared Documents/E456659", address.Folder);
        Assert.Equal("Shared Documents/E456659", address.FolderInSite);
        Assert.Equal(Folder + "/", address.FolderUrl);
        Assert.Equal(@"\\sp.example.com@SSL\DavWWWRoot\sites\RabbitReporting\Shared Documents\E456659", address.Unc);
    }

    [Fact]
    public void Not_a_sharepoint_link_is_rejected()
    {
        Assert.Null(SharePointAddress.Parse(@"C:\dane\RABIT"));
        Assert.Throws<ArgumentException>(() => new RabitAccessTest(new FakeSharePoint()).Run(Input() with { FolderLink = @"\\serwer\udzial" }));
    }

    [Fact]
    public void Webdav_over_https_lists_files_and_download_uses_first_file()
    {
        var server = new FakeSharePoint()
            .On("PROPFIND", FolderPath + "/", _ => Xml(Multistatus, (HttpStatusCode)207))
            .On("GET", FolderPath + "/ACTUALS_PAF2_B6_AC1.xlsx", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1234]) });
        using var test = new RabitAccessTest(server);

        var result = test.Run(Input());

        var webdav = result.Methods.Single(m => m.Code == "E");
        Assert.True(webdav.Works);
        Assert.Equal("ACTUALS_PAF2_B6_AC1.xlsx", Assert.Single(webdav.Files).Name);
        Assert.Contains(webdav.Details, d => d.Contains("podfoldery: Archiwum"));
        var download = result.Methods.Single(m => m.Code == "D");
        Assert.True(download.Works);
        Assert.Contains(download.Details, d => d.StartsWith("Pobrano 1"));
        Assert.Contains("WebDAV przez HTTPS", result.Conclusion);
        var propfind = server.Requests.First(r => r.Method.Method == "PROPFIND");
        Assert.Equal("1", propfind.Headers.GetValues("Depth").Single());
        Assert.Equal("f", propfind.Headers.GetValues("X-FORMS_BASED_AUTH_ACCEPTED").Single());
        Assert.Equal(["A", "B", "C", "D", "E", "F", "G", "H"], result.Methods.Select(m => m.Code));
        Assert.Contains("[DZIAŁA] E.", result.Report);
    }

    [Fact]
    public void Owssvr_rest_and_soap_list_files_without_folders()
    {
        var server = new FakeSharePoint()
            .On("GET", "/sites/RabbitReporting/_vti_bin/owssvr.dll", _ => Xml(Rows))
            .On("POST", "/sites/RabbitReporting/_vti_bin/Lists.asmx", _ => Xml($"<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body><GetListItemsResponse><GetListItemsResult><listitems>{Rows.Replace("<xml", "<x").Replace("</xml>", "</x>")}</listitems></GetListItemsResult></GetListItemsResponse></soap:Body></soap:Envelope>"))
            .On("GET", "/sites/RabbitReporting/_api/web/GetFolderByServerRelativeUrl('%2Fsites%2FRabbitReporting%2FShared%20Documents%2FE456659')/Files".Replace("%2F", "/"), _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"d":{"results":[{"Name":"ACTUALS_PAF2_B6_AC1.xlsx","ServerRelativeUrl":"/sites/RabbitReporting/Shared Documents/E456659/ACTUALS_PAF2_B6_AC1.xlsx"}]}}""",
                        Encoding.UTF8, "application/json"),
                });
        using var test = new RabitAccessTest(server);

        var result = test.Run(Input());

        foreach (var code in new[] { "A", "F", "G" })
        {
            var method = result.Methods.Single(m => m.Code == code);
            Assert.True(method.Works, $"{code}: {method.Text}");
            var file = Assert.Single(method.Files);
            Assert.Equal("ACTUALS_PAF2_B6_AC1.xlsx", file.Name);
            Assert.Equal("https://sp.example.com" + FolderPath + "/ACTUALS_PAF2_B6_AC1.xlsx", file.Url);
        }
        var owssvr = server.Requests.First(r => r.RequestUri!.AbsolutePath.EndsWith("owssvr.dll"));
        Assert.Contains("XMLDATA=1", owssvr.RequestUri!.Query);
        Assert.Contains("RootFolder=%2Fsites%2FRabbitReporting%2FShared%20Documents%2FE456659", owssvr.RequestUri.Query);
        Assert.False(result.Methods.Single(m => m.Code == "D").Works);   // pobranie pliku: serwer odmawia (401)
        Assert.Contains("lista plików, ale nie pobieranie", result.Conclusion);
    }

    [Fact]
    public void Refused_login_and_redirect_to_login_page_are_described()
    {
        var server = new FakeSharePoint()
            .On("PROPFIND", FolderPath + "/", _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://adfs.example.com/adfs/ls/?wa=wsignin1.0&token=sekret") }, Content = new StringContent("") })
            .On("GET", "/adfs/ls/", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><head><title>Sign In</title></head></html>", Encoding.UTF8, "text/html") });
        using var test = new RabitAccessTest(server);

        var result = test.Run(Input());

        var webdav = result.Methods.Single(m => m.Code == "E");
        Assert.False(webdav.Works);
        Assert.Contains("HTTP 302 PROPFIND https://sp.example.com" + FolderPath + "/", webdav.Details);
        Assert.Contains(webdav.Details, d => d == "Tytuł strony: Sign In");
        Assert.DoesNotContain(webdav.Details, d => d.Contains("sekret"));   // bez parametrów zapytania (tokeny)
        var rest = result.Methods.Single(m => m.Code == "F");
        Assert.Contains("WWW-Authenticate: Negotiate, NTLM", rest.Details);
        Assert.Contains(rest.Details, d => d.StartsWith("Serwer nie przyjął logowania kontem Windows"));
        Assert.Contains("żadna metoda automatyczna nie działa", result.Conclusion);
    }

    [Fact]
    public void Windows_only_methods_are_skipped_elsewhere_and_missing_list_id_skips_list_methods()
    {
        using var test = new RabitAccessTest(new FakeSharePoint());

        var result = test.Run(Input() with { ListId = "" });

        Assert.Equal(AccessStatus.Skipped, result.Methods.Single(m => m.Code == "A").Status);
        Assert.Equal(AccessStatus.Skipped, result.Methods.Single(m => m.Code == "G").Status);
        Assert.Equal(AccessStatus.Skipped, result.Methods.Single(m => m.Code == "D").Status);
        if (!OperatingSystem.IsWindows())
            Assert.All(result.Methods.Where(m => m.Code is "B" or "C" or "H"), m => Assert.Equal(AccessStatus.Skipped, m.Status));
    }
}
