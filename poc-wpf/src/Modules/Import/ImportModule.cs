using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Import;

/// <summary>Import źródeł (F05): pobranie plików z lokalizacji RABIT i folderu Do_importu, parsery źródeł, historia importów. W PoC: ekran zastępczy.</summary>
public sealed class ImportModule : IModule
{
    public string Key => ModuleKeys.Import;

    public string? NavLabel => "Import RABIT";

    public string Doc => "docs/pipeline-fazy.md (G1), docs/zrodla-danych.md";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("G1", "Import źródeł", "Faza globalna", StageScope.Global, CanAutoRun: true),
    ];

    public FrameworkElement CreateView(INavigator navigator) => PlaceholderView.Create(this, navigator);
}
