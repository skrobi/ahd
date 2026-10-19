using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Modules.Runs.Data;

/// <summary>Tydzień kalendarza okresów (dict.Calendar, globalny).</summary>
public sealed record CalendarWeek(int Year, int Week, string Period, DateTime DateFrom, DateTime DateTo, bool IsClosing);

/// <summary>Wiersz słownika „Kontrole przebiegu” (dict.RunCheck): typ programu, reguła, wariant przebiegu, ustawienie.</summary>
public sealed record RunCheckRow(string ProjectType, string RuleName, string RunKind, bool IsActive, string? Level, decimal? Threshold);

/// <summary>WP programu ze słownika „WP i CAM”: MPK (null – brak) i czy ma budżet w „Harmonogram i budżet” (BAC, BAC HOURS albo BAC MATERIAL ≠ 0).</summary>
public sealed record RunWpRow(string Wp, string? Mpk, bool HasBudget);

/// <summary>Sumy budżetu WP programu w „Harmonogram i budżet” (kwoty w walucie wpisów, bez przeliczenia).</summary>
public sealed record RunBudgetTotals(decimal Bac, decimal BacHours, decimal BacMaterial);

/// <summary>
/// Słowniki kontroli P2 (docs/pipeline-fazy.md, P2): Cost Category efektywny dla programu (cost element → kategoria; null – pusta),
/// WP programu, MPK ze stawką w „Stawki wydziałów” na rok RateYear; Budget – sumy budżetu (miary reguł uzgodnień); CostKinds – rodzaj
/// kosztu cost elementu (Hours / Materials / Other; brak – Other) z Cost Category efektywnego; CostElementNames – opis cost elementu.
/// </summary>
public sealed record RunDictionaryData(IReadOnlyDictionary<string, string?> CostCategories, IReadOnlyList<RunWpRow> Wps,
    IReadOnlySet<string> RatedCostCenters, int RateYear, RunBudgetTotals? Budget = null, IReadOnlyDictionary<string, string>? CostKinds = null,
    IReadOnlyDictionary<string, string>? CostElementNames = null);

/// <summary>Linia źródłowa ACTUALS (okno „Zmiany ACWP”): plik, wiersz, data utworzenia, kwota pola kwoty uruchomienia, ilość, opisy.</summary>
public sealed record RunLineRow(string FileName, int RowNumber, string? CreatedOn, decimal? Amount, decimal? Quantity, string? CostElementName,
    string? CoObjectName, string? PartnerObject, string? PurchaseOrderNumber, string? InvoiceNumber, string? OriginalOrderNumber);

/// <summary>Linie źródłowe komórki: pierwsze linie wg |kwoty| oraz liczba i suma wszystkich.</summary>
public sealed record RunLines(IReadOnlyList<RunLineRow> Rows, int Count, decimal Sum);

/// <summary>Wiersz słownika „Reguły uzgodnień” (dict.RunReconciliation).</summary>
public sealed record RunReconciliationRow(string ProjectType, string Name, string RunKind, bool IsActive, string LeftFormula, string Comparison,
    string RightFormula, decimal? Tolerance, string Level);

/// <summary>Program (meta.Project): kod, nazwa, typ.</summary>
public sealed record RunProjectRow(string Code, string Name, string ProjectType);

