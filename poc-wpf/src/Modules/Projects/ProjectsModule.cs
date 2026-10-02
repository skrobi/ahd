using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Projects;

/// <summary>Projekty (F01, F02): lista, kreator projektu z nakładką Performance Objectives, gotowość projektu. W PoC: ekran zastępczy.</summary>
public sealed class ProjectsModule : IModule
{
    public string Key => ModuleKeys.Projects;

    public string? NavLabel => "Projekty";

    public string? NavBadge => "3"; // dane przykładowe

    public string Doc => "docs/funkcjonalnosc.md (F01, F02), docs/performance-objectives.md";

    public FrameworkElement CreateView(INavigator navigator) => PlaceholderView.Create(this, navigator);
}
