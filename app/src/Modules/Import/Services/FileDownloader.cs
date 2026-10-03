using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using PzlEv.Modules.Import.Models;
using PzlEv.Shared.Utils.Files;
using Serilog;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Pobranie pliku lokalizacji na dysk lokalny przed przetwarzaniem (folder tymczasowy użytkownika, usuwany po pliku):
/// dalsze odczyty – nagłówek, wiersze, treść do bazy – idą z dysku, a SHA-256 jest liczony w trakcie kopiowania.
/// SharePoint: najpierw bezpośrednio przez HTTPS (konto Windows i ciasteczka bramy F5 z logowania w aplikacji) – z postępem
/// i bez limitu rozmiaru usługi WebClient; gdy się nie uda (np. brama odsyła do logowania) – kopia przez WebDAV (ścieżka
/// UNC), a kolejne pliki tej witryny w tym imporcie od razu przez WebDAV. Folder zwykły (Do_importu) – kopia pliku.
/// </summary>
public sealed class FileDownloader : IDisposable
{
    public const string Https = "HTTPS";
    public const string WebDav = "WebDAV";
    public const string Copy = "kopia pliku";

    private static readonly ILogger Logger = Log.ForContext("Module", "import");

    private readonly string _root;
    private readonly HttpMessageHandler? _handler;
    private readonly Func<Uri, string?> _cookies;
    private readonly HashSet<string> _httpsFailed = new(StringComparer.OrdinalIgnoreCase);
    private HttpClient? _client;

    /// <param name="root">Folder plików pobranych (domyślnie %TEMP%\PZL-EV\import).</param>
    /// <param name="handler">Obsługa HTTP (testy); domyślnie konto Windows, bez przekierowań.</param>
    /// <param name="cookies">Nagłówek Cookie dla adresu (testy); domyślnie ciasteczka WinINet.</param>
    public FileDownloader(string? root = null, HttpMessageHandler? handler = null, Func<Uri, string?>? cookies = null)
    {
        _root = root ?? Path.Combine(Path.GetTempPath(), "PZL-EV", "import");
        _handler = handler;
        _cookies = cookies ?? WinInetCookies.Get;
    }

    public void Dispose() => _client?.Dispose();

