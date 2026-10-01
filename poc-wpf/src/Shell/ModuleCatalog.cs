using PzlEv.Modules.Administration;
using PzlEv.Modules.Dashboard;
using PzlEv.Modules.Diagnostics;
using PzlEv.Modules.Evm;
using PzlEv.Modules.Export;
using PzlEv.Modules.Import;
using PzlEv.Modules.Mapping;
using PzlEv.Modules.MasterData;
using PzlEv.Modules.Progress;
using PzlEv.Modules.Projects;
using PzlEv.Modules.Reconciliation;
using PzlEv.Modules.Runs;
using PzlEv.Shared.Utils.Modularity;
using Serilog;

namespace PzlEv.Shell;

/// <summary>
/// Lista modułów aplikacji – jedyne miejsce w kodzie, które zna konkretne moduły (sprawdza to
/// ArchitectureRules.targets). Kolejność = kolejność menu. Nowy moduł: jedna linia tutaj
/// (docs/architektura.md, rozdz. 5.3, „Nowy moduł”).
/// </summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> Create()
    {
        IReadOnlyList<IModule> modules =
        [
            new DashboardModule(),
            new ImportModule(),
            new MappingModule(),
            new ProjectsModule(),
            new RunsModule(),
            new MasterDataModule(),
            new ReconciliationModule(),
            new ProgressModule(),
            new EvmModule(),
            new ExportModule(),
            new AdministrationModule(),
            new DiagnosticsModule(),
        ];

        Validate(modules);
        return modules;
    }

    private static void Validate(IReadOnlyList<IModule> modules)
    {
        var duplicateKey = modules.GroupBy(m => m.Key).FirstOrDefault(g => g.Count() > 1);
        if (duplicateKey is not null)
            throw new InvalidOperationException($"Zdublowany klucz modułu: {duplicateKey.Key}");

        var duplicateStage = modules.SelectMany(m => m.Stages).GroupBy(s => s.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicateStage is not null)
            throw new InvalidOperationException($"Etap {duplicateStage.Key} zadeklarowany w więcej niż jednym module");

        Log.Information("Moduły: {Count}; etapy: {Stages}", modules.Count,
            string.Join(", ", modules.SelectMany(m => m.Stages.Select(s => $"{s.Code}→{m.Key}"))));
    }
}
