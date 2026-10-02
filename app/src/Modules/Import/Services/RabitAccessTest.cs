using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PzlEv.Modules.Import.Models;
using Serilog;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Test dostępu do folderu RABIT na SharePoint – które drogi działają z tego komputera (odpowiednik
/// narzedzia/test_dostepu_rabit.py z narzędzia w Pythonie, metody A–D) oraz drogi dostępne w .NET (E–H).
/// Nie importuje i nie zapisuje zawartości plików – tylko status metody, szczegóły odpowiedzi i nazwy plików.
/// <list type="bullet">
/// <item>A – owssvr.dll?XMLDATA=1 (to samo co „Eksport do Excela”), HTTP, konto Windows</item>
/// <item>B – provider OLEDB Microsoft.Office.List.OLEDB.2.0 przez ADODB (jak połączenie w Excelu)</item>
/// <item>C – WebDAV \\host@SSL\DavWWWRoot\… (jak „Otwórz w Eksploratorze”, usługa WebClient)</item>
/// <item>D – bezpośrednie pobranie jednego pliku przez HTTP, konto Windows</item>
/// <item>E – WebDAV przez HTTPS z aplikacji (PROPFIND) – bez usługi WebClient</item>
/// <item>F – REST API SharePoint (_api/web/GetFolderByServerRelativeUrl)</item>
/// <item>G – usługa SOAP Lists.asmx (GetListItems)</item>
/// <item>H – połączenie sieciowe WNetAddConnection2 (jak „Mapuj dysk sieciowy” / net use) i lista UNC</item>
/// <item>I – MS-OFBA: czy brama / SharePoint pozwala zalogować się jak Office (okno logowania w aplikacji)</item>
/// </list>
/// HTTP: konto Windows (Kerberos/NTLM) jak przeglądarka; nagłówek X-FORMS_BASED_AUTH_ACCEPTED: f prosi SharePoint
/// z kilkoma metodami logowania o logowanie Windows zamiast formularza; przekierowania są opisywane w szczegółach.
/// </summary>
public sealed partial class RabitAccessTest : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    private const string PropfindBody =
        """<?xml version="1.0" encoding="utf-8"?><D:propfind xmlns:D="DAV:"><D:prop><D:displayname/><D:getcontentlength/><D:getlastmodified/><D:resourcetype/></D:prop></D:propfind>""";

    private static readonly string[] WebDavHints =
    [
        "Wskazówki (WebDAV przez usługę WebClient):",
        "– usługa WebClient musi działać; otwarcie ścieżki w Eksploratorze ją uruchamia;",
        "– adres z kropkami (np. host.firma.com) to dla Windows strefa Internet: WebClient nie wysyła logowania Windows, dopóki adres " +
        "nie jest w strefie Intranet lokalny (Opcje internetowe → Zabezpieczenia → Intranet lokalny → Witryny → Zaawansowane) " +
        @"albo w AuthForwardServerList (administrator: HKLM\SYSTEM\CurrentControlSet\Services\WebClient\Parameters, potem restart usługi);",
        "– za serwerem proxy WebClient nie wysyła logowania – adres SharePoint musi być na liście wyjątków proxy;",
        "– przeglądarka ma własne zasady logowania, dlatego może działać, gdy WebDAV nie działa.",
    ];

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public RabitAccessTest(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _client = new HttpClient(handler ?? CreateHandler(), disposeHandler: handler is null) { Timeout = Timeout.InfiniteTimeSpan };
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    public void Dispose() => _client.Dispose();

    private static HttpClientHandler CreateHandler() => new()
    {
        UseDefaultCredentials = true,                                    // konto Windows (Kerberos/NTLM) – jak przeglądarka
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        AllowAutoRedirect = false,                                       // przekierowania opisywane w szczegółach
    };

    public AccessTestResult Run(AccessTestInput input, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        var address = SharePointAddress.Parse(input.FolderLink)
            ?? throw new ArgumentException(@"To nie jest link do folderu SharePoint (https://…) ani ścieżka \\host@SSL\DavWWWRoot\…");
        var environment = EnvironmentInfo(address);
        var results = new List<AccessMethodResult>();

        void Step(string code, Func<AccessMethodResult> method)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(code);
            var result = method();
            Logger.Information("Test dostępu {Code}: {Status} – {Details}", code, result.Status, string.Join(" | ", result.Details));
            results.Add(result);
        }

        Step("A", () => Owssvr(address, input, cancellation));
        Step("B", () => OleDb(address, input));
        Step("C", () => Unc(address));
        Step("E", () => Propfind(address, cancellation));
        Step("F", () => Rest(address, cancellation));
        Step("G", () => Soap(address, input, cancellation));
        Step("H", () => Map(address));
        Step("I", () => Ofba(address, cancellation).Result);
        var fileLink = input.FileLink.Trim().Length > 0
            ? input.FileLink.Trim()
            : results.Where(r => r.Works).SelectMany(r => r.Files).FirstOrDefault(f => f.Url is not null)?.Url;
        Step("D", () => Download(fileLink, cancellation));

        var methods = results.OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
        var conclusion = Conclusion(methods);
        var report = Report(environment, methods, conclusion);
        Logger.Information("Test dostępu RABIT – raport:{NewLine}{Report}", Environment.NewLine, report);
        return new AccessTestResult(environment, methods, conclusion, report);
    }

    public static string Conclusion(IReadOnlyList<AccessMethodResult> results)
    {
        bool Ok(string code) => results.Any(r => r.Code == code && r.Works);
        var listing = new[] { "A", "B", "F", "G" }.Any(Ok);
        if (Ok("C") || Ok("H"))
            return @"WNIOSEK: WebDAV (\\…\DavWWWRoot) działa – import czyta lokalizację jak dotąd.";
        if (Ok("I"))
            return "WNIOSEK: WebDAV wymaga zalogowania do bramy – brama pozwala zalogować się jak Office (I): na ekranie Import użyj „Zaloguj do SharePoint”.";
        if (Ok("E"))
            return "WNIOSEK: działa WebDAV przez HTTPS z aplikacji (E), bez usługi WebClient – przekaż raport, przełączę import lokalizacji SharePoint na tę metodę.";
        if (listing && Ok("D"))
            return "WNIOSEK: działa lista plików i pobieranie przez HTTP – przekaż raport, dołączę tę metodę do importu.";
        if (listing)
            return "WNIOSEK: działa lista plików, ale nie pobieranie – aplikacja może pokazać, co nowego pobrać ręcznie.";
        if (Ok("D"))
            return "WNIOSEK: działa pobieranie znanego pliku – można pobierać pliki o stałych nazwach / z listy linków.";
        return @"WNIOSEK: żadna metoda automatyczna nie działa – zostaje ręczne pobieranie do 00_Global\RABIT\Do_importu (wskazówki w raporcie).";
    }

    private static List<string> EnvironmentInfo(SharePointAddress address)
    {
        var lines = new List<string>
        {
            $"Witryna: {address.SiteUrl}",
            $"Folder: {address.Folder}",
            $"Adres folderu: {address.FolderUrl}",
            $"Ścieżka WebDAV: {address.Unc}",
            $"Konto: {Environment.UserDomainName}\\{Environment.UserName} na {Environment.MachineName}",
            $"System: {RuntimeInformation.OSDescription}, proces {(Environment.Is64BitProcess ? 64 : 32)}-bit",
            $"Proxy systemowe dla adresu: {Proxy(address)}",
        };
        if (OperatingSystem.IsWindows())
        {
            lines.Add($"Strefa zabezpieczeń adresu: {WindowsAccess.Zone(address.FolderUrl)}");
            lines.AddRange(WindowsAccess.WebClientInfo());
        }
        return lines;
    }

    private static string Proxy(SharePointAddress address)
    {
        try
        {
            var target = new Uri(address.FolderUrl);
            var proxy = HttpClient.DefaultProxy.GetProxy(target);
            return proxy is null || proxy == target ? "brak (połączenie bezpośrednie)" : proxy.GetLeftPart(UriPartial.Authority);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"nieznane ({ex.Message})";
        }
    }

    // ---- A: owssvr.dll -------------------------------------------------------------------------------------

    private AccessMethodResult Owssvr(SharePointAddress address, AccessTestInput input, CancellationToken cancellation)
    {
        const string name = "owssvr.dll (Eksport do Excela) – lista plików przez HTTP, konto Windows";
        if (input.ListId.Trim().Length == 0)
            return Skipped("A", name, "Podaj ID listy (z połączenia Excela albo pliku .iqy)");
        var url = $"{address.SiteUrl}/_vti_bin/owssvr.dll?XMLDATA=1&List={Uri.EscapeDataString(input.ListId.Trim())}" +
                  (input.ViewId.Trim().Length > 0 ? $"&View={Uri.EscapeDataString(input.ViewId.Trim())}" : "") +
                  $"&RowLimit=0&RootFolder={Uri.EscapeDataString(address.Folder)}";
        return Http("A", name, HttpMethod.Get, url, null, cancellation, (response, body, details) =>
        {
            if (response.StatusCode != HttpStatusCode.OK || !IsXml(response))
                return null;
            var files = ParseRows(body, address);
            details.Add($"Plików na liście: {files.Count}");
            return files;
        });
    }

    /// <summary>Wiersze listy (z:row) z owssvr.dll albo Lists.asmx – tylko pliki, bez folderów.</summary>
    public static List<AccessFile> ParseRows(string xml, SharePointAddress address)
    {
        static string Lookup(string value) => value.Contains(";#", StringComparison.Ordinal) ? value.Split(";#", 2)[1] : value;

        var files = new List<AccessFile>();
        foreach (var row in XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "row"))
        {
            string Attr(string attribute) => (string?)row.Attribute(attribute) ?? "";
            if (Lookup(Attr("ows_FSObjType")) == "1")
                continue;
            var fileRef = Lookup(Attr("ows_FileRef"));
            var name = Attr("ows_LinkFilename") is { Length: > 0 } link ? link
                : Lookup(Attr("ows_FileLeafRef")) is { Length: > 0 } leaf ? leaf
                : fileRef.Split('/')[^1];
            var url = Attr("ows_EncodedAbsUrl") is { Length: > 0 } absolute ? absolute
                : fileRef.Length > 0 ? address.FileUrl(fileRef) : null;
            files.Add(new AccessFile(name, url));
        }
        return files;
    }

    // ---- B: OLEDB ------------------------------------------------------------------------------------------

    private AccessMethodResult OleDb(SharePointAddress address, AccessTestInput input)
    {
        const string name = "Provider OLEDB Microsoft.Office.List.OLEDB.2.0 (ADODB – jak połączenie w Excelu)";
        var details = new List<string> { $"Proces {(Environment.Is64BitProcess ? 64 : 32)}-bit – provider musi mieć bitowość pakietu Office" };
        if (!OperatingSystem.IsWindows())
            return new AccessMethodResult("B", name, AccessStatus.Skipped, [.. details, "Tylko Windows"], []);
        if (input.ListId.Trim().Length == 0)
            return new AccessMethodResult("B", name, AccessStatus.Skipped, [.. details, "Podaj ID listy"], []);

        var listXml = $"<LIST><VIEWGUID>{input.ViewId.Trim()}</VIEWGUID><LISTNAME>{input.ListId.Trim()}</LISTNAME>" +
                      $"<LISTWEB>{address.SiteUrl}/_vti_bin</LISTWEB><LISTSUBWEB></LISTSUBWEB>" +
                      $"<ROOTFOLDER>{SharePointAddress.Escape(address.Folder)}</ROOTFOLDER></LIST>";
        var attempts = new List<string>();
        var (result, error) = WindowsAccess.Run(() => WindowsAccess.ListOleDb(listXml, attempts), _timeout, sta: true);
        if (error is not null)
        {
            details.Add(error);
            if (error.Contains("provider", StringComparison.OrdinalIgnoreCase) || error.Contains("dostawc", StringComparison.OrdinalIgnoreCase))
                details.Add("Provider niezarejestrowany dla tej bitowości – sprawdź platformę Office w środowisku wyżej.");
            return new AccessMethodResult("B", name, AccessStatus.Fails, details, []);
        }
        details.AddRange([
            $"Połączenie: {result.Connection}",
            $"CommandType: {result.CommandType}",
            $"Kolumny: {string.Join(", ", result.Fields)}",
            $"Wierszy: {result.Names.Count}",
            "Uwaga: provider zwraca listę plików (metadane), nie ich zawartość.",
        ]);
        return new AccessMethodResult("B", name, AccessStatus.Works, details, result.Names.Select(n => new AccessFile(n, null)).ToList());
    }

    // ---- C, H: WebDAV przez usługę WebClient ---------------------------------------------------------------

    private AccessMethodResult Unc(SharePointAddress address) =>
        WebClientFolder("C", @"WebDAV – ścieżka \\host@SSL\DavWWWRoot\… (jak „Otwórz w Eksploratorze”, usługa WebClient)",
            address, () => WindowsAccess.ListFolder(address.Unc));

    private AccessMethodResult Map(SharePointAddress address) =>
        WebClientFolder("H", "Połączenie sieciowe WNetAddConnection2 (jak „Mapuj dysk sieciowy” / net use), konto Windows",
            address, () => WindowsAccess.MapAndList(address.Unc));

    private AccessMethodResult WebClientFolder(string code, string name, SharePointAddress address, Func<(List<string> Files, List<string> Folders)> list)
    {
        var details = new List<string> { $"Ścieżka: {address.Unc}" };
        if (!OperatingSystem.IsWindows())
            return new AccessMethodResult(code, name, AccessStatus.Skipped, [.. details, "Tylko Windows"], []);
        var (listing, error) = WindowsAccess.Run(list, _timeout, sta: false);
        if (error is not null)
            return new AccessMethodResult(code, name, AccessStatus.Fails, [.. details, error, .. WebDavHints], []);
        details.Add($"Plików: {listing.Files.Count}" + (listing.Folders.Count > 0 ? $", podfoldery: {string.Join(", ", listing.Folders.Take(10))}" : ""));
        return new AccessMethodResult(code, name, AccessStatus.Works, details,
            listing.Files.Select(n => new AccessFile(n, address.FileUrl(address.Folder + "/" + n))).ToList());
    }

    // ---- E: WebDAV przez HTTPS (PROPFIND) ------------------------------------------------------------------

    private AccessMethodResult Propfind(SharePointAddress address, CancellationToken cancellation) =>
        Http("E", "WebDAV przez HTTPS z aplikacji (PROPFIND), konto Windows – bez usługi WebClient",
            new HttpMethod("PROPFIND"), address.FolderUrl,
            request =>
            {
                request.Headers.Add("Depth", "1");
                request.Content = new StringContent(PropfindBody, Encoding.UTF8, "text/xml");
            },
            cancellation,
            (response, body, details) =>
            {
                if ((int)response.StatusCode != 207 || !IsXml(response))
                    return null;
                var (files, folders) = ParsePropfind(body, new Uri(address.FolderUrl));
                details.Add($"Plików: {files.Count}" + (folders.Count > 0 ? $", podfoldery: {string.Join(", ", folders.Take(10))}" : ""));
                return files;
            });

    /// <summary>Odpowiedź WebDAV 207 Multi-Status: pliki (z adresem) i podfoldery; sam folder pomijany.</summary>
    public static (List<AccessFile> Files, List<string> Folders) ParsePropfind(string xml, Uri folder)
    {
        XNamespace dav = "DAV:";
        var self = Uri.UnescapeDataString(folder.AbsolutePath).TrimEnd('/');
        var files = new List<AccessFile>();
        var folders = new List<string>();
        foreach (var response in XDocument.Parse(xml).Descendants(dav + "response"))
        {
            if ((string?)response.Element(dav + "href") is not { Length: > 0 } href)
                continue;
            var target = new Uri(folder, href);
            var path = Uri.UnescapeDataString(target.AbsolutePath).TrimEnd('/');
            if (string.Equals(path, self, StringComparison.OrdinalIgnoreCase))
                continue;
            var name = path[(path.LastIndexOf('/') + 1)..];
            if (response.Descendants(dav + "collection").Any())
                folders.Add(name);
            else
                files.Add(new AccessFile(name, target.AbsoluteUri));
        }
        return (files, folders);
    }

    // ---- F: REST API ---------------------------------------------------------------------------------------

    private AccessMethodResult Rest(SharePointAddress address, CancellationToken cancellation) =>
        Http("F", "REST API SharePoint (_api/web/GetFolderByServerRelativeUrl), konto Windows", HttpMethod.Get,
            $"{address.SiteUrl}/_api/web/GetFolderByServerRelativeUrl('{SharePointAddress.Escape(address.Folder.Replace("'", "''"))}')/Files",
            request => request.Headers.Accept.ParseAdd("application/json;odata=verbose"),
            cancellation,
            (response, body, details) =>
            {
                if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
                    return null;
                var files = ParseRestFiles(body, address);
                details.Add($"Plików: {files.Count}");
                return files;
            });

    /// <summary>Lista plików REST: odata=verbose {"d":{"results":[…]}} albo {"value":[…]}.</summary>
    public static List<AccessFile> ParseRestFiles(string json, SharePointAddress address)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.TryGetProperty("d", out var d) ? d : document.RootElement;
        var items = root.TryGetProperty("results", out var results) ? results : root.TryGetProperty("value", out var value) ? value : default;
        if (items.ValueKind != JsonValueKind.Array)
            return [];
        return items.EnumerateArray()
            .Select(f => new AccessFile(
                f.TryGetProperty("Name", out var n) ? n.GetString() ?? "?" : "?",
                f.TryGetProperty("ServerRelativeUrl", out var u) && u.GetString() is { Length: > 0 } path ? address.FileUrl(path) : null))
            .ToList();
    }

    // ---- G: SOAP Lists.asmx --------------------------------------------------------------------------------

    private AccessMethodResult Soap(SharePointAddress address, AccessTestInput input, CancellationToken cancellation)
    {
        const string name = "Usługa SOAP Lists.asmx (GetListItems), konto Windows";
        if (input.ListId.Trim().Length == 0)
            return Skipped("G", name, "Podaj ID listy");
        var envelope =
            $"""<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><GetListItems xmlns="http://schemas.microsoft.com/sharepoint/soap/"><listName>{SecurityElement.Escape(input.ListId.Trim())}</listName><rowLimit>5000</rowLimit><queryOptions><QueryOptions><Folder>{SecurityElement.Escape(address.FolderInSite)}</Folder></QueryOptions></queryOptions></GetListItems></soap:Body></soap:Envelope>""";
        return Http("G", name, HttpMethod.Post, $"{address.SiteUrl}/_vti_bin/Lists.asmx",
            request =>
            {
                request.Headers.Add("SOAPAction", "\"http://schemas.microsoft.com/sharepoint/soap/GetListItems\"");
                request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            },
            cancellation,
            (response, body, details) =>
            {
                if (response.StatusCode != HttpStatusCode.OK || !IsXml(response))
                    return null;
                var files = ParseRows(body, address);
                details.Add($"Plików na liście: {files.Count}");
                return files;
            });
    }

    // ---- I: MS-OFBA ----------------------------------------------------------------------------------------

    /// <summary>
    /// Czy brama / SharePoint odpowiada jak dla Office (403 z adresem strony logowania) – warianty zapytania po kolei;
    /// zwraca dane okna logowania, gdy któryś zadziałał.
    /// </summary>
    public (SharePointLoginRequest? Request, AccessMethodResult Result) Ofba(SharePointAddress address, CancellationToken cancellation)
    {
        const string name = "MS-OFBA – logowanie jak Office (strona logowania bramy w oknie aplikacji)";
        var details = new List<string>();
        var target = new Uri(address.FolderUrl);
        foreach (var (method, agent, header) in SharePointLogin.Variants)
        {
            details.Add($"Wariant: {method}" + (header ? $", {SharePointLogin.AcceptedHeader}: t" : "") + (agent is null ? "" : $", User-Agent: {agent}"));
            try
            {
                using var response = Send(new HttpMethod(method), address.FolderUrl, request =>
                {
                    request.Headers.Remove(SharePointLogin.AcceptedHeader);
                    if (header)
                        request.Headers.TryAddWithoutValidation(SharePointLogin.AcceptedHeader, "t");
                    if (agent is not null)
                        request.Headers.TryAddWithoutValidation("User-Agent", agent);
                    if (method == "PROPFIND")
                        request.Headers.Add("Depth", "0");
                }, details, _timeout, cancellation, maxRedirects: 0);
                if (SharePointLogin.ReadChallenge(response, target, address) is { } login)
                {
                    details.Add($"Brama obsługuje MS-OFBA: strona logowania {Safe(login.LoginUrl)}, powrót {Safe(login.ReturnUrl!)}, okno {login.Width}x{login.Height}");
                    return (login, new AccessMethodResult("I", name, AccessStatus.Works, details, []));
                }
                if (response.Headers.Location is { } location)
                    details.Add($"Przekierowanie: {(location.IsAbsoluteUri ? Safe(location) : location.OriginalString.Split('?')[0])}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && !(ex is OperationCanceledException && cancellation.IsCancellationRequested))
            {
                details.Add(WindowsAccess.Describe(ex));
            }
        }
        details.Add("Brak odpowiedzi MS-OFBA (403 z X-FORMS_BASED_AUTH_REQUIRED) – „Zaloguj do SharePoint” otworzy stronę folderu w oknie aplikacji.");
        return (null, new AccessMethodResult("I", name, AccessStatus.Fails, details, []));
    }

    // ---- D: pobranie jednego pliku -------------------------------------------------------------------------

    private AccessMethodResult Download(string? fileLink, CancellationToken cancellation)
    {
        const string name = "Bezpośrednie pobranie jednego pliku (HTTP, konto Windows)";
        if (string.IsNullOrWhiteSpace(fileLink))
            return Skipped("D", name, "Podaj link do jednego pliku (w bibliotece przy pliku „…” → Kopiuj link) – albo zadziała sam, gdy działa lista plików A, E, F lub G");
        var details = new List<string> { $"Plik: {Safe(new Uri(fileLink))}" };
        try
        {
            using var response = Send(HttpMethod.Get, fileLink, null, details, TimeSpan.FromMinutes(5), cancellation);
            var content = response.Content.ReadAsByteArrayAsync(cancellation).GetAwaiter().GetResult();
            var html = IsHtml(response);
            details.AddRange(Describe(response, html ? Encoding.UTF8.GetString(content) : ""));
            if (response.StatusCode == HttpStatusCode.OK && !html)
            {
                details.Add($"Pobrano {content.Length:N0} bajtów (treść nie jest zapisywana)");
                return new AccessMethodResult("D", name, AccessStatus.Works, details, []);
            }
            details.AddRange(Hint(response));
            return new AccessMethodResult("D", name, AccessStatus.Fails, details, []);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !(ex is OperationCanceledException && cancellation.IsCancellationRequested))
        {
            details.Add(WindowsAccess.Describe(ex));
            return new AccessMethodResult("D", name, AccessStatus.Fails, details, []);
        }
    }

    // ---- HTTP ----------------------------------------------------------------------------------------------

    private AccessMethodResult Http(string code, string name, HttpMethod method, string url, Action<HttpRequestMessage>? setup,
        CancellationToken cancellation, Func<HttpResponseMessage, string, List<string>, List<AccessFile>?> evaluate)
    {
        var details = new List<string>();
        try
        {
            using var response = Send(method, url, setup, details, _timeout, cancellation);
            var body = response.Content.ReadAsStringAsync(cancellation).GetAwaiter().GetResult();
            details.AddRange(Describe(response, body));
            var files = evaluate(response, body, details);
            if (files is not null)
                return new AccessMethodResult(code, name, AccessStatus.Works, details, files);
            details.AddRange(Hint(response));
            return new AccessMethodResult(code, name, AccessStatus.Fails, details, []);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !(ex is OperationCanceledException && cancellation.IsCancellationRequested))
        {
            details.Add(ex is XmlException or JsonException ? $"Nieoczekiwany format odpowiedzi: {ex.Message}" : WindowsAccess.Describe(ex));
            return new AccessMethodResult(code, name, AccessStatus.Fails, details, []);
        }
    }

    /// <summary>Żądanie z opisem każdego kroku (także przekierowań, maks. 5) w szczegółach metody.</summary>
    private HttpResponseMessage Send(HttpMethod method, string url, Action<HttpRequestMessage>? setup, List<string> trace, TimeSpan timeout,
        CancellationToken cancellation, int maxRedirects = 5)
    {
        var uri = new Uri(url);
        var current = method;
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(current, uri);
            request.Headers.TryAddWithoutValidation("X-FORMS_BASED_AUTH_ACCEPTED", "f");
            if (current == method)
                setup?.Invoke(request);
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timer.CancelAfter(timeout);
            HttpResponseMessage response;
            try
            {
                response = _client.SendAsync(request, timer.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new TimeoutException($"brak odpowiedzi serwera w {timeout.TotalSeconds:0} s ({current} {Safe(uri)})");
            }
            trace.Add($"HTTP {(int)response.StatusCode} {current} {Safe(uri)}");
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location && hop < maxRedirects)
            {
                // 301/302/303 – dalej GET (jak przeglądarka, np. strona logowania); 307/308 – ta sama metoda.
                if ((int)response.StatusCode is 301 or 302 or 303)
                    current = HttpMethod.Get;
                uri = new Uri(uri, location);
                response.Dispose();
                continue;
            }
            return response;
        }
    }

    /// <summary>Opis odpowiedzi – bez treści danych: typ, metody logowania, wersja SharePoint, tytuł strony HTML.</summary>
    private static IEnumerable<string> Describe(HttpResponseMessage response, string body)
    {
        yield return $"Typ odpowiedzi: {response.Content.Headers.ContentType?.ToString() ?? "brak"}";
        if (response.Headers.WwwAuthenticate.Count > 0)
            yield return $"WWW-Authenticate: {string.Join(", ", response.Headers.WwwAuthenticate.Select(a => a.Scheme))}";
        foreach (var header in new[] { "MicrosoftSharePointTeamServices", "Server", "X-MS-InvokeApp", "SPRequestGuid" })
        {
            if (response.Headers.TryGetValues(header, out var values))
                yield return $"{header}: {string.Join(", ", values)}";
        }
        if (response.Headers.Location is { } location)
            yield return $"Przekierowanie: {(location.IsAbsoluteUri ? Safe(location) : location.OriginalString.Split('?')[0])}";
        if (IsHtml(response) && HtmlTitle().Match(body.Length > 20_000 ? body[..20_000] : body) is { Success: true } match)
        {
            var title = string.Join(' ', match.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            yield return $"Tytuł strony: {title[..Math.Min(120, title.Length)]}";
        }
    }

    private static IEnumerable<string> Hint(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var hint = status switch
        {
            401 => "Serwer nie przyjął logowania kontem Windows (metody logowania serwera: WWW-Authenticate wyżej).",
            403 => "Odmowa (HTTP 403) – brak uprawnień albo ta droga jest zablokowana na serwerze.",
            404 => "Nie znaleziono (HTTP 404) – sprawdź link do folderu albo ID listy.",
            405 or 501 => "Metoda wyłączona na serwerze.",
            >= 300 and < 400 => "Przekierowanie – SharePoint kieruje na stronę logowania (np. ADFS) albo pod inny adres.",
            _ when IsHtml(response) => "Serwer zwrócił stronę HTML zamiast danych (np. strona logowania).",
            _ => null,
        };
        return hint is null ? [] : [hint];
    }

    private static AccessMethodResult Skipped(string code, string name, string reason) => new(code, name, AccessStatus.Skipped, [reason], []);

    private static bool IsXml(HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsHtml(HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Adres bez parametrów zapytania (mogą zawierać tokeny logowania).</summary>
    private static string Safe(Uri uri) => uri.GetLeftPart(UriPartial.Path);

    private static string Report(IReadOnlyList<string> environment, IReadOnlyList<AccessMethodResult> methods, string conclusion)
    {
        var report = new StringBuilder();
        report.AppendLine($"Test dostępu do RABIT – {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        foreach (var line in environment)
            report.AppendLine(line);
        foreach (var method in methods)
        {
            report.AppendLine().AppendLine(method.Title);
            foreach (var line in method.Text.Split(Environment.NewLine))
                report.AppendLine("    " + line);
        }
        report.AppendLine().AppendLine(conclusion);
        report.AppendLine("Raport bez zawartości plików – przejrzyj nazwy plików przed wysłaniem.");
        return report.ToString();
    }

    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlTitle();
}
