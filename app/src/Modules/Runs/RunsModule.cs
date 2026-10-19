using System.Windows;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Modules.Runs.ViewModels;
using PzlEv.Modules.Runs.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Runs;

/// <summary>
/// Przebiegi (F06): ekran z macierzą wyników kontroli uruchomień programu i definicją przebiegu (RunsView); przebieg w tle
/// (IRunLauncher) uruchamia ekran programu. Kroki i etapy P3–P9, Z – do implementacji (F5–F9).
/// </summary>
public sealed class RunsModule : IModule
{
    public string Key => ModuleKeys.Runs;

    public string? NavLabel => "Przebiegi";

    public string Doc => "docs/pipeline-fazy.md (rozdz. 4), docs/funkcjonalnosc.md (F06)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("P0", "Uruchomienie", "Przygotowanie przebiegu", StageScope.Run, CanAutoRun: true),
        new("P1", "Przypięcie stanu", "Przygotowanie przebiegu", StageScope.Run, CanAutoRun: true),
        new("P2", "Walidacja", "Walidacja", StageScope.Run, CanAutoRun: true),
        new("Z", "Zamknięcie okresu", "Zamrożenie", StageScope.Run, CanAutoRun: false),
    ];

    /// <summary>Kontrakt IRunLauncher (przebieg projektu w tle) dla ekranu projektu.</summary>
    public void Initialize(AppServices services)
    {
        var store = _store = new SqlRunStore(services.Sql);
        ISourceImport? Imports() => services.Contracts.Get<ISourceImport>();
        services.Contracts.Register<IRunLauncher>(new RunLauncher(new RunService(store, services, Imports, () => services.Contracts.Get<IProjectProcessSource>()),
            store, Imports));
    }

    private SqlRunStore? _store;

    public FrameworkElement CreateView(ModuleContext context) =>
        new RunsView { DataContext = new RunsViewModel(_store ?? new SqlRunStore(context.Services.Sql)) };
}
