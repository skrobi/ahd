using System.Windows;
using PzlEv.Shared.Utils.Mapping;
using PzlEv.Modules.Mapping.Services;
using PzlEv.Modules.Mapping.ViewModels;
using PzlEv.Modules.Mapping.Views;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Ui.Modularity;

namespace PzlEv.Modules.Mapping;

/// <summary>
/// Mapowanie CES ↔ P1S (F04, G2): raport mapowań ze słownika globalnego, struktura P1S z PZLPROD, rozstrzyganie,
/// korekty elementu i projektu CES z historią, elementy nieprzypisane z propozycją celu.
/// </summary>
public sealed class MappingModule : IModule
{
    private MappingService? _service;

    public string Key => ModuleKeys.Mapping;

    public string? NavLabel => "Mapowanie";

    public string Doc => "docs/mapowanie-ces-p1s.md, docs/pipeline-fazy.md (G2)";

    public IReadOnlyList<StageDescriptor> Stages { get; } =
    [
        new("G2", "Rozstrzygnięcie mapowania", "Faza globalna", StageScope.Global, CanAutoRun: true),
    ];

    // Magazyn: baza MS SQL środowiska (raport mapowań, ACTUALS, korekty); struktura P1S – PZLPROD (pzl-ev.json, PzlProd).
    public void Initialize(AppServices services) =>
        _service = new MappingService(new SqlMappingStore(services.Sql, services.Clock, services.User),
            services.PzlProd is null ? null : new SqlPzlProdSource(services.PzlProd), services.Journal, services.Problems);

    public FrameworkElement CreateView(ModuleContext context) => new MappingView { DataContext = new MappingViewModel(_service!) };
}
