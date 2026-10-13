namespace PzlEv.Shared.Models.Db;

/// <summary>Statusy importu w historii (meta.ImportBatch.Status).</summary>
/// <summary>Stan danych kanonicznych wersji pliku (META_SourceFile.CanonicalStatus); inne wartości – powód braku danych.</summary>
public static class CanonicalStatuses
{
    /// <summary>Dane zapisane i sprawdzone – widoczne dla odczytów (CAN_LatestFiles).</summary>
    public const string Created = "utworzone";

    /// <summary>Zapis wierszy trwa – plik pomijany przez odczyty; po awarii usuwany.</summary>
    public const string InProgress = "w toku";
}

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
    string Status)
{
    public const string ProblemReferencePrefix = "import:";

    /// <summary>Odwołanie problemów (meta.Problem.Reference) zgłoszonych w danym imporcie.</summary>
    public static string ProblemReference(long batchId) => $"{ProblemReferencePrefix}{batchId}";
}
