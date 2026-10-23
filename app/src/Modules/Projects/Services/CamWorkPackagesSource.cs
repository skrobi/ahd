using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;
using Serilog;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Kontrakt ICamWorkPackages (moduł CAM): CAM-owie z „WP i CAM” programów, WP CAM-a ze struktury każdego programu
/// (ProjectService.StructureOf – wiersz własnego WP, najwyżej w drzewie) i zapis zatwierdzonej estymacji (ApplyCamEstimate).
/// </summary>
public sealed class CamWorkPackagesSource(ProjectService service) : ICamWorkPackages
{
    private static readonly ILogger Logger = Log.ForContext("Module", "projects");

    public IReadOnlyList<CamPerson> Cams()
    {
        var names = service.PersonLookups().GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
        var emails = service.PersonEmails();
        return service.Projects()
            .SelectMany(p => service.Rows(ProjectDictionaries.WpCam, p.Code)).Select(r => r["CAM"]).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(c => new CamPerson(c, names.GetValueOrDefault(c) ?? c, emails.GetValueOrDefault(c)))
            .OrderBy(c => c.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Najwyżej tyle struktur programów naraz (PZLPROD i baza środowiska – osobne połączenia).</summary>
    public const int Parallelism = 3;

    public IReadOnlyList<CamWorkPackage> WorkPackages(string cam) =>
        Collect(_ => true, value => string.Equals(value, cam, StringComparison.OrdinalIgnoreCase), costs: true);

    // Lista wymaganych WP (etap przebiegu, postęp estymacji) – bez kosztów ACTUALS (ACWP, waluta), które są najdroższą częścią struktury.
    public IReadOnlyList<CamWorkPackage> ProjectWorkPackages(string? project) =>
        Collect(code => project is null || string.Equals(code, project, StringComparison.OrdinalIgnoreCase), value => !string.IsNullOrWhiteSpace(value), costs: false);

    /// <summary>
    /// WP z CAM-em spełniającym isCam w programach spełniających inProject: struktura każdego programu raz, programy równolegle
    /// (Parallelism), kolejność programów jak na liście; czas każdego programu w dzienniku aplikacji (log).
    /// </summary>
    private List<CamWorkPackage> Collect(Func<string, bool> inProject, Func<string?, bool> isCam, bool costs)
    {
        bool IsCam(string? value) => isCam(value);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var projects = service.Projects().Where(p => inProject(p.Code) && service.Rows(ProjectDictionaries.WpCam, p.Code).Any(r => IsCam(r["CAM"]))).ToList();
        if (projects.Count == 0)
            return [];
        var mapping = service.Mapping();
        var departmentRates = service.DepartmentRates();
        var fxRates = service.FxRates();
        var perProject = new List<CamWorkPackage>[projects.Count];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Parallel.For(0, projects.Count, new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, i =>
            {
                var started = watch.ElapsedMilliseconds;
                perProject[i] = Of(projects[i]);
                Logger.Information("CAM: struktura programu {Code} ({Costs}) – {Ms} ms", projects[i].Code, costs ? "z kosztami" : "bez kosztów",
                    watch.ElapsedMilliseconds - started);
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
            throw;
        }
        Logger.Information("CAM: {Count} programów – {Ms} ms", projects.Count, watch.ElapsedMilliseconds);
        return [.. perProject.SelectMany(p => p)];

        List<CamWorkPackage> Of(ProjectInfo project)
        {
            var (structure, rates, note) = service.StructureOf(project.Code, mapping, departmentRates, fxRates, today, costs);
            var weekly = RunDefinitions.CamWeekly(service.RunDefinitionOf(project.Code));
            return structure.Rows.Where(r => r.Wp is not null && IsCam(r.Cam)).GroupBy(r => r.Wp!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(r => r.Depth).First())
                .Select(row => new CamWorkPackage(project.Code, project.Name, row.Wp!, row.Name, row.Cam!, row.Mpk, rates.Currency,
                    row.Start, row.Finish, row.ForecastStart, row.ForecastFinish, row.BacHours, row.BacMaterial, row.Bac, row.PvCost, row.EvCost,
                    row.Acwp, row.AcwpLabor, row.AcwpMaterial, row.AcHours ?? (row.ActualDlh != 0 ? row.ActualDlh : null),
                    row.OpsBacHours is not null, row.ProductionEtcHours, row.EtcHours, row.MaterialEtc, row.EtcCost,
                    row.UndeliveredByYear ?? new Dictionary<int, decimal>(), ProjectService.HourRates(departmentRates, row.Mpk, rates),
                    rates.LaborFactor is { } labor && labor != 0 ? 1 / labor : null,
                    rates.MaterialFactor is { } usd && usd != 0 ? 1 / usd : null, note, row.ProductionEtcByYear, row.PlanStart, row.PlanFinish,
                    row.Status == "zakończony", weekly))
                .ToList();
        }
    }

    public string? ApplyEstimate(string project, string wp, decimal etcHours, decimal materialEtc, string? currency, DateOnly? forecastStart,
        DateOnly? forecastFinish) =>
        service.ApplyCamEstimate(project, wp, etcHours, materialEtc, currency, forecastStart, forecastFinish);
}
