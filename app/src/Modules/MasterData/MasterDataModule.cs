using System.Windows;
using PzlEv.Modules.MasterData.Data;
using PzlEv.Modules.MasterData.Services;
using PzlEv.Modules.MasterData.ViewModels;
using PzlEv.Modules.MasterData.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Modularity;

namespace PzlEv.Modules.MasterData;

/// <summary>Słowniki globalne (F03, G3): edycja z historią i walidacją, wymiana przez Excel.</summary>
public sealed class MasterDataModule : IModule
{
    private IDictionaryStore? _store;

    public string Key => ModuleKeys.MasterData;

    public string? NavLabel => "Słowniki";

    public string Doc => "docs/slowniki.md, docs/pipeline-fazy.md (G3)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("G3", "Utrzymanie słowników", "Faza globalna", StageScope.Global, CanAutoRun: false),
    ];

    // Magazyn danych: baza MS SQL środowiska (Sql*Store); dane startowe – migracja sql/mssql/002_dane_startowe.sql.
    public void Initialize(AppServices services) => _store = new SqlDictionaryStore(services.Sql, services.Clock, services.User);

    public FrameworkElement CreateView(ModuleContext context) =>
        new MasterDataView { DataContext = new MasterDataViewModel(new DictionaryService(_store!, context.Services.Journal), new FileDialogs()) };
}
