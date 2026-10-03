using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Import.Data;

/// <summary>
/// Magazyn importu – kontrakt przyszłych procedur meta.* / can.* (F10; w SQL dane kanoniczne ładowane
/// wsadowo do CAN_Row – SqlBulkCopy). Czyta definicje źródeł i lokalizacje zapisane przez Administrację (wspólne tabele).
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

    /// <summary>
    /// Zapis wersji pliku w jednej transakcji: META_SourceFile, treść pliku (GZip) i dane kanoniczne (strumieniowo do CAN_Row);
    /// po zapisie baza liczy wiersze i sumy – niezgodność z oczekiwanymi = CanonicalFlowException i nic nie zostaje zapisane.
    /// Null – treść (hash) zapisana w międzyczasie (np. przez inną osobę).
    /// </summary>
    StoredFile? StoreFile(SourceFileRow file, byte[] content, CanonicalData? canonical);

    void RecordSeen(SourceFileSeenRow seen);

    void FinishBatch(long batchId, DateTimeOffset at, int files, int imported, int skipped, int duplicates, int unrecognized, int errors, string status);

    IReadOnlyList<ImportBatchRow> Batches(int count);

    IReadOnlyList<SourceFileSeenRow> Seen(long batchId);

    SourceFileRow? File(long fileId);

    /// <summary>Wiersze pliku odczytane z zapisanej treści (oryginalny plik albo dawne wiersze surowe).</summary>
    IReadOnlyList<RawRowRecord> RawRows(long fileId);

    /// <summary>Dane kanoniczne pliku z CAN_Row (pole → wartość).</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> CanonicalRows(ParserRow parser, long fileId);
}
