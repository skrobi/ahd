using PzlEv.Shared.Models.Pipeline;

namespace PzlEv.Shared.Models.Db;

/// <summary>Stan uruchomienia przebiegu (META_RunPin.Status).</summary>
public static class RunStatuses
{
    public const string Running = "w toku";
    public const string Ok = "zakończony";
    public const string Warnings = "zakończony z ostrzeżeniami";
    public const string Errors = "zakończony z błędami";
    public const string Failed = "przerwany błędem";
    public const string Abandoned = "przerwany (brak zakończenia)";

    /// <summary>Przebieg etapowy wstrzymany na bramce: WARNING do potwierdzenia albo zatwierdzenie ręczne.</summary>
    public const string NeedsAction = "wymaga akcji";

    /// <summary>Przebieg etapowy zatrzymany: etap z ERROR – po poprawie „Wykonaj ponownie”.</summary>
    public const string Blocked = "zatrzymany – błąd";
}

/// <summary>Etap uruchomienia przebiegu – meta.RunStep (status, kto, kiedy, wyniki, potwierdzenie z komentarzem).</summary>
public sealed record RunStepRow(long PinId, int StepNo, string Name, string Action, string Status, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    string? ActedBy, int Errors, int Warnings, string? Comment);

/// <summary>Uruchomienie (przypięcie stanu) przebiegu projektu – meta.Run + meta.RunPin.</summary>
public sealed record RunPinRow(
    long PinId,
    long RunId,
    string Code,
    string Project,
    int PinNo,
    DateTimeOffset StartedAt,
    string StartedBy,
    DateTimeOffset? FinishedAt,
    string Status,
    long? ImportBatchId,
    long? Rows,
    decimal? GrossCost,
    decimal? Balance,
    int Errors,
    int Warnings,
    string? Summary,
    string? AmountField)
{
    /// <summary>Opis na listę projektów: kod, numer uruchomienia, stan.</summary>
    public string Label => $"{Code} #{PinNo} – {Status}";
}

/// <summary>Wynik kontroli przebiegu – run.Issue.</summary>
public sealed record RunIssueRow(long Id, long PinId, string CheckName, CheckLevel Level, string? Element, string Message, decimal? Amount, int? StepNo = null);

/// <summary>
/// Dane ACTUALS projektu w chwili przypięcia (run.Balance): element WBS × cost element × rok × okres × rozliczenie
/// wychodzące × waluta obiektu – liczba wierszy, kwota (waluta obiektu), najpóźniejsza data utworzenia.
/// </summary>
public sealed record RunBalanceRow(
    string WbsElement,
    string CostElement,
    int FiscalYear,
    int Period,
    bool IsSettlement,
    string Currency,
    int Rows,
    decimal Amount,
    DateTime? LastCreatedOn);
