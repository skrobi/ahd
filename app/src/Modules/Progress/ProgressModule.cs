using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Progress;

/// <summary>Zaawansowanie: produkcja, uzupełnienia, pliki CAM. Bez własnego ekranu – panele etapów w ekranie przebiegu. W PoC: ekran zastępczy.</summary>
public sealed class ProgressModule : IModule
{
    public string Key => ModuleKeys.Progress;

    public string? NavLabel => null;

    public string Doc => "docs/pipeline-fazy.md (P5–P7)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("P5", "Zaawansowanie z produkcji", "Zaawansowanie", StageScope.Run, CanAutoRun: true),
        new("P6", "Uzupełnienie zaawansowania", "Zaawansowanie", StageScope.Run, CanAutoRun: false),
        new("P7", "Walidacja zaawansowania", "Zaawansowanie", StageScope.Run, CanAutoRun: true),
    ];

    public FrameworkElement CreateView(ModuleContext context) => PlaceholderView.Create(this, context.Navigator);
}
