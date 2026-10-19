using System.Windows;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Modules.Runs.ViewModels;
using PzlEv.Modules.Runs.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Modularity;
using PzlEv.Shared.Views.Partials;

namespace PzlEv.Modules.Runs;

/// <summary>
/// Przebiegi (F06): ekran administracji szablonów przebiegów – etapy w kafelkach, okno Kontrole (RunsView); przebieg programu
/// uruchamia i analizuje ekran programu – panel przebiegu (IRunPanels), przebieg w tle (IRunLauncher), okno „Zmiany ACWP” (IAcwpChanges).
/// Kroki i etapy P3–P9, Z – do implementacji (F5–F9).
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

    /// <summary>Kontrakty IRunLauncher (przebieg programu w tle), IRunPanels (panel przebiegu) i IAcwpChanges dla ekranu programu.</summary>
    public void Initialize(AppServices services)
    {
        var store = _store = new SqlRunStore(services.Sql);
        ISourceImport? Imports() => services.Contracts.Get<ISourceImport>();
        IProjectProcessSource? Processes() => services.Contracts.Get<IProjectProcessSource>();
        var launcher = new RunLauncher(new RunService(store, services, Imports, Processes), store, Imports);
        services.Contracts.Register<IRunLauncher>(launcher);
        services.Contracts.Register<IAcwpChanges>(_acwpChanges = new AcwpChangesLauncher(store, Processes));
        services.Contracts.Register<IRunPanels>(new RunPanels(store, launcher, () => _acwpChanges));
        _dictionaries = new SqlDictionaryStore(services.Sql, services.Clock, services.User, GlobalDictionaries.Tables);
    }

    private SqlRunStore? _store;
    private IAcwpChanges? _acwpChanges;
    private SqlDictionaryStore? _dictionaries;

    public FrameworkElement CreateView(ModuleContext context)
    {
        var dictionaries = new DictionaryService(_dictionaries!, context.Services.Journal);
        var templates = new RunTemplateService(dictionaries, _store!);
        return new RunsView
        {
            DataContext = new RunsViewModel(templates, _store!, saved => RunChecksWindow.Open(
                new RunChecksViewModel(templates, new RunDictionaryEditorViewModel(dictionaries, new FileDialogs(),
                    [GlobalDictionaries.RunReconciliations, GlobalDictionaries.RunChecks]), new RunValidationEditorViewModel(templates, _store!)), saved)),
        };
    }
}
