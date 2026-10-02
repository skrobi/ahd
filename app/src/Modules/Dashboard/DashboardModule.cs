using System.Windows;
using PzlEv.Modules.Dashboard.Data;
using PzlEv.Modules.Dashboard.ViewModels;
using PzlEv.Modules.Dashboard.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Modularity;

namespace PzlEv.Modules.Dashboard;

/// <summary>Pulpit (F07): tylko dane z bazy środowiska (SqlDashboardData).</summary>
public sealed class DashboardModule : IModule
{
    public string Key => ModuleKeys.Dashboard;

    public string? NavLabel => "Pulpit";

    public string Doc => "docs/funkcjonalnosc.md (F07)";

    public FrameworkElement CreateView(ModuleContext context)
        => new DashboardView { DataContext = new DashboardViewModel(new SqlDashboardData(context.Services), context.Navigator) };
}
