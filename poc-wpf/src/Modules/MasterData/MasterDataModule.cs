using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.MasterData;

/// <summary>Słowniki globalne i projektu (F03): edycja, historia, walidacja przy zapisie, wymiana przez Excel. W PoC: ekran zastępczy.</summary>
public sealed class MasterDataModule : IModule
{
    public string Key => ModuleKeys.MasterData;

    public string? NavLabel => "Słowniki";

    public string Doc => "docs/slowniki.md, docs/pipeline-fazy.md (G3)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("G3", "Utrzymanie słowników", "Faza globalna", StageScope.Global, CanAutoRun: false),
    ];

    public FrameworkElement CreateView(INavigator navigator) => PlaceholderView.Create(this, navigator);
}
