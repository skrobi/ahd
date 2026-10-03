using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Export;

/// <summary>Publikacja: pliki wynikowe i plik dla Cobra. Bez własnego ekranu – panel etapu w ekranie przebiegu. Ekran zastępczy do czasu implementacji (F9).</summary>
public sealed class ExportModule : IModule
{
    public string Key => ModuleKeys.Export;

    public string? NavLabel => null;

    public string Doc => "docs/funkcjonalnosc.md (rozdz. 4), docs/pipeline-fazy.md (P9)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("P9", "Publikacja", "Publikacja", StageScope.Run, CanAutoRun: true),
    ];

    public FrameworkElement CreateView(ModuleContext context) => PlaceholderView.Create(this, context.Navigator);
}
