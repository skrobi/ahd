using System.Windows;
using PzlEv.Modules.Diagnostics.ViewModels;
using PzlEv.Modules.Diagnostics.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Modularity;

namespace PzlEv.Modules.Diagnostics;

/// <summary>Diagnostyka środowiska – moduł PoC: wynik testu stosu (runtime, pakiety, ścieżka, konto).</summary>
public sealed class DiagnosticsModule : IModule
{
    public string Key => ModuleKeys.Diagnostics;

    public string? NavLabel => "Diagnostyka";

    public string Doc => "poc-wpf/README.md";

    public FrameworkElement CreateView(INavigator navigator)
        => new DiagnosticsView { DataContext = new DiagnosticsViewModel() };
}
