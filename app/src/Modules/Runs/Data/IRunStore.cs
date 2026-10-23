using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;

namespace PzlEv.Modules.Runs.Data;

/// <summary>Tydzień kalendarza okresów (dict.Calendar, globalny).</summary>
public sealed record CalendarWeek(int Year, int Week, string Period, DateTime DateFrom, DateTime DateTo, bool IsClosing);

/// <summary>
/// Słowniki programu dla danych przebiegu: rodzaj kosztu cost elementu (Hours / Materials / Other; brak – Other) z Cost Category
/// efektywnego (wiersz programu przed globalnym), opis cost elementu.
/// </summary>
public sealed record RunDictionaryData(IReadOnlyDictionary<string, string> CostKinds, IReadOnlyDictionary<string, string> CostElementNames);

/// <summary>Wiersz danych uruchomienia jak w strukturze programu (RUN_BalanceEv – tabela „ev” reguł walidacji).</summary>
public sealed record RunEvRow(string WbsElement, string CostElement, int FiscalYear, int Period, string CostKind, decimal Amount);

/// <summary>
/// Zakres zrzutu linii ACTUALS (wynik reguły – kolumny wiersza; węzeł okna „Zmiany ACWP”): element WBS albo elementy WBS (oba null –
/// elementy WBS danych przypiętych w uruchomieniu PinId), cost elementy (null – wszystkie), rok i okres (null – wszystkie; okres 12
/// obejmuje 13–16).
/// </summary>
public sealed record RunActualsScope(long PinId, string? WbsElement, IReadOnlyCollection<string>? CostElements, int? Year, int? Period,
    IReadOnlyCollection<string>? WbsElements = null);

/// <summary>
/// Linia ACTUALS zrzutu: element WBS, cost element, rok, okres i pola linii źródłowej; waluta i wartość w walucie transakcji, materiał
/// – pola klucza linii okna „Zmiany ACWP” (AcwpChanges.Lines).
/// </summary>
public sealed record RunActualsLine(string? WbsElement, string? CostElement, int? FiscalYear, int? Period, string FileName, int RowNumber, string? CreatedOn,
    decimal? Amount, decimal? Quantity, string? CostElementName, string? CoObjectName, string? PartnerObject, string? PurchaseOrderNumber, string? InvoiceNumber,
    string? OriginalOrderNumber, string? TransactionCurrency, decimal? ValueTranCurr, string? OriginalMaterial);

