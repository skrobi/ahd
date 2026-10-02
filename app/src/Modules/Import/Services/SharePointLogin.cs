using System.Net;
using System.Net.Http;
using PzlEv.Modules.Import.Models;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Logowanie jak Office – protokół MS-OFBA ([MS-OFBA] 2.2.1, 2.2.2): zapytanie „protocol discovery” z nagłówkiem
/// X-FORMS_BASED_AUTH_ACCEPTED: t (albo agentem klienta WebDAV); serwer / brama odpowiada 403 z adresem strony
/// logowania (X-FORMS_BASED_AUTH_REQUIRED), adresem powrotu (X-FORMS_BASED_AUTH_RETURN_URL) i rozmiarem okna.
/// Po zalogowaniu brama zapisuje trwałe ciasteczko, z którego korzysta też usługa WebClient (WebDAV).
/// </summary>
public static class SharePointLogin
{
    public const string AcceptedHeader = "X-FORMS_BASED_AUTH_ACCEPTED";
    public const string RequiredHeader = "X-FORMS_BASED_AUTH_REQUIRED";
    public const string ReturnUrlHeader = "X-FORMS_BASED_AUTH_RETURN_URL";
    public const string DialogSizeHeader = "X-FORMS_BASED_AUTH_DIALOG_SIZE";

    /// <summary>Warianty zapytania jak klienci Office i WebDAV – próbowane po kolei.</summary>
    public static readonly IReadOnlyList<(string Method, string? UserAgent, bool Header)> Variants =
    [
        ("OPTIONS", null, true),
        ("OPTIONS", "Microsoft Office Protocol Discovery", true),
        ("PROPFIND", "Microsoft-WebDAV-MiniRedir/10.0", false),
    ];

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
