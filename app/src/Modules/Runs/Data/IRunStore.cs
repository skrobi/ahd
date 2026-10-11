using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Modules.Runs.Data;

/// <summary>Tydzień kalendarza okresów (dict.Calendar, globalny).</summary>
public sealed record CalendarWeek(int Year, int Week, string Period, DateTime DateFrom, DateTime DateTo, bool IsClosing);

/// <summary>Przebiegi w bazie: meta.Run, meta.RunPin, run.Balance, run.Issue (migracja 027).</summary>
public interface IRunStore
{
    /// <summary>Tydzień kalendarza okresów obejmujący dzień; null – brak w kalendarzu.</summary>
    CalendarWeek? Week(DateTime day);

    /// <summary>Przebieg projektu w tygodniu (zakładany przy pierwszym uruchomieniu) i nowe uruchomienie „w toku”.</summary>
    RunPinRow StartPin(string project, CalendarWeek week, DateTimeOffset at, string user, string machine);

    /// <summary>Dane ACTUALS projektu w chwili przypięcia (procedura RUN_PinBalance).</summary>
    IReadOnlyList<RunBalanceRow> PinBalance(string project, long pinId);

    IReadOnlyList<RunBalanceRow> Balance(long pinId);

    /// <summary>Ostatnie zakończone uruchomienie wcześniejszego przebiegu projektu (poprzedni tydzień z danymi); null – pierwszy przebieg.</summary>
    RunPinRow? Previous(string project, long runId);

    /// <summary>Cost elementy słownika Cost Category efektywnego dla projektu (globalne i zmiany projektu).</summary>
    IReadOnlySet<string> CostElements(string project);

    void Finish(long pinId, DateTimeOffset at, string status, long? importBatchId, long rows, decimal grossCost, decimal balance,
        IReadOnlyList<(string Check, CheckLevel Level, string? Element, string Message, decimal? Amount)> issues, string summary);

    /// <summary>Uruchomienia „w toku” bez działającego procesu → „przerwany”.</summary>
    void AbandonRunning(Func<long, bool> isAlive, DateTimeOffset at);

    /// <summary>Ostatnie uruchomienie przebiegu każdego projektu (lista projektów).</summary>
    IReadOnlyDictionary<string, RunPinRow> Latest();

    IReadOnlyList<RunPinRow> Pins(string project, int count);

    IReadOnlyList<RunIssueRow> Issues(long pinId);

    /// <summary>Definicje źródeł ze słownika projektu „Źródła danych”.</summary>
    IReadOnlySet<string> ProjectSources(string project);

    /// <summary>E-mail osoby ze słownika Osoby (USRID); null – brak.</summary>
    string? Email(string usrid);
}
