using System.Data;
using Microsoft.Data.SqlClient;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>Wsadowy zapis wierszy (SqlBulkCopy) w paczkach – wiersze surowe i kanoniczne dużych raportów.</summary>
public static class SqlBulk
{
    public static void Insert(SqlConnection connection, SqlTransaction transaction, string table, IReadOnlyList<(string Name, Type Type)> columns,
        IEnumerable<object?[]> rows, int chunk = 20_000)
    {
        using var copy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = table,
            BulkCopyTimeout = 0,
            BatchSize = chunk,
        };
        foreach (var (name, _) in columns)
            copy.ColumnMappings.Add(name, name);

        using var data = new DataTable();
        foreach (var (name, type) in columns)
            data.Columns.Add(name, Nullable.GetUnderlyingType(type) ?? type);
        foreach (var row in rows)
        {
            data.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
            if (data.Rows.Count < chunk)
                continue;
            copy.WriteToServer(data);
            data.Clear();
        }
        if (data.Rows.Count > 0)
            copy.WriteToServer(data);
    }
}
