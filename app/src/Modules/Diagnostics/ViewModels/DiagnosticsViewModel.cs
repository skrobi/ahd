using PzlEv.Modules.Diagnostics.Models;
using PzlEv.Modules.Diagnostics.Services;
using PzlEv.Shared.Models;
using Serilog;

namespace PzlEv.Modules.Diagnostics.ViewModels;

/// <summary>Diagnostyka środowiska – wynik testu stosu: runtime, ścieżka uruchomienia, konto, pakiety.</summary>
public sealed class DiagnosticsViewModel
{
    private static readonly ILogger Logger = Log.ForContext("Module", ModuleKeys.Diagnostics);

    public DiagnosticsViewModel()
    {
        try
        {
            Packages = PackageDiagnostics.Collect();
            foreach (var p in Packages)
                Logger.Information("Pakiet {Name} {Version} ({Location})", p.Name, p.Version, p.Location);
        }
        catch (Exception ex)
        {
            // Brak któregoś pakietu w bundlu = wynik testu negatywny – pokazujemy, nie wywracamy aplikacji.
            Logger.Error(ex, "Nie udało się wczytać pakietów");
            Packages = [new PackageInfo("BŁĄD", ex.GetType().Name, ex.Message)];
        }
        Logger.Information("Runtime: {Runtime}; OS: {Os}; Exe: {Exe}", Runtime, Os, ExePath);
    }

    public IReadOnlyList<PackageInfo> Packages { get; }

    public string Runtime => PackageDiagnostics.Runtime;
    public string Os => PackageDiagnostics.Os;
    public string ExePath => PackageDiagnostics.ExePath;
    public string WindowsUser => $@"{Environment.UserDomainName}\{Environment.UserName}";
}
