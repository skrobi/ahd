using PzlEv.Shared.Utils.Config;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Usługi wspólne przekazywane modułom (konfiguracja, czas, użytkownik, dziennik, problemy, dane, blokady operacji).
/// Tworzone raz przy starcie aplikacji; moduł wybiera na ich podstawie swój magazyn danych.
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
    /// <summary>Usługi trybu w pamięci; tryb Sql będzie dostępny po F10.</summary>
    public static AppServices Create(AppConfig config, string appVersion, IClock? clock = null, ICurrentUser? user = null)
    {
        if (config.DataMode != DataMode.InMemory)
            throw new NotSupportedException("DataMode = Sql – tryb dostępny po przejściu na MS SQL (tasks/F10). Ustaw DataMode = InMemory w pzl-ev.json.");

        clock ??= new SystemClock();
        user ??= new WindowsUser();
        var db = new InMemoryDatabase(config.InMemoryStatePath);
        db.Load();
        return new AppServices(config, clock, user, db, new InMemoryJournal(db, clock, user), new InMemoryProblemLog(db, clock), appVersion,
            new FileOperationLock(config.RabitFolder, clock, user));
    }
}
