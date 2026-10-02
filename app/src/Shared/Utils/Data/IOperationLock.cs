namespace PzlEv.Shared.Utils.Data;

/// <summary>Kto trzyma blokadę operacji: konto, komputer, od kiedy.</summary>
public sealed record LockHolder(string User, string Machine, DateTimeOffset Since)
{
    public string Text => $"{User} ({Machine}) od {Since.ToLocalTime():yyyy-MM-dd HH:mm}";
}

/// <summary>
/// Blokada operacji wspólnej dla wszystkich użytkowników (np. import) – druga osoba widzi, kto ją wykonuje
/// (docs/pipeline-fazy.md, rozdz. 1.3). Tryb w pamięci: plik blokady na dysku sieciowym (FileOperationLock);
/// po przejściu na MS SQL (F10): sp_getapplock.
/// </summary>
public interface IOperationLock
{
    /// <summary>Zakłada blokadę; null – zajęta, <paramref name="holder"/> mówi przez kogo.</summary>
    IDisposable? TryAcquire(string operation, out LockHolder? holder);

    /// <summary>Kto teraz trzyma blokadę; null – wolna.</summary>
    LockHolder? Holder(string operation);
}
