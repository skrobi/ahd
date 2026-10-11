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

    /// <summary>
    /// Importy „w toku”, które już nie trwają (isAlive – blokada importu wolna, np. po awarii aplikacji) → „przerwany”;
    /// pliki „w toku” takich importów usuwane z danymi kanonicznymi.
    /// </summary>
    void AbandonRunning(DateTimeOffset at, Func<long, bool> isAlive);

    /// <summary>Ostatnia rozstrzygnięta decyzja dla pliku (lokalizacja + nazwa), którego wersja (SHA-256) jest w bazie – do pominięcia po metadanych.</summary>
    SourceFileSeenRow? LastSettled(string location, string fileName);

    SourceFileRow? FindByHash(string sha256);

    /// <summary>
    /// Zapis wersji pliku: META_SourceFile („w toku”) i dane kanoniczne (strumieniowo do CAN_Row paczkami, wiersze czytane
    /// w trakcie zapisu), po kontroli – „utworzone”; treść pliku nie jest przechowywana. Błędy treści (canonical.Totals)
    /// albo niezgodność wierszy i sum w bazie (CanonicalFlowException) – wyjątek, wiersze i wpis pliku usunięte. stage – etap zapisu dla ekranu.
    /// Null – treść (hash) zapisana w międzyczasie (np. przez inną osobę).
    /// </summary>
    StoredFile? StoreFile(SourceFileRow file, CanonicalData? canonical, Action<string>? stage = null);

    void RecordSeen(SourceFileSeenRow seen);

    void FinishBatch(long batchId, DateTimeOffset at, int files, int imported, int skipped, int duplicates, int unrecognized, int errors, string status);

    IReadOnlyList<ImportBatchRow> Batches(int count);

    IReadOnlyList<SourceFileSeenRow> Seen(long batchId);

    SourceFileRow? File(long fileId);

    /// <summary>Dane kanoniczne pliku z CAN_Row (pole → wartość).</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> CanonicalRows(ParserRow parser, long fileId);
}
