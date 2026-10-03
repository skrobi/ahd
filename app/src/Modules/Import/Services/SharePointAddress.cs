using System.Text.RegularExpressions;
using System.Web;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Folder SharePoint z linku skopiowanego z przeglądarki (adres folderu, widok AllItems.aspx?RootFolder=… / ?id=…)
/// albo ze ścieżki WebDAV \\host@SSL\DavWWWRoot\…: witryna (/sites/… albo /teams/…), folder (ścieżka względna
/// serwera, bez kodowania), adres https folderu i ścieżka WebDAV. Te same reguły co narzędzie w Pythonie.
/// </summary>
public sealed partial record SharePointAddress(string Scheme, string Host, string SitePath, string Folder)
{
    public string Origin => $"{Scheme}://{Host}";

    /// <summary>Adres witryny, np. https://host/sites/RabbitReporting.</summary>
    public string SiteUrl => Origin + Escape(SitePath);

    /// <summary>Adres folderu zakończony „/” (WebDAV PROPFIND).</summary>
    public string FolderUrl => Origin + Escape(Folder) + "/";

    /// <summary>Koduje segmenty ścieżki URL (spacja → %20), zachowując „/”.</summary>
    public static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public static SharePointAddress? Parse(string linkOrPath)
    {
        var text = linkOrPath.Trim();
        if (text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var unc = UncPattern().Match(text);
            if (!unc.Success)
                return null;
            var port = unc.Groups["port"].Success ? ":" + unc.Groups["port"].Value : "";
            return Create(unc.Groups["ssl"].Success ? "https" : "http", unc.Groups["host"].Value + port, unc.Groups["path"].Value.Replace('\\', '/'));
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        var query = HttpUtility.ParseQueryString(uri.Query);
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var folder = query["RootFolder"] ?? query["id"] ?? path.Split("/Forms/")[0];
        return Create(uri.Scheme, uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}", folder);
    }

    private static SharePointAddress Create(string scheme, string host, string folder)
    {
        folder = "/" + folder.Trim('/');
        var segments = folder.Split('/');
        var site = segments.Length > 2 && (segments[1].Equals("sites", StringComparison.OrdinalIgnoreCase) || segments[1].Equals("teams", StringComparison.OrdinalIgnoreCase))
            ? string.Join('/', segments[..3])
            : "";
        return new SharePointAddress(scheme, host, site, folder);
    }

    [GeneratedRegex(@"^\\\\(?<host>[^\\@]+)(?<ssl>@SSL)?(?:@(?<port>\d+))?\\DavWWWRoot(?<path>\\.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UncPattern();
}
