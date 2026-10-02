using System.Windows;
using PzlEv.Modules.Dashboard.Data;
using PzlEv.Modules.Dashboard.ViewModels;
using PzlEv.Modules.Dashboard.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Modularity;

namespace PzlEv.Modules.Dashboard;

/// <summary>Pulpit (F07). Jedyny moduł z pełnym ekranem w PoC – wzorzec dla kolejnych modułów.</summary>
public sealed class DashboardModule : IModule
{
    public string Key => ModuleKeys.Dashboard;

    public string? NavLabel => "Pulpit";

    public string Doc => "docs/funkcjonalnosc.md (F07)";

    // Źródło danych wybierane tutaj: dane przykładowe → docelowo implementacja na widokach bazy.
    public FrameworkElement CreateView(INavigator navigator)
        => new DashboardView { DataContext = new DashboardViewModel(new DashboardSampleData(), navigator) };
}
