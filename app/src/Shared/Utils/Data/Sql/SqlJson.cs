using System.Text.Json;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>Listy (kolumny, wartości wiersza surowego) zapisywane w bazie jako JSON.</summary>
public static class SqlJson
{
    public static string Write<T>(T value) => JsonSerializer.Serialize(value);

    public static IReadOnlyList<string> Strings(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    public static string?[] Values(string json) => JsonSerializer.Deserialize<string?[]>(json) ?? [];
}
