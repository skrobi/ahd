using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// Dane kanoniczne w stałej tabeli CAN_Row (migracja 007): wiersz = plik, numer wiersza, parser (ParserId, wersja) i sloty
/// pól parsera (CanonicalSlots). Zapytania budowane z przydziału slotów parsera – nazwy pól jako aliasy. Zapis parsera
/// nie zmienia tabel.
/// </summary>
public static partial class SqlCanonical
{
    public static readonly IReadOnlyList<string> FixedColumns = ["FileId", "RowNumber", "ParserId", "ParserVersion"];

    /// <summary>Nazwa pola (alias w zapytaniach): litera, potem litery, cyfry, _.</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    public static partial Regex FieldName();

    /// <summary>Nazwa danych parsera (TableName – nazwa logiczna, np. dla widoku): litery, cyfry, _.</summary>
    [GeneratedRegex("^[A-Za-z0-9_]{1,40}$")]
    public static partial Regex TableName();

    /// <summary>Kolumny zapisu wsadowego: stałe + sloty pól (w tej kolejności wartości wiersza).</summary>
    public static IReadOnlyList<(string Name, Type Type)> BulkColumns(IReadOnlyList<ParserField> fields) =>
        [("FileId", typeof(long)), ("RowNumber", typeof(int)), ("ParserId", typeof(long)), ("ParserVersion", typeof(int)),
            .. fields.Select(f => (Slot(f), FieldTypes.ClrType(f.Type)))];

    /// <summary>Slot pola – pole bez slotu (parser sprzed migracji 007) jest błędem konfiguracji.</summary>
    public static string Slot(ParserField field) =>
        CanonicalSlots.IsSlot(field.Slot) ? field.Slot! : throw new InvalidOperationException($"Pole {field.Field} nie ma slotu w CAN_Row – zapisz parser ponownie");

    /// <summary>Slot pola parsera o danej nazwie – brak pola = komunikat (np. mapowanie wymaga pola WbsElement parsera ACTUALS).</summary>
    public static string Slot(ParserRow parser, string field) =>
        parser.Fields.FirstOrDefault(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase)) is { } found
            ? Slot(found)
            : throw new InvalidOperationException($"Parser {parser.Code} nie ma pola {field}");

    /// <summary>Slot pola albo NULL, gdy parser takiego pola (już) nie ma – zapytania z polami opcjonalnymi.</summary>
    public static string SlotOrNull(ParserRow parser, string field) =>
        parser.Fields.FirstOrDefault(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase) && CanonicalSlots.IsSlot(f.Slot))?.Slot ?? "NULL";

    /// <summary>Lista „slot AS [pole]” wszystkich pól parsera ze slotem.</summary>
    public static string SelectList(ParserRow parser) =>
        string.Join(", ", parser.Fields.Where(f => CanonicalSlots.IsSlot(f.Slot)).Select(f => $"{f.Slot} AS [{f.Field}]"));

    /// <summary>Bieżąca wersja parsera o kodzie (także nieaktywna) – null, gdy parsera nie ma.</summary>
    public static ParserRow? CurrentParser(SqlConnection connection, SqlDatabase db, string code) =>
        connection.QuerySingleOrDefault<SqlParserRow>(
            $"SELECT {SqlParserRow.Columns} FROM {db.Table(DbTables.Parser)} WHERE SupersededAt IS NULL AND Code = @code", new { code })?.ToRow();

    /// <summary>Wiersze danych kanonicznych pliku (pole → wartość) – podgląd i testy.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(SqlDatabase db, ParserRow parser, long fileId)
    {
        using var connection = db.Open();
        var fields = SelectList(parser);
        return connection.Query(
                $"SELECT FileId, RowNumber, ParserVersion{(fields.Length > 0 ? ", " + fields : "")} FROM {db.Table(DbTables.CanonicalRow)} " +
                "WHERE FileId = @fileId AND ParserId = @parserId ORDER BY RowNumber",
                new { fileId, parserId = parser.ParserId })
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>((IDictionary<string, object?>)r, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
