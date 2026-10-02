namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Logowanie do SharePoint za bramą F5 w oknie aplikacji – jak Office (protokół MS-OFBA): strona logowania
/// i adres powrotu z odpowiedzi bramy. Bez MS-OFBA (ReturnUrl = null) okno otwiera stronę folderu, a logowanie jest
/// zakończone po załadowaniu strony witryny SharePoint (nie strony bramy).
/// </summary>
public sealed record SharePointLoginRequest(Uri LoginUrl, Uri? ReturnUrl, int Width, int Height, Uri SiteUrl)
{
    public bool IsOfba => ReturnUrl is not null;

    public bool IsDone(Uri current)
    {
        if (ReturnUrl is not null)
            return current.AbsoluteUri.StartsWith(ReturnUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        var site = Uri.UnescapeDataString(SiteUrl.AbsolutePath).TrimEnd('/');
        return current.Host.Equals(SiteUrl.Host, StringComparison.OrdinalIgnoreCase)
               && Uri.UnescapeDataString(current.AbsolutePath).StartsWith(site + "/", StringComparison.OrdinalIgnoreCase);
    }
}
