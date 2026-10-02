using PzlEv.Shared.Models;

namespace PzlEv.Modules.Import.Models;

/// <summary>Wynik uruchomienia importu: partia, decyzje dla plików, problemy, czy przerwany.</summary>
public sealed record ImportRunResult(long BatchId, IReadOnlyList<FileResult> Files, IReadOnlyList<Issue> Issues, bool Cancelled)
{
    /// <summary>Niedostępne lokalizacje SharePoint (WebDAV) – zwykle brak zalogowania do bramy.</summary>
    public IReadOnlyList<string> UnavailableSharePoint { get; init; } = [];

    public int Count(string decision) => Files.Count(f => f.Decision == decision);

    public string Summary =>
        $"Pliki: {Files.Count} · zaimportowane {Count(Shared.Models.Db.FileDecisions.Imported)} · pominięte {Count(Shared.Models.Db.FileDecisions.Skipped)} · " +
        $"duplikaty {Count(Shared.Models.Db.FileDecisions.Duplicate)} · nierozpoznane {Count(Shared.Models.Db.FileDecisions.Unrecognized)} · " +
        $"błędy {Count(Shared.Models.Db.FileDecisions.Error)}{(Cancelled ? " · PRZERWANY" : "")}";
}
