using PzlEv.Modules.Import.Models;
using PzlEv.Modules.Import.Services;
using Serilog;

namespace PzlEv.Modules.Import.ViewModels;

/// <summary>
/// Logowanie do bramy SharePoint każdej aktywnej lokalizacji (jak Office – MS-OFBA; bez niego strona folderu
/// w oknie aplikacji). Bez lokalizacji SharePoint – nic do zrobienia. false – logowanie anulowane.
/// </summary>
public static class SharePointSignIn
{
    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    public static async Task<bool> Run(ImportService service, ISharePointLoginDialog dialog, Action<string> progress)
    {
        var addresses = service.Locations().Where(l => !l.IsManualFolder)
            .Select(l => SharePointAddress.Parse(l.ConfiguredPath))
            .OfType<SharePointAddress>()
            .DistinctBy(a => a.Origin, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var address in addresses)
        {
            progress($"Logowanie do SharePoint ({address.Host})…");
            var request = await Task.Run(() =>
            {
                using var login = new SharePointLogin();
                return login.Prepare(address);
            });
            if (!dialog.Login(request))
            {
                Logger.Information("Logowanie SharePoint {Host} anulowane", address.Host);
                progress("");
                return false;
            }
        }
        progress("");
        return true;
    }
}
