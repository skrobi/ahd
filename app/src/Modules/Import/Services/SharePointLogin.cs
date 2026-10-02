using System.Net;
using System.Net.Http;
using PzlEv.Modules.Import.Models;
using Serilog;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Logowanie do SharePoint za bramą F5 jak Office – protokół MS-OFBA ([MS-OFBA] 2.2.1, 2.2.2): zapytanie
/// „protocol discovery” z nagłówkiem X-FORMS_BASED_AUTH_ACCEPTED: t (albo agentem klienta WebDAV); brama odpowiada
/// 403 z adresem strony logowania (X-FORMS_BASED_AUTH_REQUIRED), adresem powrotu (X-FORMS_BASED_AUTH_RETURN_URL)
/// i rozmiarem okna. Po zalogowaniu w oknie aplikacji brama zapisuje trwałe ciasteczko, z którego korzysta też
/// usługa WebClient (WebDAV). Bez odpowiedzi MS-OFBA okno otwiera stronę folderu.
/// </summary>
public sealed class SharePointLogin : IDisposable
{
    public const string AcceptedHeader = "X-FORMS_BASED_AUTH_ACCEPTED";
    public const string RequiredHeader = "X-FORMS_BASED_AUTH_REQUIRED";
    public const string ReturnUrlHeader = "X-FORMS_BASED_AUTH_RETURN_URL";
    public const string DialogSizeHeader = "X-FORMS_BASED_AUTH_DIALOG_SIZE";

    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    /// <summary>Warianty zapytania jak klienci Office i WebDAV – próbowane po kolei.</summary>
    private static readonly (string Method, string? UserAgent, bool Header)[] Variants =
    [
        ("OPTIONS", null, true),
        ("OPTIONS", "Microsoft Office Protocol Discovery", true),
        ("PROPFIND", "Microsoft-WebDAV-MiniRedir/10.0", false),
    ];

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public SharePointLogin(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _client = new HttpClient(handler ?? new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false }, disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public void Dispose() => _client.Dispose();

    /// <summary>Dane okna logowania: z odpowiedzi MS-OFBA, a bez niej – strona folderu.</summary>
    public SharePointLoginRequest Prepare(SharePointAddress address, CancellationToken cancellation = default)
    {
        var target = new Uri(address.FolderUrl);
        foreach (var (method, agent, header) in Variants)
        {
            var variant = $"{method}" + (header ? $", {AcceptedHeader}: t" : "") + (agent is null ? "" : $", User-Agent: {agent}");
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), target);
                if (header)
                    request.Headers.TryAddWithoutValidation(AcceptedHeader, "t");
                if (agent is not null)
                    request.Headers.TryAddWithoutValidation("User-Agent", agent);
                if (method == "PROPFIND")
                    request.Headers.Add("Depth", "0");
                using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timer.CancelAfter(_timeout);
                using var response = _client.SendAsync(request, timer.Token).GetAwaiter().GetResult();
                if (ReadChallenge(response, target, address) is { } login)
                {
                    Logger.Information("Logowanie SharePoint {Host}: MS-OFBA ({Variant}) – strona logowania {Login}, powrót {Return}",
                        address.Host, variant, login.LoginUrl.GetLeftPart(UriPartial.Path), login.ReturnUrl!.GetLeftPart(UriPartial.Path));
                    return login;
                }
                Logger.Information("Logowanie SharePoint {Host}: {Variant} → HTTP {Status}", address.Host, variant, (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                Logger.Warning("Logowanie SharePoint {Host}: {Variant} – {Error}", address.Host, variant, ex.Message);
            }
        }
        Logger.Information("Logowanie SharePoint {Host}: brak odpowiedzi MS-OFBA – okno otworzy stronę folderu", address.Host);
        return Fallback(address);
    }

    /// <summary>Odpowiedź MS-OFBA (403 z adresem logowania) → dane okna logowania; inna odpowiedź → null.</summary>
    public static SharePointLoginRequest? ReadChallenge(HttpResponseMessage response, Uri requestUri, SharePointAddress address)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden || Header(response, RequiredHeader) is not { Length: > 0 } required
            || !Uri.TryCreate(requestUri, required, out var login))
            return null;
        var returnUrl = Header(response, ReturnUrlHeader) is { Length: > 0 } back && Uri.TryCreate(requestUri, back, out var parsed) ? parsed : login;
        var (width, height) = (660, 495);   // domyślny rozmiar okna wg [MS-OFBA] 2.2.2
        if (Header(response, DialogSizeHeader)?.Split('x') is [var w, var h] && int.TryParse(w, out var pw) && int.TryParse(h, out var ph) && pw > 0 && ph > 0)
            (width, height) = (pw, ph);
        return new SharePointLoginRequest(login, returnUrl, width, height, new Uri(address.SiteUrl + "/"));
    }

    /// <summary>Bez MS-OFBA: okno otwiera stronę folderu; zakończone po załadowaniu strony witryny.</summary>
    public static SharePointLoginRequest Fallback(SharePointAddress address) =>
        new(new Uri(address.FolderUrl), null, 1000, 760, new Uri(address.SiteUrl + "/"));

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;
}
