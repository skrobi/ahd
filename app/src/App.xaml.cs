using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shell;
using Serilog;

namespace PzlEv;

public partial class App : Application
{
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Serilog zapisuje log obok uruchomionego .exe (katalog aplikacji) – sprawdza też prawo zapisu
        // w miejscu uruchomienia (np. na dysku sieciowym).
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, "pzl-ev-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        DispatcherUnhandledException += OnUnhandledException;

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        version = version.Split('+')[0];
        Log.Information("PZL-EV {Version} start. BaseDirectory={BaseDirectory}", version, AppContext.BaseDirectory);

        try
        {
            // Konfiguracja (pzl-ev.json) → usługi wspólne → moduły (Shell/ModuleCatalog.cs) → powłoka.
            var config = AppConfigLoader.Load(AppContext.BaseDirectory);
            Log.Information("Środowisko {Environment}, dane {DataMode}, korzeń {NetworkRoot}", config.Environment, config.DataMode, config.NetworkRoot);
            _services = AppServices.Create(config, version);
            var modules = ModuleCatalog.Create(_services);
            _services.Database.Commit();
            var window = new ShellWindow { DataContext = new ShellViewModel(modules, _services) };
            window.Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
        {
            Log.Error(ex, "Błąd uruchomienia");
            MessageBox.Show(ex.Message, "PZL-EV – nie można uruchomić", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Nieobsłużony wyjątek UI");
        MessageBox.Show(e.Exception.Message, "PZL-EV – błąd", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.Database.Commit();
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Nie zapisano stanu danych w pamięci");
        }
        Log.Information("PZL-EV stop.");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
