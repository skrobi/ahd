using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PzlEv.Shared.Utils.Config;

/// <summary>
/// Wczytuje pzl-ev.json obok PZL-EV.exe (wzór z opisem każdego ustawienia – app/pzl-ev.json, kopiowany przy budowie).
/// Układ: "Env" (TEST / PROD) i "Environments": { "TEST": { NetworkRoot, Sql: { Server, Database, Schema,
/// TablePrefix, TrustServerCertificate }, PzlProd: { Server, Database, Schema, TrustServerCertificate } }, "PROD": {…} }.
/// Plik jest wymagany; brak pliku albo pola = błąd z opisem przy starcie (bez cichych wartości domyślnych). Sekcja
/// PzlProd jest opcjonalna – bez niej ekran mapowania pokazuje, czego brakuje. Ścieżki mogą zawierać zmienne, np. %LOCALAPPDATA%.
/// </summary>
public static partial class AppConfigLoader
{
    public const string FileName = "pzl-ev.json";
    public const string DefaultTablePrefix = "PZLEV_";

    /// <summary>Wczytuje konfigurację z folderu aplikacji; błąd pliku = wyjątek z czytelnym opisem.</summary>
    public static AppConfig Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Brak pliku konfiguracji {path}. Skopiuj wzór app\\{FileName} (z opisem ustawień) obok PZL-EV.exe i uzupełnij.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{path}: niepoprawny JSON – {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var env = Text(root, "Env")?.ToUpperInvariant() ?? throw new InvalidOperationException($"{path}: brak pola Env (TEST albo PROD)");
            if (!root.TryGetProperty("Environments", out var environments) || !environments.TryGetProperty(env, out var section))
                throw new InvalidOperationException($"{path}: Env = '{env}', brak sekcji Environments.{env}");

            var at = $"Environments.{env}";
            var networkRoot = Text(section, "NetworkRoot") ?? throw new InvalidOperationException($"{path}: {at}.NetworkRoot – pole wymagane");
            if (!section.TryGetProperty("Sql", out var sql))
                throw new InvalidOperationException($"{path}: {at}.Sql – sekcja wymagana (Server, Database, Schema)");

            var pzlProd = section.TryGetProperty("PzlProd", out var prod) ? ReadSql(prod, $"{path}: {at}.PzlProd", tablePrefix: false) : null;
            return new AppConfig(env, System.Environment.ExpandEnvironmentVariables(networkRoot), ReadSql(sql, $"{path}: {at}.Sql"), pzlProd);
        }
    }

    /// <summary>Sekcja połączenia; tablePrefix: false – tabele bez sygnatury (PZLPROD: [LOG].[WBS]).</summary>
    private static SqlSettings ReadSql(JsonElement section, string at, bool tablePrefix = true)
    {
        var connectionString = Text(section, "ConnectionString");
        var server = Text(section, "Server");
        var database = Text(section, "Database");
        if (connectionString is null && (server is null || database is null))
            throw new InvalidOperationException($"{at} – wymagane Server i Database");
        var schema = Text(section, "Schema") ?? throw new InvalidOperationException($"{at}.Schema – pole wymagane (np. FINOP)");
        var prefix = tablePrefix ? Text(section, "TablePrefix") ?? DefaultTablePrefix : "";
        // Schemat i sygnatura trafiają do nazw obiektów SQL – tylko litery, cyfry i _.
        if (!Identifier().IsMatch(schema))
            throw new InvalidOperationException($"{at}.Schema = '{schema}' – dozwolone litery, cyfry i _");
        if (tablePrefix && !Identifier().IsMatch(prefix))
            throw new InvalidOperationException($"{at}.TablePrefix = '{prefix}' – dozwolone litery, cyfry i _");
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
