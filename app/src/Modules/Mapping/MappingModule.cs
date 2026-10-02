using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Mapping;

/// <summary>Mapowanie CES ↔ P1S (F04): globalny raport mapowań, korekty, elementy nieprzypisane. W PoC: ekran zastępczy.</summary>
public sealed class MappingModule : IModule
{
    public string Key => ModuleKeys.Mapping;

    public string? NavLabel => "Mapowanie";

    public string? NavBadge => "6"; // dane przykładowe

    public string Doc => "docs/mapowanie-ces-p1s.md, docs/pipeline-fazy.md (G2)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("G2", "Rozstrzygnięcie mapowania", "Faza globalna", StageScope.Global, CanAutoRun: true),
    ];

    public FrameworkElement CreateView(ModuleContext context) => PlaceholderView.Create(this, context.Navigator);
}
