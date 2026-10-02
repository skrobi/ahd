using System.Windows;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Runs;

/// <summary>Przebiegi (F06): lista przebiegów, ekran przebiegu z krokami i etapami, przypięcie stanu, walidacja, zamknięcie okresu. W PoC: ekran zastępczy.</summary>
public sealed class RunsModule : IModule
{
    public string Key => ModuleKeys.Runs;

    public string? NavLabel => "Przebiegi";

    public string? NavBadge => "2 aktywne"; // dane przykładowe

    public string Doc => "docs/pipeline-fazy.md (rozdz. 4), docs/funkcjonalnosc.md (F06)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("P0", "Uruchomienie", "Przygotowanie przebiegu", StageScope.Run, CanAutoRun: true),
        new("P1", "Przypięcie stanu", "Przygotowanie przebiegu", StageScope.Run, CanAutoRun: true),
        new("P2", "Walidacja", "Walidacja", StageScope.Run, CanAutoRun: true),
        new("Z", "Zamknięcie okresu", "Zamrożenie", StageScope.Run, CanAutoRun: false),
    ];

    public FrameworkElement CreateView(ModuleContext context) => PlaceholderView.Create(this, context.Navigator);
}
