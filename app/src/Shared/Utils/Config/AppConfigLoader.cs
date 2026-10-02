using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PzlEv.Shared.Utils.Config;

/// <summary>
/// Wczytuje pzl-ev.json. Układ: "Env" (TEST / PROD) i "Environments": { "TEST": { NetworkRoot, DataMode,
/// InMemoryStatePath, Sql: { Server, Database, Schema, TablePrefix, TrustServerCertificate } }, "PROD": {…} }.
/// Wcześniejszy układ płaski (Environment, NetworkRoot, DataMode, InMemoryStatePath) nadal działa.
/// Każde pole opcjonalne – brakujące dostają wartości domyślne; DataMode = Sql wymaga sekcji Sql.
/// </summary>
public static partial class AppConfigLoader
{
    public const string FileName = "pzl-ev.json";
    public const string DefaultTablePrefix = "PZLEV_";

    public static AppConfig Defaults()
    {
        var local = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "PZL-EV");
        return new AppConfig(
            Environment: "TEST",
            NetworkRoot: Path.Combine(local, "TEST-root"),
            DataMode: DataMode.InMemory,
            InMemoryStatePath: Path.Combine(local, "inmemory-state.json"),
            Sql: null);
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
            var env = (Text(root, "Env") ?? Text(root, "Environment") ?? config.Environment).ToUpperInvariant();
            var section = root;
            if (root.TryGetProperty("Environments", out var environments))
            {
                if (!environments.TryGetProperty(env, out section))
                    throw new InvalidOperationException($"{FileName}: Env = '{env}', brak sekcji Environments.{env}");
            }

            var modeText = Text(section, "DataMode");
            var mode = config.DataMode;
            if (modeText is not null && !Enum.TryParse(modeText, ignoreCase: true, out mode))
                throw new InvalidOperationException($"{FileName}: DataMode = '{modeText}' – dozwolone: InMemory, Sql");

            var sql = section.TryGetProperty("Sql", out var sqlSection) ? ReadSql(sqlSection, env) : null;
            if (mode == DataMode.Sql && sql is null)
                throw new InvalidOperationException($"{FileName}: DataMode = Sql wymaga sekcji Sql (Server, Database, Schema) w Environments.{env}");

            return config with
            {
                Environment = env,
                NetworkRoot = Text(section, "NetworkRoot") ?? config.NetworkRoot,
                DataMode = mode,
                InMemoryStatePath = Text(section, "InMemoryStatePath") ?? config.InMemoryStatePath,
                Sql = sql,
            };
        }
    }

    private static SqlSettings ReadSql(JsonElement section, string env)
    {
        var at = $"Environments.{env}.Sql";
        var connectionString = Text(section, "ConnectionString");
        var server = Text(section, "Server");
        var database = Text(section, "Database");
        if (connectionString is null && (server is null || database is null))
            throw new InvalidOperationException($"{FileName}: {at} – wymagane Server i Database (albo ConnectionString)");
        var schema = Text(section, "Schema") ?? throw new InvalidOperationException($"{FileName}: {at}.Schema – pole wymagane (np. FINOP)");
        var prefix = Text(section, "TablePrefix") ?? DefaultTablePrefix;
        // Schemat i sygnatura trafiają do nazw obiektów SQL – tylko litery, cyfry i _.
        if (!Identifier().IsMatch(schema))
            throw new InvalidOperationException($"{FileName}: {at}.Schema = '{schema}' – dozwolone litery, cyfry i _");
        if (!Identifier().IsMatch(prefix))
            throw new InvalidOperationException($"{FileName}: {at}.TablePrefix = '{prefix}' – dozwolone litery, cyfry i _");
        var trust = section.TryGetProperty("TrustServerCertificate", out var t) && t.ValueKind == JsonValueKind.True;
        return new SqlSettings(server ?? "", database ?? "", schema, prefix, trust, connectionString);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex Identifier();
}
