using System.IO;
using System.Text.Json;

namespace PzlEv.Shared.Utils.Config;

/// <summary>Wczytuje pzl-ev.json; każde pole opcjonalne – brakujące dostają wartości domyślne.</summary>
public static class AppConfigLoader
{
    public const string FileName = "pzl-ev.json";

    public static AppConfig Defaults()
    {
        var local = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "PZL-EV");
        return new AppConfig(
            Environment: "TEST",
            NetworkRoot: Path.Combine(local, "TEST-root"),
            DataMode: DataMode.InMemory,
            InMemoryStatePath: Path.Combine(local, "inmemory-state.json"),
            ConnectionString: null);
    }

    /// <summary>Wczytuje konfigurację z folderu aplikacji; błąd pliku = wyjątek z czytelnym opisem.</summary>
    public static AppConfig Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        var config = Defaults();
        if (!File.Exists(path))
            return config;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{FileName}: niepoprawny JSON – {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            string? Text(string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                    ? v.GetString()!.Trim()
                    : null;

            var modeText = Text("DataMode");
            var mode = config.DataMode;
            if (modeText is not null && !Enum.TryParse(modeText, ignoreCase: true, out mode))
                throw new InvalidOperationException($"{FileName}: DataMode = '{modeText}' – dozwolone: InMemory, Sql");

            return config with
            {
                Environment = Text("Environment") ?? config.Environment,
                NetworkRoot = Text("NetworkRoot") ?? config.NetworkRoot,
                DataMode = mode,
                InMemoryStatePath = Text("InMemoryStatePath") ?? config.InMemoryStatePath,
                ConnectionString = Text("ConnectionString"),
            };
        }
    }
}
