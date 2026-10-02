using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;
using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
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
            Log.Information("Środowisko {Environment}, baza {Database}, korzeń {NetworkRoot}", config.Environment, config.Sql.Describe, config.NetworkRoot);
            _services = AppServices.Create(config, version);
            OfferMigrations(_services.Sql);
            var modules = ModuleCatalog.Create(_services);
            var window = new ShellWindow { DataContext = new ShellViewModel(modules, _services) };
            window.Show();
        }
        catch (SqlException ex)
        {
            Log.Error(ex, "Brak połączenia z bazą");
            MessageBox.Show($"Baza danych {_services?.Sql?.Describe}: {ex.Message}", "PZL-EV – baza danych niedostępna", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
        {
            Log.Error(ex, "Błąd uruchomienia");
            MessageBox.Show(ex.Message, "PZL-EV – nie można uruchomić", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Brakujące migracje bazy (sql/mssql) – pytanie przy starcie. „Nie” – aplikacja startuje, a migracje można wykonać
    /// później: Diagnostyka → Migracja (wymaga prawa tworzenia tabel w schemacie).
    /// </summary>
    private static void OfferMigrations(SqlDatabase sql)
    {
        var pending = SqlMigrations.Pending(sql);
        Log.Information("Baza {Database}: wersja schematu {Current}, do wykonania {Pending}", sql.Describe, SqlMigrations.CurrentVersion(sql),
            pending.Count == 0 ? "–" : string.Join(", ", pending.Select(s => s.Name)));
        if (pending.Count == 0)
            return;
        var answer = MessageBox.Show(
            $"Baza {sql.Describe}\nMigracje do wykonania:\n{string.Join("\n", pending.Select(s => "  • " + s.Name))}\n\n" +
            "Wykonać teraz? (wymaga prawa tworzenia tabel w schemacie)\n\n" +
            "Nie – aplikacja uruchomi się bez nich; migracje wykonasz później: Diagnostyka → Migracja.",
            "PZL-EV – migracje bazy danych", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            Log.Warning("Migracje odłożone przez użytkownika");
            return;
        }
        var applied = SqlMigrations.Apply(sql);
        Log.Information("Wykonane migracje: {Scripts}", string.Join(", ", applied));
        MessageBox.Show($"Wykonano: {string.Join(", ", applied)}", "PZL-EV – migracje bazy danych", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Nieobsłużony wyjątek UI");
        MessageBox.Show(e.Exception.Message, "PZL-EV – błąd", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("PZL-EV stop.");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
