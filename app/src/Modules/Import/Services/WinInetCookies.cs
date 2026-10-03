using System.Runtime.InteropServices;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Ciasteczka przeglądarki Windows (WinINet) dla adresu – te same, które zapisało okno logowania SharePoint (kontrolka
/// WebBrowser) po przejściu bramy F5; także HttpOnly. Pobieranie pliku przez HTTPS wysyła je jak przeglądarka.
/// </summary>
internal static class WinInetCookies
{
    private const int HttpOnly = 0x00002000;   // INTERNET_COOKIE_HTTPONLY

    [DllImport("wininet.dll", EntryPoint = "InternetGetCookieExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetGetCookieEx(string url, string? cookieName, [Out] char[]? cookieData, ref int size, int flags, IntPtr reserved);

    /// <summary>Nagłówek Cookie dla adresu („a=1; b=2”); null – brak ciasteczek albo system inny niż Windows.</summary>
    public static string? Get(Uri url)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            var size = 0;
            if (!InternetGetCookieEx(url.AbsoluteUri, null, null, ref size, HttpOnly, IntPtr.Zero) || size <= 0)
                return null;
            var data = new char[size];
            return InternetGetCookieEx(url.AbsoluteUri, null, data, ref size, HttpOnly, IntPtr.Zero)
                ? new string(data).TrimEnd('\0')
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
