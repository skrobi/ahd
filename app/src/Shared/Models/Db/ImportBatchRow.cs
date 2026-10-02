namespace PzlEv.Shared.Models.Db;

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
