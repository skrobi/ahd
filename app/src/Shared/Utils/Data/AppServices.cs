using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Usługi wspólne przekazywane modułom (konfiguracja, czas, użytkownik, baza MS SQL środowiska, dziennik, problemy,
/// blokady operacji, baza PZLPROD – tylko odczyt, null gdy nieskonfigurowana). Tworzone raz przy starcie aplikacji;
/// moduły tworzą na ich podstawie swoje magazyny SQL.
/// </summary>
public sealed record AppServices(
    AppConfig Config,
    IClock Clock,
    ICurrentUser User,
    SqlDatabase Sql,
    IJournal Journal,
    IProblemLog Problems,
    string AppVersion,
    IOperationLock Locks,
    SqlDatabase? PzlProd = null)
{
    /// <summary>Opis bazy do stopki i Diagnostyki.</summary>
    public string DataDescription => $"Baza: {Sql.Describe}";

    public static AppServices Create(AppConfig config, string appVersion, IClock? clock = null, ICurrentUser? user = null)
    {
        clock ??= new SystemClock();
        user ??= new WindowsUser();
        var sql = new SqlDatabase(config.Sql, appVersion);
        return new AppServices(config, clock, user, sql, new SqlJournal(sql, clock, user), new SqlProblemLog(sql, clock, user), appVersion,
            new FileOperationLock(config.RabitFolder, clock, user), config.PzlProd is { } prod ? new SqlDatabase(prod, appVersion) : null);
    }
}
