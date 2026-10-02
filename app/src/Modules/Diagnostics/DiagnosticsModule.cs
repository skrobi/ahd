using System.Windows;
using PzlEv.Modules.Diagnostics.ViewModels;
using PzlEv.Modules.Diagnostics.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Modularity;

namespace PzlEv.Modules.Diagnostics;

/// <summary>Diagnostyka środowiska: wynik testu stosu (runtime, pakiety, ścieżka, konto) i migracje bazy.</summary>
public sealed class DiagnosticsModule : IModule
{
    public string Key => ModuleKeys.Diagnostics;

    public string? NavLabel => "Diagnostyka";

    public string Doc => "app/README.md";

    public FrameworkElement CreateView(ModuleContext context)
        => new DiagnosticsView { DataContext = new DiagnosticsViewModel(context.Services) };
}
