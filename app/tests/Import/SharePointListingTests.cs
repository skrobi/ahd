using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using PzlEv.Modules.Import.Services;
using Xunit;

namespace PzlEv.Tests.Import;

/// <summary>Lista plików SharePoint przez HTTPS (PROPFIND) z sesją bramy z okna logowania – bez usługi WebClient.</summary>
public sealed class SharePointListingTests
{
    private static readonly SharePointAddress Address = SharePointAddress.Parse("https://sp.example.com/sites/Rabbit/Shared%20Documents/E1")!;
    private const string Unc = @"\\sp.example.com@SSL\DavWWWRoot\sites\Rabbit\Shared Documents\E1";

    private const string Multistatus =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <D:multistatus xmlns:D="DAV:">
          <D:response><D:href>https://sp.example.com/sites/Rabbit/Shared%20Documents/E1/</D:href>
            <D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop></D:propstat></D:response>
          <D:response><D:href>/sites/Rabbit/Shared%20Documents/E1/ACTUALS_PAF_01.xlsx</D:href>
            <D:propstat><D:prop><D:displayname>ACTUALS_PAF_01.xlsx</D:displayname><D:getcontentlength>1234</D:getcontentlength>
              <D:getlastmodified>Fri, 02 Oct 2026 06:15:00 GMT</D:getlastmodified><D:resourcetype/></D:prop></D:propstat></D:response>
          <D:response><D:href>/sites/Rabbit/Shared%20Documents/E1/Archiwum/</D:href>
            <D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop></D:propstat></D:response>
          <D:response><D:href>/sites/Rabbit/Shared%20Documents/E1/ACTUALS%20CES%2002.csv</D:href>
            <D:propstat><D:prop><D:getcontentlength>10</D:getcontentlength><D:getlastmodified>Thu, 01 Oct 2026 22:00:00 GMT</D:getlastmodified><D:resourcetype/></D:prop></D:propstat></D:response>
        </D:multistatus>
        """;

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    [Fact]
    public void Folder_listing_returns_files_with_size_and_date_and_subfolders()
    {
        var (files, folders) = SharePointListing.Parse(XDocument.Parse(Multistatus), new Uri(Address.FolderUrl), Unc);

        Assert.Equal(["ACTUALS CES 02.csv", "ACTUALS_PAF_01.xlsx"], files.Select(f => f.Name));
        var paf = files[1];
        Assert.Equal((1234L, new DateTime(2026, 10, 2, 6, 15, 0, DateTimeKind.Utc), DateTimeKind.Utc), (paf.Length, paf.LastWriteTimeUtc, paf.LastWriteTimeUtc.Kind));
        Assert.Equal(Path.Combine(Unc, "ACTUALS_PAF_01.xlsx"), paf.FullName);   // kopia przez WebDAV jako zapas
        Assert.Equal(["Archiwum"], folders);
    }

    [Fact]
    public void Listing_sends_propfind_with_gateway_session_cookies()
    {
        var handler = new Handler(_ => new HttpResponseMessage((HttpStatusCode)207)
        {
            Content = new StringContent(Multistatus, Encoding.UTF8, "application/xml"),
        });
        using var listing = new SharePointListing(handler, _ => "MRHSession=abc; LastMRH_Session=1");

        var (files, _) = listing.List(Address, Unc);

        Assert.Equal(2, files.Count);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(("PROPFIND", "1", "MRHSession=abc; LastMRH_Session=1"),
            (request.Method.Method, request.Headers.GetValues("Depth").Single(), request.Headers.GetValues("Cookie").Single()));
    }

    [Theory]
    [InlineData(HttpStatusCode.Found, "brak sesji bramy")]
    [InlineData(HttpStatusCode.Forbidden, "brak sesji bramy")]
    [InlineData(HttpStatusCode.OK, "strona logowania bramy")]
    public void Without_gateway_session_listing_fails_with_reason(HttpStatusCode status, string reason)
    {
        var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html") };
            if (status == HttpStatusCode.Found)
                response.Headers.Location = new Uri("https://sp.example.com/my.policy");
            return response;
        });
        using var listing = new SharePointListing(handler, _ => null);

        Assert.Contains(reason, Assert.Throws<IOException>(() => listing.List(Address, Unc)).Message);
    }
}
