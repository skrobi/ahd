using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using PzlEv.Modules.Import.Models;
using Serilog;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Lista plików folderu SharePoint przez HTTPS (WebDAV PROPFIND, Depth 1) z procesu aplikacji – konto Windows
/// i ciasteczka sesji bramy F5 z okna logowania (WinINet), tak jak pobieranie pliku. Nie zależy od usługi WebClient,
/// która widzi tylko trwałe ciasteczka (sesyjne ciasteczko bramy zapisuje trwale dopiero Office). Brak sesji bramy
/// (przekierowanie, strona logowania, 401/403) – wyjątek z opisem; wtedy import czyta ścieżkę UNC.
/// </summary>
public sealed class SharePointListing : IDisposable
{
    private const string Body =
        """<?xml version="1.0" encoding="utf-8"?><D:propfind xmlns:D="DAV:"><D:prop><D:displayname/><D:getcontentlength/><D:getlastmodified/><D:resourcetype/></D:prop></D:propfind>""";

    private static readonly XNamespace Dav = "DAV:";
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    private readonly HttpClient _client;
    private readonly Func<Uri, string?> _cookies;

    /// <param name="handler">Obsługa HTTP (testy); domyślnie konto Windows, bez przekierowań, ciasteczka podawane ręcznie.</param>
    /// <param name="cookies">Nagłówek Cookie dla adresu (testy); domyślnie ciasteczka WinINet.</param>
    public SharePointListing(HttpMessageHandler? handler = null, Func<Uri, string?>? cookies = null)
    {
        _client = new HttpClient(handler ?? new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false, UseCookies = false },
            disposeHandler: handler is null) { Timeout = TimeSpan.FromSeconds(60) };
        _cookies = cookies ?? WinInetCookies.Get;
    }

    public void Dispose() => _client.Dispose();

    /// <summary>Pliki i podfoldery folderu; uncFolder – ścieżka UNC folderu (FullName plików – kopia przez WebDAV).</summary>
    public (List<SourceFile> Files, List<string> Folders) List(SharePointAddress address, string uncFolder)
    {
        var url = new Uri(address.FolderUrl);
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url)
        {
            Content = new StringContent(Body, Encoding.UTF8, "text/xml"),
        };
        request.Headers.Add("Depth", "1");
        request.Headers.TryAddWithoutValidation("User-Agent", "Microsoft-WebDAV-MiniRedir/10.0");
        var cookies = _cookies(url);
        if (!string.IsNullOrEmpty(cookies))
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
        using var response = _client.Send(request);
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        Logger.Information("Lista SharePoint {Url} przez HTTPS: HTTP {Status}, {Type}, ciasteczka: {Cookies}", url.GetLeftPart(UriPartial.Path),
            (int)response.StatusCode, type, string.IsNullOrEmpty(cookies) ? "brak" : $"{cookies.Split(';').Length}");
        if ((int)response.StatusCode != 207 || type.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new IOException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"HTTP {(int)response.StatusCode} – brak sesji bramy SharePoint (zaloguj się ponownie)",
                HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect =>
                    $"HTTP {(int)response.StatusCode} → {response.Headers.Location?.GetLeftPart(UriPartial.Path)} – brak sesji bramy SharePoint (zaloguj się ponownie)",
                _ => type.Contains("html", StringComparison.OrdinalIgnoreCase) ? "strona logowania bramy zamiast listy plików" : $"HTTP {(int)response.StatusCode}",
            });
        using var stream = response.Content.ReadAsStream();
        return Parse(XDocument.Load(stream), url, uncFolder);
    }

    /// <summary>Odpowiedź PROPFIND (multistatus) → pliki i podfoldery; sam folder pominięty.</summary>
    public static (List<SourceFile> Files, List<string> Folders) Parse(XDocument document, Uri folder, string uncFolder)
    {
        var files = new List<SourceFile>();
        var folders = new List<string>();
        var self = Uri.UnescapeDataString(folder.AbsolutePath).TrimEnd('/');
        foreach (var item in document.Descendants(Dav + "response"))
        {
            var href = item.Element(Dav + "href")?.Value.Trim() ?? "";
            var path = Uri.UnescapeDataString(Uri.TryCreate(folder, href, out var absolute) ? absolute.AbsolutePath : href).TrimEnd('/');
            if (path.Length == 0 || string.Equals(path, self, StringComparison.OrdinalIgnoreCase))
                continue;
            var name = path[(path.LastIndexOf('/') + 1)..];
            var prop = item.Descendants(Dav + "prop").FirstOrDefault();
            if (prop?.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null)
            {
                folders.Add(name);
                continue;
            }
            var length = long.TryParse(prop?.Element(Dav + "getcontentlength")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0;
            var modified = DateTime.TryParse(prop?.Element(Dav + "getlastmodified")?.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when) ? when : DateTime.MinValue;
            files.Add(new SourceFile(name, length, DateTime.SpecifyKind(modified, DateTimeKind.Utc), Path.Combine(uncFolder, name)));
        }
        return (files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(), folders.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
