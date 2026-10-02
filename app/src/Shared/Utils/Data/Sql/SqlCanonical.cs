using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// Tabele danych kanonicznych parserów (can.&lt;Tabela&gt; → CAN_&lt;Tabela&gt;): kolumny stałe (FileId, RowNumber, ParserVersion)
/// i kolumny pól parsera z typem. Zapis parsera zakłada tabelę albo dokłada brakujące kolumny; kolumn nie usuwa ani nie
/// zmienia ich typu (dane już zapisane zostają), tekst może tylko wydłużyć. Wymaga prawa tworzenia i zmiany tabel w schemacie.
/// </summary>
public static partial class SqlCanonical
{
    public static readonly IReadOnlyList<string> FixedColumns = ["FileId", "RowNumber", "ParserVersion"];

    /// <summary>Nazwa pola (kolumny w bazie): litera, potem litery, cyfry, _.</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    public static partial Regex FieldName();

    /// <summary>Nazwa tabeli parsera (po CAN_): litery, cyfry, _.</summary>
    [GeneratedRegex("^[A-Za-z0-9_]{1,40}$")]
    public static partial Regex TableName();

    public static string SqlType(ParserField field) => field.Type switch
    {
        FieldTypes.Decimal => "DECIMAL(28,8)",
        FieldTypes.Integer => "INT",
        FieldTypes.Date => "DATE",
        _ => $"NVARCHAR({field.Length ?? FieldTypes.DefaultTextLength})",
    };

    /// <summary>Kolumny zapisu wsadowego: stałe + pola (w tej kolejności wartości wiersza).</summary>
    public static IReadOnlyList<(string Name, Type Type)> BulkColumns(IReadOnlyList<ParserField> fields) =>
        [("FileId", typeof(long)), ("RowNumber", typeof(int)), ("ParserVersion", typeof(int)), .. fields.Select(f => (f.Field, FieldTypes.ClrType(f.Type)))];

    /// <summary>
    /// Zakłada tabelę parsera albo dokłada brakujące kolumny pól (i wydłuża tekst); zwraca opis zmian. Niezgodny typ
    /// istniejącej kolumny albo niedozwolona nazwa – InvalidOperationException z komunikatem dla użytkownika.
    /// </summary>
    public static IReadOnlyList<string> EnsureTable(SqlDatabase db, SqlConnection connection, SqlTransaction transaction, ParserRow parser)
    {
        if (!TableName().IsMatch(parser.Table))
            throw new InvalidOperationException($"Tabela parsera „{parser.Table}”: dozwolone litery, cyfry i _ (do 40 znaków).");
        foreach (var field in parser.Fields)
        {
            if (!FieldName().IsMatch(field.Field) || FixedColumns.Contains(field.Field, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Pole „{field.Field}”: nazwa niedozwolona w bazie.");
        }

        var table = db.Table(parser.LogicalTable);
        var existing = connection.Query<ColumnInfo>(
                """
                SELECT c.name AS Name, t.name AS TypeName, c.max_length AS MaxLength
                FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
                WHERE c.object_id = OBJECT_ID(@table)
                """,
                new { table }, transaction)
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        if (existing.Count == 0)
        {
            var physical = $"{db.Settings.TablePrefix}CAN_{parser.Table}";
            var columns = string.Concat(parser.Fields.Select(f => $"    [{f.Field}] {SqlType(f)} NULL,\n"));
            connection.Execute(
                $"""
                CREATE TABLE {table} (
                    FileId BIGINT NOT NULL CONSTRAINT [FK_{physical}_File] REFERENCES {db.Table(DbTables.SourceFile)} (FileId),
                    RowNumber INT NOT NULL,
                    ParserVersion INT NOT NULL,
                {columns}    CONSTRAINT [PK_{physical}] PRIMARY KEY (FileId, RowNumber, ParserVersion) WITH (DATA_COMPRESSION = PAGE)
                )
                """,
                transaction: transaction);
            return [$"utworzona tabela {table} ({parser.Fields.Count} pól)"];
        }

        var changes = new List<string>();
        foreach (var field in parser.Fields)
        {
            if (!existing.TryGetValue(field.Field, out var column))
            {
                connection.Execute($"ALTER TABLE {table} ADD [{field.Field}] {SqlType(field)} NULL", transaction: transaction);
                changes.Add($"nowa kolumna {field.Field} ({FieldTypes.Label(field.Type)})");
                continue;
            }
            var family = Family(column.TypeName);
            if (family != field.Type)
                throw new InvalidOperationException(
                    $"Pole {field.Field}: kolumna w bazie ma typ {column.TypeName} – zmiana na „{FieldTypes.Label(field.Type)}” niemożliwa (dane już zapisane); dodaj nowe pole.");
            if (family != FieldTypes.Text || column.MaxLength == -1)
                continue;
            var unicode = column.TypeName.StartsWith('n');
            var length = unicode ? column.MaxLength / 2 : column.MaxLength;
            var wanted = field.Length ?? FieldTypes.DefaultTextLength;
            if (wanted <= length)
                continue;
            connection.Execute($"ALTER TABLE {table} ALTER COLUMN [{field.Field}] {(unicode ? "NVARCHAR" : "VARCHAR")}({wanted}) NULL", transaction: transaction);
            changes.Add($"kolumna {field.Field} wydłużona do {wanted} znaków");
        }
        return changes;
    }

    /// <summary>Wiersze danych kanonicznych pliku (pole → wartość) – podgląd i testy.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(SqlDatabase db, string logicalTable, long fileId)
    {
        using var connection = db.Open();
        return connection.Query($"SELECT * FROM {db.Table(logicalTable)} WHERE FileId = @fileId ORDER BY RowNumber", new { fileId })
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>((IDictionary<string, object?>)r, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static string Family(string typeName) => typeName.ToLowerInvariant() switch
    {
        "nvarchar" or "varchar" or "nchar" or "char" => FieldTypes.Text,
        "decimal" or "numeric" or "money" or "float" or "real" => FieldTypes.Decimal,
        "int" or "bigint" or "smallint" or "tinyint" => FieldTypes.Integer,
        "date" or "datetime" or "datetime2" => FieldTypes.Date,
        _ => typeName,
    };

    private sealed class ColumnInfo
    {
        public string Name { get; set; } = "";
        public string TypeName { get; set; } = "";
        public short MaxLength { get; set; }
    }
}
