using System.IO;
using System.Text.RegularExpressions;
using Dapper;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// Migracje schematu: skrypty sql/mssql/NNN_*.sql wbudowane w aplikację (te same uruchamia się sqlcmd z -v Schema=…
/// Prefix=…). Każdy skrypt jest idempotentny i sam zapisuje swoją wersję w META_SchemaVersion; aplikacja wykonuje
/// brakujące po kolei, każdy w jednej transakcji.
/// </summary>
public static partial class SqlMigrations
{
    private const string ResourcePrefix = "PzlEv.Sql.";

    public sealed record Script(int Number, string Name, string Text);

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

    /// <summary>Wykonuje brakujące skrypty; zwraca nazwy wykonanych.</summary>
    public static IReadOnlyList<string> Apply(SqlDatabase db)
    {
        var current = CurrentVersion(db);
        var applied = new List<string>();
        foreach (var script in All().Where(s => s.Number > current))
        {
            db.InTransaction((connection, transaction) =>
            {
                foreach (var batch in Batches(script.Text, db.Settings.Schema, db.Settings.TablePrefix))
                    connection.Execute(batch, transaction: transaction, commandTimeout: 300);
                return 0;
            });
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
}
