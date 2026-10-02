using System.IO;
using System.Windows;
using System.Windows.Threading;
using PzlEv.Shell;
using Serilog;

namespace PzlEv;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Serilog zapisuje log obok uruchomionego .exe (katalog aplikacji).
        // Potwierdza, że pakiet Serilog działa i że mamy prawo zapisu w miejscu
        // uruchomienia (np. na dysku sieciowym).
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, "pzl-ev-test-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        DispatcherUnhandledException += OnUnhandledException;

        Log.Information("PZL-EV (test) start. BaseDirectory={BaseDirectory}", AppContext.BaseDirectory);

        // Lista modułów (Shell/ModuleCatalog.cs) → powłoka z menu i nawigacją.
        var window = new ShellWindow { DataContext = new ShellViewModel(ModuleCatalog.Create()) };
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Nieobsłużony wyjątek UI");
        MessageBox.Show(e.Exception.Message, "PZL-EV (test) – błąd",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("PZL-EV (test) stop.");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
