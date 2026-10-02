using PzlEv.Shared.Models.Db;

namespace PzlEv.Modules.Import.Services;

/// <summary>Rozpoznanie źródła po prefiksie nazwy pliku – bez rozróżniania wielkości liter, wygrywa najdłuższy prefiks.</summary>
public static class SourceMatcher
{
    public static SourceDefinitionRow? Match(string fileName, IEnumerable<SourceDefinitionRow> definitions) =>
        definitions
            .Where(d => d.Prefix.Length > 0 && fileName.StartsWith(d.Prefix, StringComparison.OrdinalIgnoreCase))
            .MaxBy(d => d.Prefix.Length);

    /// <summary>Pliki pomijane bez decyzji: blokady Excela (~$), ukryte, częściowo pobrane (.part).</summary>
    public static bool IsIgnored(string fileName) =>
        fileName.StartsWith("~$", StringComparison.Ordinal) || fileName.StartsWith('.') || fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
}
