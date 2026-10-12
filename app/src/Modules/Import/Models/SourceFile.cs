using System.IO;

namespace PzlEv.Modules.Import.Models;

/// <summary>
/// Plik lokalizacji do importu: nazwa, rozmiar, data zmiany (UTC) i ścieżka (UNC / dysk – kopia przez WebDAV albo
/// z folderu). Z listy folderu (FileInfo) albo z listy SharePoint czytanej przez HTTPS (SharePointListing).
/// </summary>
public sealed record SourceFile(string Name, long Length, DateTime LastWriteTimeUtc, string FullName)
{
    public string Extension => Path.GetExtension(Name);

    public DateTime LastWriteTime => LastWriteTimeUtc.ToLocalTime();

    public static SourceFile From(FileInfo file) => new(file.Name, file.Length, file.LastWriteTimeUtc, file.FullName);
}