    /// <summary>Pozostałości importów przerwanych awarią (starsze niż 12 godzin) – usuwane na początku importu.</summary>
    public void CleanStale()
    {
        if (!Directory.Exists(_root))
            return;
        foreach (var folder in new DirectoryInfo(_root).EnumerateDirectories().Where(d => d.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-12)))
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warning("Nie usunięto pozostałości pobierania {Folder}: {Error}", folder.FullName, ex.Message);
            }
        }
    }

    /// <summary>
    /// Plik na dysku lokalnym; detail – postęp pobierania dla ekranu („35,2 z 86,0 MB (4,1 MB/s), HTTPS”), true – stan końcowy.
    /// </summary>
    public LocalFile Download(ImportLocation location, FileInfo file, Action<string, bool> detail, CancellationToken cancellation)
    {
        var folder = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, file.Name);
        var watch = Stopwatch.StartNew();
        try
        {
            if (!location.IsManualFolder && SharePointAddress.Parse(location.ConfiguredPath) is { } address && !_httpsFailed.Contains(address.Origin))
            {
                var url = new Uri(address.FolderUrl + Uri.EscapeDataString(file.Name));
                var failure = TryHttps(url, file, target, detail, cancellation, out var sha, out var size);
                if (failure is null)
                    return new LocalFile(folder, target, sha, size, Https, watch.Elapsed);
                _httpsFailed.Add(address.Origin);
                Logger.Warning("Pobieranie {File} przez HTTPS nieudane ({Failure}) – kopia przez WebDAV; kolejne pliki z {Host} w tym imporcie od razu przez WebDAV",
                    file.Name, failure, address.Host);
                if (File.Exists(target))
                    File.Delete(target);
            }

            var method = WebDavPath.IsWebDav(location.Path) ? WebDav : Copy;
            detail(method == WebDav ? "WebDAV: Windows pobiera cały plik przed odczytem…" : "kopiowanie…", true);
            using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            var (hash, copied) = CopyWithHash(source, target, file.Length, detail, method, cancellation);
            return new LocalFile(folder, target, hash, copied, method, watch.Elapsed);
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    /// <summary>Pobranie przez HTTPS; null – pobrano, inaczej powód niepowodzenia (wtedy kopia przez WebDAV).</summary>
    private string? TryHttps(Uri url, FileInfo file, string target, Action<string, bool> detail, CancellationToken cancellation, out string sha, out long size)
    {
        sha = "";
        size = 0;
        try
        {
            _client ??= new HttpClient(_handler ?? new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false, UseCookies = false },
                disposeHandler: _handler is null) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var cookies = _cookies(url);
            if (!string.IsNullOrEmpty(cookies))
                request.Headers.TryAddWithoutValidation("Cookie", cookies);
            detail($"HTTPS: łączenie z {url.Host}…", true);
            using var headers = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            headers.CancelAfter(TimeSpan.FromSeconds(60));
            using var response = _client.Send(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
            headers.CancelAfter(Timeout.Infinite);
            Logger.Information("Pobieranie {File} przez HTTPS: HTTP {Status}, {Type}, {Length} B, ciasteczka: {Cookies}", file.Name, (int)response.StatusCode,
                response.Content.Headers.ContentType?.MediaType ?? "-", response.Content.Headers.ContentLength?.ToString() ?? "?",
                string.IsNullOrEmpty(cookies) ? "brak" : $"{cookies.Split(';').Length}");
            if (response.StatusCode != HttpStatusCode.OK)
                return $"HTTP {(int)response.StatusCode}" + (response.Headers.Location is { } location ? $" → {location.GetLeftPart(UriPartial.Path)}" : "");
            if (response.Content.Headers.ContentType?.MediaType == "text/html")
                return "odpowiedź HTML (np. strona logowania bramy) zamiast pliku";
            using var body = response.Content.ReadAsStream(cancellation);
            (sha, size) = CopyWithHash(body, target, response.Content.Headers.ContentLength ?? file.Length, detail, Https, cancellation);
            if (response.Content.Headers.ContentLength is { } length && length != size)
                return $"pobrano {size} B z {length} B";
            if (size != file.Length)
                Logger.Information("Pobieranie {File} przez HTTPS: {Size} B, w lokalizacji {Listed} B (SharePoint może dopisywać właściwości dokumentu)",
                    file.Name, size, file.Length);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !cancellation.IsCancellationRequested)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Kopia strumienia do pliku z SHA-256 liczonym w trakcie i postępem (MB, MB/s) najwyżej co 0,25 s.</summary>
    private static (string Sha, long Size) CopyWithHash(Stream source, string target, long expected, Action<string, bool> detail, string method,
        CancellationToken cancellation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        var buffer = new byte[1 << 20];
        var watch = Stopwatch.StartNew();
        long done = 0, shown = 0;
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
            done += read;
            if (watch.ElapsedMilliseconds - shown >= 250)
            {
                shown = watch.ElapsedMilliseconds;
                detail(Progress(done, expected, watch.Elapsed, method), false);
            }
        }
        detail(Progress(done, expected, watch.Elapsed, method), true);
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), done);
    }

    private static string Progress(long done, long expected, TimeSpan elapsed, string method) =>
        $"{StageReporter.Megabytes(done)} z {StageReporter.Megabytes(Math.Max(expected, done))} MB" +
        (elapsed.TotalSeconds >= 0.5 ? $" ({StageReporter.Megabytes((long)(done / elapsed.TotalSeconds))} MB/s)" : "") + $", {method}";

    internal static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning("Nie usunięto pobranego pliku {Folder}: {Error}", folder, ex.Message);
        }
    }
}
