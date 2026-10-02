using System.IO;

namespace PzlEv.Shared.Utils.Files;

/// <summary>
/// Pliki i podfoldery jednego folderu (bez zagłębiania). Obejście błędu .NET dla ścieżek WebDAV (usługa WebClient,
/// \\host@SSL\DavWWWRoot\…): nazwy zwracane z końcowym znakiem '\0' oraz wpisy „.” i „..”
/// (dotnet/runtime#62429, #46723). Nazwa jest czyszczona, a plik odczytywany ponownie po poprawnej ścieżce.
/// </summary>
public static class FolderEntries
{
    public static string CleanName(string name) => name.TrimEnd('\0');

    public static (List<FileInfo> Files, List<string> Folders) List(string path)
    {
        var dir = new DirectoryInfo(path);
        var files = new List<FileInfo>();
        foreach (var file in dir.EnumerateFiles())
        {
            var name = CleanName(file.Name);
            if (name.Length == file.Name.Length)
            {
                files.Add(file);
                continue;
            }
            var clean = new FileInfo(Path.Combine(dir.FullName, name));
            if (clean.Exists)
                files.Add(clean);
        }
        var folders = dir.EnumerateDirectories()
            .Select(d => CleanName(d.Name))
            .Where(n => n is not ("." or ".."))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(), folders);
    }
}
