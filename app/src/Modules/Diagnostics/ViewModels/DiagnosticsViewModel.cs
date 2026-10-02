using System.Windows.Input;
using PzlEv.Modules.Diagnostics.Models;
using PzlEv.Modules.Diagnostics.Services;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Diagnostics.ViewModels;

/// <summary>
/// Diagnostyka środowiska – wynik testu stosu (runtime, ścieżka uruchomienia, konto, pakiety) oraz migracje bazy:
/// lista skryptów sql/mssql ze stanem i przycisk „Migracja” (wykonuje brakujące, wykonane pomija).
/// </summary>
public sealed class DiagnosticsViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", ModuleKeys.Diagnostics);

    private readonly AppServices _services;
    private IReadOnlyList<MigrationInfo> _migrations = [];
    private string _migrationStatus = "";
    private bool _migrating;

    public DiagnosticsViewModel(AppServices services)
    {
        _services = services;
        Migrate = new AsyncRelayCommand(DoMigrate, () => !_migrating);
        Environment = services.Config.Environment;
        Database = services.DataDescription;
        NetworkRoot = services.Config.NetworkRoot;
        ImportFolder = services.Config.ImportFolder;
        AppVersion = services.AppVersion;
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
        LoadMigrations();
    }

    public IReadOnlyList<MigrationInfo> Migrations { get => _migrations; private set => SetProperty(ref _migrations, value); }

    /// <summary>Wynik ostatniej migracji albo stan (ile skryptów do wykonania).</summary>
    public string MigrationStatus { get => _migrationStatus; private set => SetProperty(ref _migrationStatus, value); }

    public ICommand Migrate { get; }

    private void LoadMigrations()
    {
        try
        {
            var status = SqlMigrations.Status(_services.Sql);
            Migrations = status.Select(s => new MigrationInfo(
                    s.Script.Name,
                    s.AppliedAt is { } at ? $"wykonana {at.ToLocalTime():dd.MM.yyyy HH:mm} · {s.AppliedBy}" : "do wykonania",
                    s.AppliedAt is not null))
                .ToList();
            var pending = status.Count(s => s.AppliedAt is null);
            MigrationStatus = pending == 0 ? "Wszystkie migracje wykonane." : $"Do wykonania: {pending}.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Odczyt stanu migracji nieudany");
            MigrationStatus = $"Nie udało się odczytać stanu migracji: {ex.Message}";
        }
    }

    private async Task DoMigrate()
    {
        _migrating = true;
        MigrationStatus = "Migracja w toku…";
        try
        {
            var applied = await Task.Run(() => SqlMigrations.Apply(_services.Sql));
            Logger.Information("Migracja z Diagnostyki: {Scripts}", applied.Count == 0 ? "brak do wykonania" : string.Join(", ", applied));
            LoadMigrations();
            MigrationStatus = applied.Count == 0
                ? "Brak migracji do wykonania – wszystkie skrypty są już w bazie."
                : $"Wykonano: {string.Join(", ", applied)}. Uruchom aplikację ponownie, aby wszystkie ekrany pokazały nowe dane.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Migracja nieudana");
            LoadMigrations();
            MigrationStatus = $"Migracja nieudana (skrypt wycofany): {ex.Message}";
        }
        finally
        {
            _migrating = false;
        }
    }

    public IReadOnlyList<PackageInfo> Packages { get; }

    public string Environment { get; }
    public string Database { get; }
    public string NetworkRoot { get; }
    public string ImportFolder { get; }
    public string AppVersion { get; }

    public string Runtime => PackageDiagnostics.Runtime;
    public string Os => PackageDiagnostics.Os;
    public string ExePath => PackageDiagnostics.ExePath;
    public string WindowsUser => $@"{System.Environment.UserDomainName}\{System.Environment.UserName}";
}
