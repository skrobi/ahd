using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.MasterData.Models;
using PzlEv.Modules.MasterData.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.MasterData.Data;

/// <summary>
/// Słowniki w bazie: osobna tabela z typowanymi kolumnami na każdy słownik (DICT_Calendar, DICT_FxRate…), wspólne
/// kolumny historii (RowId, Version, kto / kiedy zapisał i zastąpił). Ten sam kontrakt co wersja w pamięci:
/// zapis jest jedną transakcją; zmieniony w międzyczasie wiersz albo zajęty klucz = konflikt, nic nie zapisano.
/// Wartości słownika (zapis kanoniczny, ValueFormat) ↔ typy kolumn według opisu słownika.
/// </summary>
public sealed class SqlDictionaryStore(SqlDatabase db, IClock clock, ICurrentUser user) : IDictionaryStore
{
    /// <summary>Tabela i kolumny słownika: nazwa kolumny w opisie słownika → kolumna tabeli.</summary>
    private sealed record Map(string Table, IReadOnlyList<(string Spec, string Column)> Columns);

    private static readonly Dictionary<string, Map> Maps = new()
    {
        [GlobalDictionaries.Calendar] = new("dict.Calendar",
            [("Rok", "Year"), ("Tydzień", "Week"), ("Okres", "Period"), ("Od", "DateFrom"), ("Do", "DateTo"), ("Zamykający", "IsClosing")]),
        [GlobalDictionaries.DepartmentRates] = new("dict.DepartmentRate",
            [("Department", "Department"), ("Year", "Year"), ("Labor Rate", "LaborRate"), ("Overhead", "Overhead")]),
        [GlobalDictionaries.FxRates] = new("dict.FxRate", [("Waluta", "Currency"), ("Okres", "Period"), ("Kurs", "Rate")]),
        [GlobalDictionaries.CostCategory] = new("dict.CostCategory",
            [("Numer elementu kosztowego", "CostElement"), ("Opis", "Description"), ("Obszar", "Area"), ("Cost Category", "CostCategory")]),
        [GlobalDictionaries.Persons] = new("dict.Person", [("Konto AD", "AdAccount"), ("Imię i nazwisko", "FullName")]),
    };

    private const string ProjectFilter = "(Project = @project OR (Project IS NULL AND @project IS NULL))";

    public IReadOnlyList<DictionaryEntryRow> Current(string dictionary, string? project = null)
    {
        using var connection = db.Open();
        return Read(connection, null, dictionary, $"WHERE {ProjectFilter} AND SupersededAt IS NULL ORDER BY RowId", new { project });
    }

    public IReadOnlyList<DictionaryEntryRow> AsOf(string dictionary, DateTimeOffset moment, string? project = null)
    {
        using var connection = db.Open();
        return Read(connection, null, dictionary,
            $"WHERE {ProjectFilter} AND RecordedAt <= @moment AND (SupersededAt IS NULL OR SupersededAt > @moment) ORDER BY RowId", new { project, moment });
    }

    public IReadOnlyList<DictionaryEntryRow> History(long rowId)
    {
        // RowId pochodzi z jednej sekwencji dla wszystkich słowników – wiersz jest w dokładnie jednej tabeli.
        using var connection = db.Open();
        foreach (var dictionary in Maps.Keys)
        {
            var rows = Read(connection, null, dictionary, "WHERE RowId = @rowId ORDER BY Version", new { rowId });
            if (rows.Count > 0)
                return rows;
        }
        return [];
    }