/// <summary>Wiersz słownika „Wykluczenia” programu (puste pole – dowolna wartość).</summary>
public sealed record RunExclusionRow(string? CostElement, string? WbsElement, string? PartnerObject)
{
    /// <summary>Wiersz danych przypiętych objęty wykluczeniem bez partnera (partnera nie ma w danych przypiętych).</summary>
    public bool Covers(RunBalanceRow row) => PartnerObject is null
        && (CostElement is null || string.Equals(CostElement, row.CostElement, StringComparison.OrdinalIgnoreCase))
        && (WbsElement is null || string.Equals(WbsElement, row.WbsElement, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Przebiegi w bazie: meta.Run, meta.RunPin, run.Balance, run.Issue (migracja 027).</summary>
public interface IRunStore
{
    /// <summary>Tydzień kalendarza okresów obejmujący dzień; null – brak w kalendarzu.</summary>
    CalendarWeek? Week(DateTime day);

    /// <summary>Przebieg projektu w tygodniu (zakładany przy pierwszym uruchomieniu) i nowe uruchomienie „w toku”.</summary>
    RunPinRow StartPin(string project, CalendarWeek week, DateTimeOffset at, string user, string machine);

    /// <summary>Dane ACTUALS projektu w chwili przypięcia (procedura RUN_PinBalance); amountField – pole kwoty parsera ACTUALS.</summary>
    IReadOnlyList<RunBalanceRow> PinBalance(string project, long pinId, string amountField);

    IReadOnlyList<RunBalanceRow> Balance(long pinId);

    /// <summary>Ostatnie zakończone uruchomienie wcześniejszego przebiegu projektu (poprzedni tydzień z danymi); null – pierwszy przebieg.</summary>
    RunPinRow? Previous(string project, long runId);

    /// <summary>Cost elementy słownika Cost Category efektywnego dla projektu (globalne i zmiany projektu).</summary>
    IReadOnlySet<string> CostElements(string project);

    /// <summary>Uruchomienia „w toku” bez działającego procesu → „przerwany”.</summary>
    void AbandonRunning(Func<long, bool> isAlive, DateTimeOffset at);

    /// <summary>Ostatnie uruchomienie przebiegu każdego projektu (lista projektów).</summary>
    IReadOnlyDictionary<string, RunPinRow> Latest();

    IReadOnlyList<RunPinRow> Pins(string project, int count);

    IReadOnlyList<RunIssueRow> Issues(long pinId);

    /// <summary>Definicje źródeł ze słownika projektu „Źródła danych”.</summary>
    IReadOnlySet<string> ProjectSources(string project);

    /// <summary>Słowniki kontroli spójności (P2): Cost Category efektywny, WP i CAM z budżetem, MPK ze stawką na rok year.</summary>
    RunDictionaryData Dictionaries(string project, int year);

    /// <summary>
    /// Linie ACTUALS elementu WBS × cost element × rok × okres z najnowszych wersji plików zaimportowanych do asOf (stan uruchomienia),
    /// kwota z pola amountField; pierwsze top linii wg |kwoty|.
    /// </summary>
    RunLines ActualsLines(string wbsElement, string costElement, int year, int period, DateTimeOffset asOf, string amountField, int top);

    /// <summary>Słownik globalny „Reguły uzgodnień” – bieżące wiersze.</summary>
    IReadOnlyList<RunReconciliationRow> Reconciliations();

    /// <summary>Reguły walidacji ACTUALS (słownik „Reguły walidacji”); pusty słownik – reguły startowe (RunValidationRules.Starters).</summary>
    IReadOnlyList<RunValidationRule> ValidationRules();

    /// <summary>Wykluczenia programu (słownik „Wykluczenia”): cost element, element WBS, partner – puste: dowolny.</summary>
    IReadOnlyList<RunExclusionRow> Exclusions(string project);

    /// <summary>Uruchomienie po identyfikatorze; null – brak.</summary>
    RunPinRow? Pin(long pinId);

    /// <summary>Tydzień przebiegu (meta.Run) – okres i czy zamyka okres.</summary>
    CalendarWeek RunWeek(long runId);

    /// <summary>Słowniki definicji przebiegów: definicje, etapy, elementy kontrolne etapów (bieżące wiersze).</summary>
    (IReadOnlyList<RunDefinitionHeaderRow> Headers, IReadOnlyList<RunStepRowData> Steps, IReadOnlyList<RunStepElementRow> Elements) DefinitionRows();

    /// <summary>Definicja przebiegu użyta w uruchomieniu (JSON – RunWorkflowEngine.Serialize) i etapy uruchomienia (pierwszy – do wykonania).</summary>
    void StartSteps(long pinId, string definitionJson, IReadOnlyList<RunStepDefinition> steps);

    string? DefinitionOf(long pinId);

    IReadOnlyList<RunStepRow> Steps(long pinId);

    /// <summary>Stan etapu: status, chwile, kto, liczby wyników, komentarz (null – bez zmiany komentarza).</summary>
    void SetStep(long pinId, int stepNo, string status, DateTimeOffset? startedAt, DateTimeOffset? finishedAt, string? actedBy, int errors, int warnings,
        string? comment = null);

    /// <summary>Wyniki kontroli etapu (poprzednie wyniki etapu usuwane – ponowne wykonanie).</summary>
    void ReplaceStepIssues(long pinId, int stepNo, IReadOnlyList<(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount)> issues);

    /// <summary>Stan uruchomienia: status, zakończenie, podsumowanie; liczby błędów i ostrzeżeń – z wyników kontroli.</summary>
    void SetPinState(long pinId, string status, DateTimeOffset? finishedAt, string summary);

    /// <summary>Import źródeł uruchomienia (etap Import źródeł).</summary>
    void SetPinImport(long pinId, long? importBatchId);

    /// <summary>Zestawienie przypiętych danych (etap Przypięcie stanu): wiersze, koszt brutto, saldo okresów zamkniętych.</summary>
    void SetPinBalance(long pinId, long rows, decimal grossCost, decimal balance);

    /// <summary>Aktywne programy (ekran Przebiegi).</summary>
    IReadOnlyList<RunProjectRow> Projects();

    /// <summary>Typ projektu (META_Project.ProjectType); null – brak projektu.</summary>
    string? ProjectType(string project);

    /// <summary>Słownik globalny „Kontrole przebiegu” – bieżące wiersze.</summary>
    IReadOnlyList<RunCheckRow> CheckSettings();

    /// <summary>Pole kwoty ACTUALS projektu – słownik „Parametry projektu” (brak – ValueObjCrcy).</summary>
    string ActualsAmount(string project);

    /// <summary>Przebieg wybrany w programie (Parametry programu → „Przebieg”); null – szablon domyślny dla typu.</summary>
    string? AssignedDefinition(string project);

    /// <summary>Przebiegi wybrane w programach: program → szablon.</summary>
    IReadOnlyDictionary<string, string> DefinitionAssignments();
}
