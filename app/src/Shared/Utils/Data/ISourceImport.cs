using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Import wybranych źródeł – kontrakt modułu Import dla innych modułów (np. „Importuj źródła projektu”). Rejestruje go
/// moduł Import (AppServices.Contracts); pliki zapisują się raz, wspólnie dla wszystkich projektów.
/// </summary>
public interface ISourceImport
{
    /// <summary>Aktywne definicje źródeł.</summary>
    IReadOnlyList<SourceDefinitionRow> Definitions();

    /// <summary>Logowanie do SharePoint lokalizacji RABIT (okno, wątek UI); false – anulowane.</summary>
    Task<bool> SignIn();

    /// <summary>Import plików podanych definicji źródeł (pozostałe pliki pomijane); progress – postęp do pokazania.</summary>
    SourceImportResult Import(IReadOnlySet<string> sources, IProgress<string>? progress, CancellationToken cancellation);
}

/// <summary>Wynik importu źródeł: liczba plików zaimportowanych, z błędem, opis i problemy.</summary>
public sealed record SourceImportResult(long BatchId, int Imported, int Errors, string Summary, IReadOnlyList<Issue> Issues);