    public StoreResult Save(string dictionary, string? project, IReadOnlyList<RowChange> changes)
    {
        var map = MapOf(dictionary);
        var spec = GlobalDictionaries.Get(dictionary);
        var table = db.Table(map.Table);
        try
        {
            return db.InTransaction((connection, transaction) =>
            {
                // 1. Sprawdzenie (bieżące wiersze zablokowane do końca transakcji): wersje i unikalność kluczy po zapisie.
                var current = Read(connection, transaction, dictionary, $"WITH (UPDLOCK, HOLDLOCK) WHERE {ProjectFilter} AND SupersededAt IS NULL", new { project })
                    .ToDictionary(r => r.RowId);
                foreach (var change in changes.Where(c => c.Kind != RowChangeKind.Added))
                {
                    if (change.RowId is not { } id || !current.TryGetValue(id, out var existing))
                        return StoreResult.Rejected($"Wiersz {change.Key} został w międzyczasie usunięty – odśwież dane.");
                    if (existing.Version != change.ExpectedVersion)
                        return StoreResult.Rejected($"Wiersz {change.Key} zmienił {existing.RecordedBy} ({existing.RecordedAt:yyyy-MM-dd HH:mm}) – odśwież dane.");
                }
                var duplicate = current.Values
                    .Where(r => !changes.Any(c => c.Kind != RowChangeKind.Added && c.RowId == r.RowId))
                    .Select(r => r.Key)
                    .Concat(changes.Where(c => c.Kind != RowChangeKind.Removed).Select(c => c.Key))
                    .GroupBy(k => k)
                    .FirstOrDefault(g => g.Count() > 1);
                if (duplicate is not null)
                    return StoreResult.Rejected($"Klucz {duplicate.Key} już istnieje w słowniku – odśwież dane.");

                // 2. Zapis: najpierw zamknięcie wersji (zwalnia klucze), potem nowe wersje.
                var now = clock.Now;
                foreach (var change in changes.Where(c => c.Kind != RowChangeKind.Added))
                {
                    connection.Execute($"UPDATE {table} SET SupersededAt = @now, SupersededBy = @user WHERE RowId = @rowId AND SupersededAt IS NULL",
                        new { now, user = user.Account, rowId = change.RowId }, transaction);
                }
                var columns = string.Join(", ", map.Columns.Select(c => c.Column));
                var values = string.Join(", ", map.Columns.Select((_, i) => $"@v{i}"));
                foreach (var change in changes.Where(c => c.Kind != RowChangeKind.Removed))
                {
                    var parameters = new DynamicParameters(new
                    {
                        rowId = change.Kind == RowChangeKind.Added ? db.NextLogicalId(connection, transaction) : change.RowId!.Value,
                        version = change.Kind == RowChangeKind.Added ? 1 : current[change.RowId!.Value].Version + 1,
                        project, now, user = user.Account,
                    });
                    for (var i = 0; i < map.Columns.Count; i++)
                    {
                        var column = spec.Column(map.Columns[i].Spec)!;
                        parameters.Add($"v{i}", ToDb(column, change.Values?.GetValueOrDefault(column.Name)));
                    }
                    connection.Execute(
                        $"INSERT INTO {table} (RowId, Version, Project, {columns}, RecordedAt, RecordedBy) VALUES (@rowId, @version, @project, {values}, @now, @user)",
                        parameters, transaction);
                }
                return new StoreResult(true, null,
                    changes.Count(c => c.Kind == RowChangeKind.Added), changes.Count(c => c.Kind == RowChangeKind.Updated), changes.Count(c => c.Kind == RowChangeKind.Removed));
            });
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return StoreResult.Rejected("Klucz już istnieje w słowniku (zapis innej osoby w międzyczasie) – odśwież dane.");
        }
    }

    private List<DictionaryEntryRow> Read(SqlConnection connection, SqlTransaction? transaction, string dictionary, string where, object parameters)
    {
        var map = MapOf(dictionary);
        var spec = GlobalDictionaries.Get(dictionary);
        var columns = string.Join(", ", map.Columns.Select(c => c.Column));
        var sql = $"SELECT Id, RowId, Version, Project, RecordedAt, RecordedBy, SupersededAt, SupersededBy, {columns} FROM {db.Table(map.Table)} {where}";
        var result = new List<DictionaryEntryRow>();
        using var reader = (System.Data.Common.DbDataReader)connection.ExecuteReader(sql, parameters, transaction);
        while (reader.Read())
        {
            var values = new Dictionary<string, string?>();
            for (var i = 0; i < map.Columns.Count; i++)
            {
                var column = spec.Column(map.Columns[i].Spec)!;
                values[column.Name] = FromDb(column, reader.GetValue(8 + i));
            }
            result.Add(new DictionaryEntryRow(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), dictionary, reader.IsDBNull(3) ? null : reader.GetString(3), spec.KeyOf(values), values,
                reader.GetFieldValue<DateTimeOffset>(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return result;
    }

    private static Map MapOf(string dictionary) =>
        Maps.TryGetValue(dictionary, out var map) ? map : throw new NotSupportedException($"Słownik {dictionary} nie ma tabeli w bazie");

    /// <summary>Wartość kanoniczna (ValueFormat) → typ kolumny bazy.</summary>
    private static object? ToDb(DictColumn column, string? canonical) =>
        canonical is null ? null : column.Type switch
        {
            ColumnType.Integer => int.Parse(canonical, CultureInfo.InvariantCulture),
            ColumnType.Decimal => decimal.Parse(canonical, NumberStyles.Number, CultureInfo.InvariantCulture),
            ColumnType.Date => DateTime.ParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ColumnType.Boolean => canonical == "tak",
            _ => canonical,
        };

    /// <summary>Wartość z bazy → zapis kanoniczny (ValueFormat).</summary>
    private static string? FromDb(DictColumn column, object value) =>
        value is DBNull ? null : column.Type switch
        {
            ColumnType.Integer => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ColumnType.Decimal => PolishNumber.ToCanonical((decimal)value),
            ColumnType.Date => DateText.ToCanonical(DateOnly.FromDateTime((DateTime)value)),
            ColumnType.Boolean => (bool)value ? "tak" : "nie",
            _ => (string)value,
        };
}
