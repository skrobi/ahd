using System.Windows.Input;
using PzlEv.Shared.Utils.Process;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Ekran programu – zakładka Raporty: raport kosztów i raport kosztów CES (CAS).</summary>
public sealed partial class ProjectDetailViewModel
{
    public ICommand ExportCostReport { get; }

    /// <summary>Raport kosztów CES z narzutami CAS w formułach (zakładka Raporty – tylko projekty CAS).</summary>
    public ICommand ExportCasCostReport { get; }

    /// <summary>Proces projektu (ścieżka wspólna albo wariant typu – ProjectService.Process).</summary>
    private ProjectProcess ProjectProcess => _projectProcess ??= _service.Process(Code, Project.Type, null);
    private ProjectProcess? _projectProcess;

    /// <summary>Raport kosztów z narzutami w formułach (raport kosztów CES) – projekty, których proces go ma (CAS).</summary>
    public bool HasBurdenReport => ProjectProcess.HasBurdenReport;

    /// <summary>Raport kosztów projektu z ostatniego importu ACTUALS do Excela (procedura REP_ProjectCosts).</summary>
    private async Task DoExportCostReport()
    {
        var path = _dialogs.SaveExcel("Raport kosztów programu", $"{Code}_koszty.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            var rows = await Busy.Run("Raport kosztów – ostatni import ACTUALS programu…", () => _service.ExportCostReport(path, Code));
            Status = $"Zapisano {path} – {rows} wierszy (Project definition × Cost Element).";
        });
    }

    /// <summary>Raport kosztów CES do Excela: raport kosztów z narzutami CAS liczonymi formułami (ProjectService.ExportCasCostReport).</summary>
    private async Task DoExportCasCostReport()
    {
        var path = _dialogs.SaveExcel("Raport kosztów CES", $"{Code}_koszty_CES.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            var (rows, notes) = await Busy.Run("Raport kosztów CES – ostatni import ACTUALS i narzuty CAS…", () => _service.ExportCasCostReport(path, Code));
            Status = $"Zapisano {path} – {rows} wierszy (Project definition × Cost Element)"
                     + (notes > 0 ? $"; uwagi: {notes} (arkusz „Uwagi” – brak grupy narzutowej albo wskaźnika)." : ".");
        });
    }
}
