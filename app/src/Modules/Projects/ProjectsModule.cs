using System.Windows;
using PzlEv.Modules.Projects.Data;
using PzlEv.Modules.Projects.Services;
using PzlEv.Modules.Projects.ViewModels;
using PzlEv.Modules.Projects.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Mapping;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Modularity;

namespace PzlEv.Modules.Projects;

/// <summary>Projekty (F01, F02): lista, kreator projektu z nakładką Performance Objectives i słownikami projektu, ekran projektu z gotowością.</summary>
public sealed class ProjectsModule : IModule
{
    private ProjectService? _service;

    public string Key => ModuleKeys.Projects;

    public string? NavLabel => "Projekty";

    public string Doc => "docs/funkcjonalnosc.md (F01, F02), docs/performance-objectives.md, docs/slowniki.md (rozdz. 3–7)";

    // Magazyny: baza MS SQL środowiska – projekty (META_Project, META_PerformanceObjective), słowniki projektu (DICT_*),
    // mapowanie CES ↔ P1S (tylko odczyt); struktura P1S – PZLPROD (pzl-ev.json, PzlProd).
    public void Initialize(AppServices services) =>
        _service = new ProjectService(
            new SqlProjectStore(services.Sql, services.Clock, services.User),
            new SqlDictionaryStore(services.Sql, services.Clock, services.User, [.. GlobalDictionaries.Tables, .. ProjectDictionaries.Tables]),
            services.Journal,
            new ProjectFolders(services.Config.ProjectsFolder),
            new SqlMappingStore(services.Sql, services.Clock, services.User),
            services.PzlProd is null ? null : new SqlPzlProdSource(services.PzlProd));

    public FrameworkElement CreateView(ModuleContext context) =>
        new ProjectsView { DataContext = new ProjectsViewModel(_service!, new FileDialogs()) };
}
