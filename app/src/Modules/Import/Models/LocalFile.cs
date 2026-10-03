using PzlEv.Modules.Import.Services;

namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Plik pobrany na dysk lokalny do przetwarzania: ścieżka, SHA-256 i rozmiar pobranej treści, sposób pobrania
/// (HTTPS / WebDAV / kopia pliku) i czas. Zamknięcie usuwa plik z folderu tymczasowego.
/// </summary>
public sealed record LocalFile(string Folder, string Path, string Sha256, long Size, string Method, TimeSpan Elapsed) : IDisposable
{
    public void Dispose() => FileDownloader.TryDelete(Folder);
}
