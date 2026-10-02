using System.IO;
using System.Text.Json;

namespace PzlEv.Shared.Utils.Data;

/// <summary>
/// Blokada w pliku na dysku sieciowym: plik „operacja.lock” otwarty na wyłączność przez cały czas operacji
/// (system plików zwalnia go także po awarii aplikacji) oraz „operacja.lock.json” z opisem, kto ją trzyma.
/// </summary>
public sealed class FileOperationLock(string folder, IClock clock, ICurrentUser user) : IOperationLock
{
    public IDisposable? TryAcquire(string operation, out LockHolder? holder)
    {
        Directory.CreateDirectory(folder);
        var path = LockPath(operation);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            holder = ReadInfo(operation) ?? new LockHolder("nieznany użytkownik", "?", clock.Now);
            return null;
        }
        holder = null;
        File.WriteAllText(InfoPath(operation), JsonSerializer.Serialize(new LockHolder(user.Account, Environment.MachineName, clock.Now)));
        return new Lease(stream, path, InfoPath(operation));
    }

    public LockHolder? Holder(string operation)
    {
        var path = LockPath(operation);
        if (!File.Exists(path))
            return null;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return null;   // plik bez właściciela (np. po awarii) – blokada wolna
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ReadInfo(operation) ?? new LockHolder("nieznany użytkownik", "?", clock.Now);
        }
    }

    private string LockPath(string operation) => Path.Combine(folder, $"{operation}.lock");

    private string InfoPath(string operation) => Path.Combine(folder, $"{operation}.lock.json");

    private LockHolder? ReadInfo(string operation)
    {
        try
        {
            return JsonSerializer.Deserialize<LockHolder>(File.ReadAllText(InfoPath(operation)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private sealed class Lease(FileStream stream, string path, string infoPath) : IDisposable
    {
        public void Dispose()
        {
            TryDelete(infoPath);
            stream.Dispose();
            TryDelete(path);
        }

        private static void TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // inna osoba założyła już blokadę – plik należy do niej
            }
        }
    }
}
