using System.Text.Json;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>Listy (kolumny, wartości wiersza surowego) zapisywane w bazie jako JSON.</summary>
public static class SqlJson
{
    public static string Write<T>(T value) => JsonSerializer.Serialize(value);

    public static IReadOnlyList<string> Strings(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    public static string?[] Values(string json) => JsonSerializer.Deserialize<string?[]>(json) ?? [];

    /// <summary>Lista obiektów (pola parsera, mapowanie kolumn); pusta przy braku wartości.</summary>
    public static IReadOnlyList<T> List<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<T>>(json) ?? [];
}
