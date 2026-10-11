using PzlEv.Modules.Import.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Import.ViewModels;

/// <summary>Kontrakt ISourceImport dla innych modułów: logowanie do SharePoint (okno) i import wybranych źródeł.</summary>
public sealed class SourceImport(ImportService service, ISharePointLoginDialog dialog) : ISourceImport
{
    public IReadOnlyList<SourceDefinitionRow> Definitions() => service.Definitions();

    public Task<bool> SignIn() => SharePointSignIn.Run(service, dialog, _ => { });

    public SourceImportResult Import(IReadOnlySet<string> sources, IProgress<string>? progress, CancellationToken cancellation)
    {
        var run = service.Run(progress is null ? null : new Progress(progress), cancellation, sources);
        return new SourceImportResult(run.BatchId, run.Count(FileDecisions.Imported), run.Count(FileDecisions.Error), run.NotStarted ?? run.Summary, run.Issues);
    }

    private sealed class Progress(IProgress<string> target) : IProgress<Models.ImportProgress>
    {
        public void Report(Models.ImportProgress value) => target.Report(value.Message);
    }
}
