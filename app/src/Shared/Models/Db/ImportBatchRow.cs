namespace PzlEv.Shared.Models.Db;

/// <summary>Statusy importu w historii (meta.ImportBatch.Status).</summary>
public static class ImportBatchStatus
{
    public const string Running = "w toku";
    public const string Abandoned = "przerwany (brak zakończenia)";
}

/// <summary>Uruchomienie importu – meta.ImportBatch: kto, komputer, wersja aplikacji, liczniki decyzji, status.</summary>
public sealed record ImportBatchRow(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string User,
    string Machine,
    string AppVersion,
    int Files,
    int Imported,
    int Skipped,
    int Duplicates,
    int Unrecognized,
    int Errors,
    string Status);
