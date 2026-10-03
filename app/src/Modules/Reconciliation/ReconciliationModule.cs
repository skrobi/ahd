using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Reconciliation;

/// <summary>Uzgodnienie: łączenie źródeł i pliki dla finansów. Bez własnego ekranu – panele etapów w ekranie przebiegu. Ekran zastępczy do czasu implementacji (F6).</summary>
public sealed class ReconciliationModule : IModule
{
    public string Key => ModuleKeys.Reconciliation;

    public string? NavLabel => null;

    public string Doc => "docs/pipeline-fazy.md (P3, P4)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("P3", "Łączenie źródeł", "Uzgodnienie", StageScope.Run, CanAutoRun: true),
        new("P4", "Pliki dla finansów", "Przegląd finansów", StageScope.Run, CanAutoRun: false),
    ];

    public FrameworkElement CreateView(ModuleContext context) => PlaceholderView.Create(this, context.Navigator);
}
