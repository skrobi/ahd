using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Import.Data;

/// <summary>
/// Magazyn importu – kontrakt przyszłych procedur meta.* / stg.* / can.* (F10; w SQL wiersze surowe ładowane
/// wsadowo – SqlBulkCopy). Czyta definicje źródeł i lokalizacje zapisane przez Administrację (wspólne tabele).
/// </summary>
public interface IImportStore
{
    IReadOnlyList<SourceDefinitionRow> ActiveDefinitions();

    /// <summary>Bieżąca wersja aktywnego parsera (pola i tabela danych kanonicznych); null – brak albo nieaktywny.</summary>
    ParserRow? ActiveParser(string code);

    IReadOnlyList<SourceLocationRow> ActiveLocations();

    long BeginBatch(DateTimeOffset at, string user, string machine, string appVersion);

    /// <summary>Importy „w toku” bez zakończenia (np. po awarii aplikacji) → „przerwany”; wywoływane po założeniu blokady.</summary>
    void AbandonRunning(DateTimeOffset at);

    /// <summary>Ostatnia rozstrzygnięta decyzja dla pliku (lokalizacja + nazwa), którego treść jest w bazie – do pominięcia po metadanych.</summary>
    SourceFileSeenRow? LastSettled(string location, string fileName);

    SourceFileRow? FindByHash(string sha256);

    /// <summary>Rejestruje wersję pliku z wierszami surowymi; null – treść (hash) zarejestrowana w międzyczasie.</summary>
    long? RegisterFile(SourceFileRow file, IReadOnlyList<string?[]> rows);

    /// <summary>Zapisuje dane kanoniczne w tabeli parsera (albo tylko status pliku, gdy data = null).</summary>
    void CompleteCanonical(long fileId, string status, CanonicalData? data, int? parserVersion);

    void RecordSeen(SourceFileSeenRow seen);

    void FinishBatch(long batchId, DateTimeOffset at, int files, int imported, int skipped, int duplicates, int unrecognized, int errors, string status);

    IReadOnlyList<ImportBatchRow> Batches(int count);

    IReadOnlyList<SourceFileSeenRow> Seen(long batchId);

    SourceFileRow? File(long fileId);

    IReadOnlyList<RawRowRecord> RawRows(long fileId);

    /// <summary>Dane kanoniczne pliku z tabeli parsera (pole → wartość).</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> CanonicalRows(ParserRow parser, long fileId);
}
