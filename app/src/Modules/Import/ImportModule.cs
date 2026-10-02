using System.Windows;
using PzlEv.Modules.Import.Data;
using PzlEv.Modules.Import.Services;
using PzlEv.Modules.Import.ViewModels;
using PzlEv.Modules.Import.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Ui.Modularity;

namespace PzlEv.Modules.Import;

/// <summary>Import źródeł (F05, G1): lokalizacje RABIT i folder Do_importu → wiersze surowe i dane kanoniczne.</summary>
public sealed class ImportModule : IModule
{
    public string Key => ModuleKeys.Import;

    public string? NavLabel => "Import RABIT";

    public string Doc => "docs/pipeline-fazy.md (G1), docs/zrodla-danych.md";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("G1", "Import źródeł", "Faza globalna", StageScope.Global, CanAutoRun: true),
    ];

    // Magazyn danych: w trybie w pamięci InMemoryImportStore; po F10 – magazyn SQL wybierany tutaj.
    public FrameworkElement CreateView(ModuleContext context)
    {
        var store = new InMemoryImportStore(context.Services.Database);
        return new ImportView { DataContext = new ImportViewModel(new ImportService(store, context.Services), store, new SharePointLoginDialog()) };
    }
}
