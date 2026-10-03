using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>
/// Wsadowy zapis wierszy (SqlBulkCopy) strumieniowo – wiersze nie są gromadzone w pamięci (miliony wierszy raportu).
/// Paczki co najmniej 102 400 wierszy trafiają do indeksu kolumnowego od razu jako skompresowane grupy wierszy.
/// </summary>
public static class SqlBulk
{
    public const int BatchSize = 500_000;

    public static long Insert(SqlConnection connection, SqlTransaction transaction, string table, IReadOnlyList<(string Name, Type Type)> columns,
        IEnumerable<object?[]> rows)
    {
        using var copy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
        {
            DestinationTableName = table,
            BulkCopyTimeout = 0,
            BatchSize = BatchSize,
            EnableStreaming = true,
        };
        foreach (var (name, _) in columns)
            copy.ColumnMappings.Add(name, name);
        using var reader = new RowReader(columns, rows);
        copy.WriteToServer(reader);
        return reader.Count;
    }

    /// <summary>Minimalny czytnik danych nad strumieniem wierszy (object?[] w kolejności kolumn).</summary>
    private sealed class RowReader(IReadOnlyList<(string Name, Type Type)> columns, IEnumerable<object?[]> rows) : DbDataReader
    {
        private readonly IEnumerator<object?[]> _rows = rows.GetEnumerator();
        private object?[] _current = [];

        public long Count { get; private set; }

        public override int FieldCount => columns.Count;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override int Depth => 0;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool Read()
        {
            if (!_rows.MoveNext())
                return false;
            _current = _rows.Current;
            Count++;
            return true;
        }

        public override bool NextResult() => false;
        public override string GetName(int ordinal) => columns[ordinal].Name;
        public override int GetOrdinal(string name) => columns.Select((c, i) => (c.Name, i)).First(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).i;
        public override Type GetFieldType(int ordinal) => Nullable.GetUnderlyingType(columns[ordinal].Type) ?? columns[ordinal].Type;
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override object GetValue(int ordinal) => _current[ordinal] ?? DBNull.Value;
        public override bool IsDBNull(int ordinal) => _current[ordinal] is null;

        public override int GetValues(object[] values)
        {
            var count = Math.Min(values.Length, columns.Count);
            for (var i = 0; i < count; i++)
                values[i] = GetValue(i);
            return count;
        }

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
        public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _rows.Dispose();
            base.Dispose(disposing);
        }
    }
}
