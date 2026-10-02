using System.Collections;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PzlEv.Tests.InMemory;

/// <summary>
/// Baza w pamięci do testów (aplikacja pracuje wyłącznie na MS SQL): tabele o kształcie schematu (meta.*, stg.*,
/// can.*, dict.* z typami wierszy w Shared/Models/Db) dla magazynów InMemory*Store – szybkie testy logiki bez bazy;
/// te same scenariusze testy uruchamiają też na SQL (TestStores).
///
/// Zasady: odczyt w Read, zapis w Write (jedna blokada); operacja zapisu najpierw sprawdza dane, a dopiero potem
/// je zmienia (brak zmian częściowych). Commit zapisuje stan do pliku JSON, jeśli coś się zmieniło.
/// </summary>
public sealed class InMemoryDatabase
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly object _gate = new();
    private readonly Dictionary<string, (Type RowType, IList Rows)> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _sequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _statePath;
    private bool _dirty;

    public InMemoryDatabase(string? statePath = null)
    {
        _statePath = statePath;
    }

    /// <summary>Czy tabela istnieje i ma wiersze (np. do decyzji o danych startowych).</summary>
    public bool HasRows(string table) => Read(() => _tables.TryGetValue(table, out var t) && t.Rows.Count > 0);

    public T Read<T>(Func<T> read)
    {
        lock (_gate)
            return read();
    }

    public T Write<T>(Func<T> write)
    {
        lock (_gate)
        {
            var result = write();
            _dirty = true;
            return result;
        }
    }

    public void Write(Action write) => Write(() => { write(); return 0; });

    /// <summary>Tabela o podanej nazwie (np. "dict.Entry"); używać wyłącznie wewnątrz Read / Write.</summary>
    public List<TRow> Table<TRow>(string name)
    {
        if (!Monitor.IsEntered(_gate))
            throw new InvalidOperationException($"Dostęp do tabeli {name} poza Read/Write.");

        if (_tables.TryGetValue(name, out var table))
        {
            if (table.RowType != typeof(TRow))
                throw new InvalidOperationException($"Tabela {name} ma wiersze {table.RowType.Name}, nie {typeof(TRow).Name}.");
            return (List<TRow>)table.Rows;
        }

        var rows = new List<TRow>();
        _tables[name] = (typeof(TRow), rows);
        return rows;
    }

    /// <summary>Kolejny identyfikator sekwencji (jak IDENTITY / SEQUENCE w MS SQL); wewnątrz Write.</summary>
    public long NextId(string sequence)
    {
        if (!Monitor.IsEntered(_gate))
            throw new InvalidOperationException($"Sekwencja {sequence} poza Write.");
        _sequences.TryGetValue(sequence, out var last);
        _sequences[sequence] = ++last;
        return last;
    }

    /// <summary>Zapisuje stan do pliku, jeśli od ostatniego zapisu coś się zmieniło.</summary>
    public void Commit()
    {
        if (_statePath is null)
            return;
        lock (_gate)
        {
            if (!_dirty)
                return;
            var tables = new JsonObject();
            foreach (var (name, (type, rows)) in _tables)
            {
                tables[name] = new JsonObject
                {
                    ["type"] = type.FullName,
                    ["rows"] = JsonSerializer.SerializeToNode(rows, rows.GetType(), Json),
                };
            }
            var state = new JsonObject
            {
                ["sequences"] = JsonSerializer.SerializeToNode(_sequences, Json),
                ["tables"] = tables,
            };
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, state.ToJsonString(Json));
            File.Move(tmp, _statePath, overwrite: true);
            _dirty = false;
        }
    }

    /// <summary>Wczytuje stan z pliku (jeśli istnieje). Zwraca false, gdy pliku nie ma – wtedy dane startowe.</summary>
    public bool Load()
    {
        if (_statePath is null || !File.Exists(_statePath))
            return false;
        lock (_gate)
        {
            var state = JsonNode.Parse(File.ReadAllText(_statePath))!.AsObject();
            _tables.Clear();
            _sequences.Clear();
            foreach (var (name, value) in state["sequences"]!.AsObject())
                _sequences[name] = value!.GetValue<long>();
            foreach (var (name, node) in state["tables"]!.AsObject())
            {
                var typeName = node!["type"]!.GetValue<string>();
                var rowType = typeof(InMemoryDatabase).Assembly.GetType(typeName)
                    ?? throw new InvalidOperationException($"Stan danych: nieznany typ wiersza {typeName} (tabela {name}).");
                var listType = typeof(List<>).MakeGenericType(rowType);
                var rows = (IList)(node["rows"].Deserialize(listType, Json) ?? Activator.CreateInstance(listType)!);
                _tables[name] = (rowType, rows);
            }
            _dirty = false;
            return true;
        }
    }
}
