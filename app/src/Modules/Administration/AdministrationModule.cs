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

    // Magazyn danych: baza MS SQL środowiska (Sql*Store); dane startowe – migracja sql/mssql/002_dane_startowe.sql.
    public void Initialize(AppServices services) =>
        _service = new SourceConfigService(new SqlSourceConfigStore(services.Sql, services.Clock, services.User), services.Journal);

    public FrameworkElement CreateView(ModuleContext context) =>
        new AdministrationView { DataContext = new AdministrationViewModel(_service!) };
}
