using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Import.Data;

/// <summary>
/// Magazyn importu – kontrakt przyszłych procedur meta.* / stg.* / can.* (F10; w SQL wiersze surowe ładowane
/// wsadowo – SqlBulkCopy). Czyta definicje źródeł i lokalizacje zapisane przez Administrację (wspólne tabele).
/// </summary>
public interface IImportStore
{
    IReadOnlyList<SourceDefinitionRow> ActiveDefinitions();

    IReadOnlyList<SourceLocationRow> ActiveLocations();

    long BeginBatch(DateTimeOffset at, string user, string machine, string appVersion);

    /// <summary>Ostatnia rozstrzygnięta decyzja dla pliku (lokalizacja + nazwa) – do pominięcia po metadanych.</summary>
    SourceFileSeenRow? LastSettled(string location, string fileName);

    SourceFileRow? FindByHash(string sha256);

    /// <summary>Rejestruje wersję pliku z wierszami surowymi; null – treść (hash) zarejestrowana w międzyczasie.</summary>
    long? RegisterFile(SourceFileRow file, IReadOnlyList<string?[]> rows);

    /// <summary>Zapisuje dane kanoniczne (albo tylko status, gdy nie powstały).</summary>
    void CompleteCanonical(long fileId, string status, IReadOnlyList<ActualsRow> rows, int? parserVersion);

    void RecordSeen(SourceFileSeenRow seen);

    void FinishBatch(long batchId, DateTimeOffset at, int files, int imported, int skipped, int duplicates, int unrecognized, int errors, string status);

    IReadOnlyList<ImportBatchRow> Batches(int count);

    IReadOnlyList<SourceFileSeenRow> Seen(long batchId);

    SourceFileRow? File(long fileId);

    IReadOnlyList<RawRowRecord> RawRows(long fileId);

    IReadOnlyList<ActualsRow> Actuals(long fileId);
}
