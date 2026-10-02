using PzlEv.Shared.Utils.Config;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Usługi wspólne przekazywane modułom (konfiguracja, czas, użytkownik, dziennik, problemy, dane, blokady operacji).
/// Tworzone raz przy starcie aplikacji; moduł wybiera na ich podstawie swój magazyn danych: Sql (baza MS SQL
/// środowiska) albo – gdy null – magazyn w pamięci (Database).
/// </summary>
public sealed record AppServices(
    AppConfig Config,
    IClock Clock,
    ICurrentUser User,
    InMemoryDatabase Database,
    IJournal Journal,
    IProblemLog Problems,
    string AppVersion,
    IOperationLock Locks)
{
    /// <summary>Baza MS SQL środowiska (DataMode = Sql); null – dane w pamięci (Database).</summary>
    public SqlDatabase? Sql { get; init; }

    /// <summary>Opis miejsca danych do stopki i Diagnostyki.</summary>
    public string DataDescription => Sql is null ? "Dane w pamięci (tryb przejściowy)" : $"Baza: {Sql.Describe}";

    public static AppServices Create(AppConfig config, string appVersion, IClock? clock = null, ICurrentUser? user = null)
    {
        clock ??= new SystemClock();
        user ??= new WindowsUser();
        var locks = new FileOperationLock(config.RabitFolder, clock, user);
        if (config.DataMode == DataMode.Sql)
        {
            var sql = new SqlDatabase(config.Sql ?? throw new InvalidOperationException("DataMode = Sql – brak sekcji Sql w pzl-ev.json"), appVersion);
            return new AppServices(config, clock, user, new InMemoryDatabase(), new SqlJournal(sql, clock, user), new SqlProblemLog(sql, clock), appVersion, locks)
            {
                Sql = sql,
            };
        }

        var db = new InMemoryDatabase(config.InMemoryStatePath);
        db.Load();
        return new AppServices(config, clock, user, db, new InMemoryJournal(db, clock, user), new InMemoryProblemLog(db, clock), appVersion, locks);
    }
}
