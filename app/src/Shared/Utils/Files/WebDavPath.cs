using System.Web;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Lokalizacje RABIT na SharePoint czytane przez WebDAV (usługa WebClient Windows, konto użytkownika):
/// biblioteka widoczna jako ścieżka UNC \\host@SSL\DavWWWRoot\sites\…\folder.
/// </summary>
public static class WebDavPath
{
    public const string SizeLimitHint =
        "Plik może przekraczać limit usługi WebClient (domyślnie ok. 50 MB) – administrator może zwiększyć FileSizeLimitInBytes " +
        @"w HKLM\SYSTEM\CurrentControlSet\Services\WebClient\Parameters; do tego czasu pobierz plik w przeglądarce do folderu Do_importu.";

    public const string AccessHint =
        "Dla SharePoint (WebDAV): usługa WebClient musi działać, a folder trzeba raz otworzyć w Eksploratorze Windows " +
        "(logowanie kontem użytkownika); ścieżka w postaci \\\\host@SSL\\DavWWWRoot\\sites\\…. SharePoint RABIT jest za bramą " +
        "logowania F5 (BIG-IP): WebDAV działa dopiero po zalogowaniu przez Office – w bibliotece „Eksport do Excela” albo " +
        "otwarcie pliku w Excelu; po wygaśnięciu sesji trzeba to powtórzyć.";

    /// <summary>
    /// Link do folderu SharePoint skopiowany z przeglądarki (adres folderu albo widok listy z RootFolder / id)
    /// → ścieżka WebDAV UNC. Ścieżka UNC i ścieżka lokalna są zwracane bez zmian.
    /// </summary>
    public static string ToUnc(string pathOrUrl)
    {
        var text = pathOrUrl.Trim();
        if (text.StartsWith(@"\\", StringComparison.Ordinal)
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return text;

        var query = HttpUtility.ParseQueryString(uri.Query);
        var folder = query["RootFolder"] ?? query["id"] ?? Uri.UnescapeDataString(uri.AbsolutePath).Split("/Forms/")[0];
        var host = uri.Authority + (uri.Scheme == Uri.UriSchemeHttps ? "@SSL" : "");
        return $@"\\{host}\DavWWWRoot" + ("/" + folder.Trim('/')).Replace('/', '\\');
    }

    public static bool IsWebDav(string path) =>
        path.Contains("DavWWWRoot", StringComparison.OrdinalIgnoreCase) || path.Contains("@SSL", StringComparison.OrdinalIgnoreCase);
}
