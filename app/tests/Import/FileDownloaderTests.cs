using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using PzlEv.Modules.Import.Models;
using PzlEv.Modules.Import.Services;
using Xunit;

namespace PzlEv.Tests.Import;

/// <summary>Pobranie pliku na dysk lokalny przed przetwarzaniem: HTTPS dla SharePoint, WebDAV / kopia pliku jako zapas.</summary>
public sealed class FileDownloaderTests : IDisposable
{
    private const string SiteFolder = "https://sp.example.com/sites/Rabbit/Shared%20Documents/E1";
    private readonly string _root = Directory.CreateTempSubdirectory("pzl-ev-download-").FullName;
    private readonly byte[] _content = Encoding.UTF8.GetBytes("Kod;Kwota\nA1;1 254,51\n");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Plik w folderze lokalizacji (w aplikacji – ścieżka WebDAV UNC).</summary>
    private FileInfo Source(string name)
    {
        var folder = Path.Combine(_root, "lokalizacja");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, _content);
        return new FileInfo(path);
    }

    private ImportLocation SharePoint(FileInfo file) => new("RABIT", file.DirectoryName!, false, SiteFolder);

    private static string Sha(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>Odpowiedź HTTP ustalona w teście; zapisuje zapytania (adres, ciasteczka).</summary>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Url, string? Cookie)> Requests { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.TryGetValues("Cookie", out var cookie) ? cookie.Single() : null));
            return respond(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    [Fact]
    public void Manual_folder_file_is_copied_with_hash_and_removed_after_use()
    {
        var file = Source("ACTUALS_PAF_01.csv");
        var details = new List<(string Text, bool Final)>();
        using var downloader = new FileDownloader(Path.Combine(_root, "pobrane"));

        string folder;
        using (var local = downloader.Download(new ImportLocation("Do_importu", file.DirectoryName!, true, file.DirectoryName!), file,
                   (text, final) => details.Add((text, final)), CancellationToken.None))
        {
            Assert.Equal((FileDownloader.Copy, Sha(_content), (long)_content.Length), (local.Method, local.Sha256, local.Size));
            Assert.Equal(_content, File.ReadAllBytes(local.Path));
            Assert.Equal("ACTUALS_PAF_01.csv", Path.GetFileName(local.Path));
            folder = local.Folder;
        }

        Assert.False(Directory.Exists(folder));   // plik tymczasowy usunięty
        Assert.Contains(details, d => d.Final && d.Text.EndsWith(" MB, kopia pliku"));
    }

    [Fact]
    public void SharePoint_file_is_downloaded_over_https_with_browser_cookies()
    {
        var file = Source("ACTUALS PAF 01.csv");
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_content) });
        using var downloader = new FileDownloader(Path.Combine(_root, "pobrane"), handler, _ => "MRHSession=abc; FedAuth=xyz");

        using var local = downloader.Download(SharePoint(file), file, (_, _) => { }, CancellationToken.None);

        Assert.Equal((FileDownloader.Https, Sha(_content)), (local.Method, local.Sha256));
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{SiteFolder}/ACTUALS%20PAF%2001.csv", request.Url.AbsoluteUri);
        Assert.Equal("MRHSession=abc; FedAuth=xyz", request.Cookie);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found, null)]                 // brama odsyła do logowania
    [InlineData(HttpStatusCode.OK, "text/html")]             // strona logowania zamiast pliku
    [InlineData(HttpStatusCode.Unauthorized, null)]
    public void Failed_https_download_falls_back_to_webdav_copy_for_the_rest_of_the_import(HttpStatusCode status, string? type)
    {
        var first = Source("ACTUALS_PAF_01.csv");
        var second = Source("ACTUALS_PAF_02.csv");
        var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("<html>logowanie</html>") };
            if (type is not null)
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            return response;
        });
        using var downloader = new FileDownloader(Path.Combine(_root, "pobrane"), handler, _ => null);

        using var local = downloader.Download(SharePoint(first), first, (_, _) => { }, CancellationToken.None);
        using var next = downloader.Download(SharePoint(second), second, (_, _) => { }, CancellationToken.None);

        Assert.Equal((FileDownloader.Copy, Sha(_content)), (local.Method, local.Sha256));   // w aplikacji ścieżka UNC – WebDAV
        Assert.Equal(_content, File.ReadAllBytes(local.Path));
        Assert.Equal(FileDownloader.Copy, next.Method);
        Assert.Single(handler.Requests);   // kolejny plik tej witryny od razu przez WebDAV
    }

    [Fact]
    public void Cancelled_download_leaves_no_file()
    {
        var file = Source("ACTUALS_PAF_01.csv");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        using var downloader = new FileDownloader(Path.Combine(_root, "pobrane"));

        Assert.ThrowsAny<OperationCanceledException>(() =>
            downloader.Download(new ImportLocation("Do_importu", file.DirectoryName!, true, file.DirectoryName!), file, (_, _) => { }, cancel.Token));

        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "pobrane")));
    }
}
