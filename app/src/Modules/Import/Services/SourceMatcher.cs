using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Import.Services;

/// <summary>
/// Rozpoznanie źródła po prefiksie nazwy pliku – bez rozróżniania wielkości liter, wygrywa najdłuższy prefiks.
/// Gwiazdka na końcu prefiksu (zapis „ACTUALS_*”) oznacza to samo co prefiks bez niej.
/// </summary>
public static class SourceMatcher
{
    public static SourceDefinitionRow? Match(string fileName, IEnumerable<SourceDefinitionRow> definitions) =>
        definitions
            .Where(d => Prefix(d.Prefix) is { Length: > 0 } prefix && fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => Prefix(d.Prefix).Length)
            .FirstOrDefault();

    /// <summary>Prefiks do porównania: bez spacji i bez gwiazdek na końcu.</summary>
    public static string Prefix(string prefix) => prefix.Trim().TrimEnd('*').Trim();

    /// <summary>Pliki pomijane bez decyzji: blokady Excela (~$), ukryte, częściowo pobrane (.part).</summary>
    public static bool IsIgnored(string fileName) =>
        fileName.StartsWith("~$", StringComparison.Ordinal) || fileName.StartsWith('.') || fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
}
