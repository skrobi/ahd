using System.Windows;
using PzlEv.Modules.Administration.Data;
using PzlEv.Modules.Administration.Services;
using PzlEv.Modules.Administration.ViewModels;
using PzlEv.Modules.Administration.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Ui.Modularity;

namespace PzlEv.Modules.Administration;

/// <summary>Administracja (F08): definicje źródeł i lokalizacje RABIT. Role i grupy AD – później.</summary>
public sealed class AdministrationModule : IModule
{
    private SourceConfigService? _service;

    public string Key => ModuleKeys.Administration;

    public string? NavLabel => "Administracja";

    public string Doc => "docs/funkcjonalnosc.md (F08), docs/zrodla-danych.md (rozdz. 2–3), docs/uprawnienia.md";

    // Magazyn danych: w trybie w pamięci InMemorySourceConfigStore; po F10 – magazyn SQL wybierany tutaj.
    public void Initialize(AppServices services)
    {
        ISourceConfigStore store = services.Sql is { } sql
            ? new SqlSourceConfigStore(sql, services.Clock, services.User)
            : new InMemorySourceConfigStore(services.Database, services.Clock, services.User);
        _service = new SourceConfigService(store, services.Journal);
        SourceConfigSeed.EnsureSeeded(_service);
    }

    public FrameworkElement CreateView(ModuleContext context) =>
        new AdministrationView { DataContext = new AdministrationViewModel(_service!) };
}
