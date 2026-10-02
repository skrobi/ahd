using System.IO;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Win32;
using PzlEv.Modules.Import.Models;
using PzlEv.Modules.Import.ViewModels;
using Serilog;

namespace PzlEv.Modules.Import.Views;

/// <summary>
/// Okno logowania do SharePoint za bramą F5 – jak okno logowania Office (MS-OFBA). Kontrolka WebBrowser używa
/// silnika przeglądarki Windows (WinINet), który dzieli trwałe ciasteczka z usługą WebClient (WebDAV): po
/// zalogowaniu import czyta lokalizację bez eksportu do Excela. Okno zamyka się samo po osiągnięciu adresu powrotu.
/// </summary>
public partial class SharePointLoginWindow : Window
{
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    private readonly SharePointLoginRequest _request;

    public SharePointLoginWindow(SharePointLoginRequest request)
    {
        InitializeComponent();
        _request = request;
        Width = Math.Max(request.Width, 640);
        Height = Math.Max(request.Height, 480) + 60;
        Info.Text = request.IsOfba
            ? "Zaloguj się do SharePoint (brama F5) – okno zamknie się samo po zalogowaniu."
            : "Zaloguj się do SharePoint (brama F5). Gdy zobaczysz stronę biblioteki, okno zamknie się samo (albo kliknij „Gotowe”).";
        Browser.Navigated += OnNavigated;
        Loaded += (_, _) => Browser.Navigate(request.LoginUrl);
        Closed += (_, _) => Browser.Dispose();
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        Logger.Information("Logowanie SharePoint: {Url}", e.Uri?.GetLeftPart(UriPartial.Path));
        if (e.Uri is not null && _request.IsDone(e.Uri) && IsVisible)
            DialogResult = true;
    }

    private void OnDone(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>
    /// Kontrolka WebBrowser domyślnie działa w trybie IE7 – strona logowania bramy wymaga trybu IE11
    /// (FEATURE_BROWSER_EMULATION dla PZL-EV.exe, ustawienie użytkownika).
    /// </summary>
    internal static void UseModernBrowserMode()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION");
            key.SetValue(Path.GetFileName(Environment.ProcessPath ?? "PZL-EV.exe"), 11001, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Logger.Warning(ex, "Nie ustawiono trybu przeglądarki IE11 dla okna logowania");
        }
    }
}

/// <summary>Okno logowania do SharePoint otwierane z ekranu Import.</summary>
public sealed class SharePointLoginDialog : ISharePointLoginDialog
{
    public bool Login(SharePointLoginRequest request)
    {
        SharePointLoginWindow.UseModernBrowserMode();
        var window = new SharePointLoginWindow(request) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true;
    }
}