/// <summary>Zrzut linii ACTUALS: pierwsze linie wg |kwoty| oraz liczba i suma wszystkich.</summary>
public sealed record RunActualsLines(IReadOnlyList<RunActualsLine> Rows, int Count, decimal Sum);

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

    /// <summary>Pliki ACTUALS z wierszami programu (Project definition nakładki) – najnowsze wersje zaimportowane do asOf (informacja przypięcia).</summary>
    IReadOnlyList<RunPinFile> PinFiles(string project, DateTimeOffset asOf);

    /// <summary>Słowniki używane w przebiegu zmienione po chwili since (wiersze programu i globalne): słownik, liczba wierszy, ostatnia zmiana.</summary>
    IReadOnlyList<RunDictionaryChange> DictionaryChanges(string project, DateTimeOffset since);

    IReadOnlyList<RunBalanceRow> Balance(long pinId);

    /// <summary>Ostatnie zakończone uruchomienie wcześniejszego przebiegu projektu (poprzedni tydzień z danymi); null – pierwszy przebieg.</summary>
    RunPinRow? Previous(string project, long runId);

    /// <summary>Uruchomienia „w toku” bez działającego procesu → „przerwany”.</summary>
    void AbandonRunning(Func<long, bool> isAlive, DateTimeOffset at);

    /// <summary>Ostatnie uruchomienie przebiegu każdego projektu (lista projektów).</summary>
    IReadOnlyDictionary<string, RunPinRow> Latest();

    IReadOnlyList<RunPinRow> Pins(string project, int count);

    /// <summary>Zamknięcia okresów programu (RunPinRow.IsPeriodClose) – wszystkie, od najnowszego; porównania „od zamknięcia do zamknięcia”.</summary>
    IReadOnlyList<RunPinRow> ClosingPins(string project);

    IReadOnlyList<RunIssueRow> Issues(long pinId);

    /// <summary>Definicje źródeł ze słownika projektu „Źródła danych”.</summary>
    IReadOnlySet<string> ProjectSources(string project);

    /// <summary>Słowniki programu: rodzaj kosztu i opis cost elementu (Cost Category efektywny).</summary>
    RunDictionaryData Dictionaries(string project);

    /// <summary>
    /// Linie ACTUALS zakresu (wynik reguły – „Linie ACTUALS”) z najnowszych wersji plików zaimportowanych do asOf, kwota z pola amountField;
    /// pierwsze top linii wg |kwoty|, liczba i suma wszystkich.
    /// </summary>
    RunActualsLines ActualsLines(RunActualsScope scope, DateTimeOffset asOf, string amountField, int top);

    /// <summary>Reguły walidacji (słownik „Reguły walidacji”); pusty słownik – reguły startowe (RunRules.Starters).</summary>
    IReadOnlyList<RunRule> Rules();

    /// <summary>Dane uruchomienia jak w strukturze (tabela „ev” reguł) – zastępuje zapisane wcześniej.</summary>
    void SaveEv(long pinId, IReadOnlyList<RunEvRow> rows);

    /// <summary>Wynik łączenia źródeł uruchomienia (RUN_CostAssignment, migracja 059) – zastępuje zapisany wcześniej.</summary>
    void SaveCostAssignment(long pinId, IReadOnlyList<(CostTarget Target, decimal Amount)> rows);

    /// <summary>Wynik łączenia źródeł uruchomienia: element WBS, WP, CAM, Cost Category, powód braku, koszt.</summary>
    IReadOnlyList<(CostTarget Target, decimal Amount)> CostAssignment(long pinId);

    /// <summary>Uruchomienie ma zapisane dane „ev” (uruchomienia sprzed migracji 051 – nie).</summary>
    bool HasEv(long pinId);

    /// <summary>
    /// Zapytanie reguły walidacji na danych uruchomienia (tabele biezace, poprzednie, ev … i parametry – RunRules.Tables): kontrola
    /// zapytania (RunRules.Check), transakcja zawsze wycofywana, limit czasu RunRules.TimeoutSeconds, najwyżej RunRules.MaxRows wierszy;
    /// błąd SQL Server – w wyniku (Error, z linią zapytania).
    /// </summary>
    RunRuleResult RunRule(string sql, RunRuleContext context);

    /// <summary>Wykluczenia programu (słownik „Wykluczenia”): cost element, element WBS, partner – puste: dowolny.</summary>
    IReadOnlyList<RunExclusionRow> Exclusions(string project);

    /// <summary>Uruchomienie po identyfikatorze; null – brak.</summary>
    RunPinRow? Pin(long pinId);

    /// <summary>Zamrożone uruchomienie przebiegu projektu w tygodniu (klocek „Zamknięcie okresu”); null – przebieg niezamrożony albo brak.</summary>
    RunPinRow? FrozenIn(string project, int year, int week);

    /// <summary>Zamrożenie przebiegu (meta.Run, migracja 058): uruchomienie, kto i kiedy; pinId null – „Otwórz zamknięcie”.</summary>
    void SetFrozen(long runId, long? pinId, DateTimeOffset? at, string? user);

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

    /// <summary>Wyniki kontroli etapu (poprzednie wyniki etapu usuwane – ponowne wykonanie); Details – wiersz wyniku reguły (JSON).</summary>
    void ReplaceStepIssues(long pinId, int stepNo, IReadOnlyList<(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount, string? Details)> issues);

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

    /// <summary>Pole kwoty ACTUALS projektu – słownik „Parametry projektu” (brak – ValueObjCrcy).</summary>
    string ActualsAmount(string project);

    /// <summary>Przebieg wybrany w programie (Parametry programu → „Przebieg”); null – szablon domyślny dla typu.</summary>
    string? AssignedDefinition(string project);

    /// <summary>Przebiegi wybrane w programach: program → szablon.</summary>
    IReadOnlyDictionary<string, string> DefinitionAssignments();
}
