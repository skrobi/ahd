using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Evm;

/// <summary>Silnik EVM i obliczenie EV. Silnik zależy wyłącznie od modeli wspólnych (docs/architektura.md, rozdz. 5.1). W PoC: ekran zastępczy.</summary>
public sealed class EvmModule : IModule
{
    public string Key => ModuleKeys.Evm;

    public string? NavLabel => null;

    public string Doc => "docs/ev-obliczenia.md, docs/pipeline-fazy.md (P8)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("P8", "Obliczenie EV", "Obliczenie i przegląd", StageScope.Run, CanAutoRun: true),
    ];

    public FrameworkElement CreateView(INavigator navigator) => PlaceholderView.Create(this, navigator);
}
