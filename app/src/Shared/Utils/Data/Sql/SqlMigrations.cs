using System.IO;
using System.Text.RegularExpressions;
using Dapper;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// Migracje bazy: skrypty sql/mssql/NNN_*.sql wbudowane w aplikację (te same uruchamia się sqlcmd z -v Schema=…
/// Prefix=…). Każdy skrypt jest idempotentny i sam zapisuje swój numer w META_SchemaVersion; aplikacja wykonuje
/// po kolei skrypty, których numeru nie ma w bazie (wykonane są pomijane), każdy w jednej transakcji pod blokadą
/// sp_getapplock – dwie osoby uruchamiające migrację jednocześnie nie wykonają skryptu dwa razy.
/// Skrypty NNN_dane_*.sql to dane startowe (presety); testy mogą je pominąć.
/// </summary>
public static partial class SqlMigrations
{
    private const string ResourcePrefix = "PzlEv.Sql.";

    public sealed record Script(int Number, string Name, string Text)
    {
        /// <summary>Dane startowe (presety), nie tabele.</summary>
        public bool IsPresets => Name.Contains("_dane_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Skrypt i jego wykonanie w bazie (null – do wykonania).</summary>
    public sealed record ScriptStatus(Script Script, DateTimeOffset? AppliedAt, string? AppliedBy);

    public static IReadOnlyList<Script> All()
    {
        var assembly = typeof(SqlMigrations).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(n =>
            {
                var name = n[ResourcePrefix.Length..];
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return new Script(int.Parse(name[..3], System.Globalization.CultureInfo.InvariantCulture), name, reader.ReadToEnd());
            })
            .OrderBy(s => s.Number)
            .ToList();
    }

    /// <summary>Wersja schematu wymagana przez tę wersję aplikacji.</summary>
    public static int Required => All().Max(s => s.Number);

    /// <summary>Wersja schematu w bazie; 0 – brak tabel PZL-EV.</summary>
    public static int CurrentVersion(SqlDatabase db)
    {
        using var connection = db.Open();
        var table = db.Table("meta.SchemaVersion");
        return connection.ExecuteScalar<int>($"IF OBJECT_ID(N'{table}') IS NULL SELECT 0 ELSE SELECT ISNULL(MAX(Version), 0) FROM {table}");
    }

    /// <summary>Wszystkie skrypty aplikacji z informacją, kiedy i kto je wykonał.</summary>
    public static IReadOnlyList<ScriptStatus> Status(SqlDatabase db)
    {
        using var connection = db.Open();
        var table = db.Table("meta.SchemaVersion");
        var applied = connection.Query<AppliedRow>(
                $"""
                IF OBJECT_ID(N'{table}') IS NULL SELECT CAST(0 AS INT) AS Version, SYSDATETIMEOFFSET() AS AppliedAt, N'' AS DbLogin WHERE 1 = 0
                ELSE SELECT Version, AppliedAt, DbLogin FROM {table}
                """)
            .ToDictionary(r => r.Version);
        return All().Select(s => applied.TryGetValue(s.Number, out var r) ? new ScriptStatus(s, r.AppliedAt, r.DbLogin) : new ScriptStatus(s, null, null))
            .ToList();
    }

    /// <summary>Skrypty do wykonania (ich numeru nie ma w bazie).</summary>
    public static IReadOnlyList<Script> Pending(SqlDatabase db) => Status(db).Where(s => s.AppliedAt is null).Select(s => s.Script).ToList();

    /// <summary>Wykonuje skrypty, których nie ma w bazie (presets: false – bez danych startowych); zwraca nazwy wykonanych.</summary>
    public static IReadOnlyList<string> Apply(SqlDatabase db, bool presets = true)
    {
        var table = db.Table("meta.SchemaVersion");
        var applied = new List<string>();
        foreach (var script in Pending(db).Where(s => presets || !s.IsPresets))
        {
            var executed = db.InTransaction((connection, transaction) =>
            {
                connection.Execute(
                    """
                    DECLARE @result INT;
                    EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 120000;
                    IF @result < 0 THROW 51000, N'Migracja bazy jest właśnie wykonywana przez inną osobę – spróbuj za chwilę.', 1;
                    """,
                    new { resource = $"PZL-EV migracje {db.Settings.Schema}.{db.Settings.TablePrefix}" }, transaction);
                var done = connection.ExecuteScalar<int>(
                    $"IF OBJECT_ID(N'{table}') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM {table} WHERE Version = @number",
                    new { number = script.Number }, transaction);
                if (done > 0)
                    return false;   // wykonany w międzyczasie przez inną osobę
                foreach (var batch in Batches(script.Text, db.Settings.Schema, db.Settings.TablePrefix))
                    connection.Execute(batch, transaction: transaction, commandTimeout: 300);
                return true;
            });
            if (executed)
                applied.Add(script.Name);
        }
        return applied;
    }

    /// <summary>Partie skryptu (rozdzielone liniami GO) ze zmiennymi sqlcmd $(Schema) i $(Prefix).</summary>
    public static IEnumerable<string> Batches(string text, string schema, string prefix) =>
        GoLine().Split(text.Replace("$(Schema)", schema, StringComparison.Ordinal).Replace("$(Prefix)", prefix, StringComparison.Ordinal))
            .Where(b => b.Trim().Length > 0);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();

    private sealed class AppliedRow
    {
        public int Version { get; set; }
        public DateTimeOffset AppliedAt { get; set; }
        public string DbLogin { get; set; } = "";
    }
}
