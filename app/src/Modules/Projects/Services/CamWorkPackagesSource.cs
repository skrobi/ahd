using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Kontrakt ICamWorkPackages (moduł CAM): CAM-owie z „WP i CAM” programów, WP CAM-a ze struktury każdego programu
/// (ProjectService.StructureOf – wiersz własnego WP, najwyżej w drzewie) i zapis zatwierdzonej estymacji (ApplyCamEstimate).
/// </summary>
public sealed class CamWorkPackagesSource(ProjectService service) : ICamWorkPackages
{
    public IReadOnlyList<CamPerson> Cams()
    {
        var names = service.PersonLookups().GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
        return service.Projects()
            .SelectMany(p => service.Rows(ProjectDictionaries.WpCam, p.Code)).Select(r => r["CAM"]).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(c => new CamPerson(c, names.GetValueOrDefault(c) ?? c))
            .OrderBy(c => c.Name, StringComparer.CurrentCulture).ToList();
    }

    public IReadOnlyList<CamWorkPackage> WorkPackages(string cam) =>
        Collect(_ => true, value => string.Equals(value, cam, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<CamWorkPackage> ProjectWorkPackages(string? project) =>
        Collect(code => project is null || string.Equals(code, project, StringComparison.OrdinalIgnoreCase), value => !string.IsNullOrWhiteSpace(value));

    /// <summary>WP z CAM-em spełniającym isCam w programach spełniających inProject (struktura każdego programu raz).</summary>
    private List<CamWorkPackage> Collect(Func<string, bool> inProject, Func<string?, bool> isCam)
    {
        bool IsCam(string? value) => isCam(value);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var projects = service.Projects().Where(p => inProject(p.Code) && service.Rows(ProjectDictionaries.WpCam, p.Code).Any(r => IsCam(r["CAM"]))).ToList();
        if (projects.Count == 0)
            return [];
        var mapping = service.Mapping();
        var departmentRates = service.DepartmentRates();
        var fxRates = service.FxRates();
        var result = new List<CamWorkPackage>();
        foreach (var project in projects)
        {
            var (structure, rates, note) = service.StructureOf(project.Code, mapping, departmentRates, fxRates, today);
            foreach (var row in structure.Rows.Where(r => r.Wp is not null && IsCam(r.Cam)).GroupBy(r => r.Wp!, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.OrderBy(r => r.Depth).First()))
            {
                result.Add(new CamWorkPackage(project.Code, project.Name, row.Wp!, row.Name, row.Cam!, row.Mpk, rates.Currency,
                    row.Start, row.Finish, row.ForecastStart, row.ForecastFinish, row.BacHours, row.BacMaterial, row.Bac, row.PvCost, row.EvCost,
                    row.Acwp, row.AcwpLabor, row.AcwpMaterial, row.AcHours ?? (row.ActualDlh != 0 ? row.ActualDlh : null),
                    row.OpsBacHours is not null, row.ProductionEtcHours, row.EtcHours, row.MaterialEtc, row.EtcCost,
                    row.UndeliveredByYear ?? new Dictionary<int, decimal>(), ProjectService.HourRates(departmentRates, row.Mpk, rates),
                    rates.LaborFactor is { } labor && labor != 0 ? 1 / labor : null,
                    rates.MaterialFactor is { } usd && usd != 0 ? 1 / usd : null, note, row.ProductionEtcByYear, row.PlanStart, row.PlanFinish,
                    row.Status == "zakończony"));
            }
        }
        return result;
    }

    public string? ApplyEstimate(string project, string wp, decimal etcHours, decimal materialEtc, string? currency, DateOnly? forecastStart,
        DateOnly? forecastFinish) =>
        service.ApplyCamEstimate(project, wp, etcHours, materialEtc, currency, forecastStart, forecastFinish);
}
