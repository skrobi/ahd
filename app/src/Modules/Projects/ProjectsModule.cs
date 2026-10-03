using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Projects;

/// <summary>Projekty (F01, F02): lista, kreator projektu z nakładką Performance Objectives, gotowość projektu. Ekran zastępczy do czasu implementacji (F4).</summary>
public sealed class ProjectsModule : IModule
{
    public string Key => ModuleKeys.Projects;

    public string? NavLabel => "Projekty";

    public string Doc => "docs/funkcjonalnosc.md (F01, F02), docs/performance-objectives.md";

    public FrameworkElement CreateView(ModuleContext context) => PlaceholderView.Create(this, context.Navigator);
}
