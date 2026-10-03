using System.Globalization;
using System.IO;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Projects.Services;

/// <summary>
/// Wczytanie nakładki Performance Objectives z eksportu struktury WBS z SAP (docs/performance-objectives.md,
/// rozdz. 5): kolumny rozpoznawane po nagłówkach, drzewo budowane według Level (1 = korzeń; wiersz należy do
/// najbliższego wcześniejszego wiersza o niższym poziomie). Klucz – WBS element.
/// </summary>
public static class PerformanceObjectivesReader
{
    public const string ProjectDefinition = "Project definition";
    public const string Level = "Level";
    public const string WbsElement = "WBS element";
    public const string Name = "Name";
    public const string PersonResponsible = "Person responsible";
    public const string ProfitCenter = "Profit center";
    public const string LegacyWbs = "Legacy WBS";
    public const string PerformanceObligation = "Performance Obligation";
    public const string SacObjNumber = "SAC Obj Number";
    public const string Statistical = "Statistical";
    public const string AcctAsstElement = "Acct asst elem.ind.";

    private static readonly string[] Required = [Level, WbsElement, Name];

    public static PoImportResult Read(string path) => Read(TabularFileReader.Read(path), Path.GetFileName(path));

    public static PoImportResult Read(TabularData data, string fileName)
    {
        var issues = new List<Issue>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var column in new[] { ProjectDefinition, Level, WbsElement, Name, PersonResponsible, ProfitCenter, LegacyWbs, PerformanceObligation, SacObjNumber, Statistical, AcctAsstElement })
        {
            var i = FindHeader(data.Headers, column);
            if (i >= 0)
                index[column] = i;
            else if (Required.Contains(column))
                issues.Add(Issue.Error($"Brak kolumny '{column}'", fileName));
        }
        var tree = new PoTree();
        if (issues.Count > 0)
            return new PoImportResult(fileName, tree, issues);

        var stack = new List<(int Level, long Key)>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previousLevel = 0;
        for (var r = 0; r < data.Rows.Count; r++)
        {
            var row = data.Rows[r];
            var line = $"wiersz {r + 2}";
            string? Cell(string column) => index.TryGetValue(column, out var i) && i < row.Length && !string.IsNullOrWhiteSpace(row[i]) ? row[i]!.Trim() : null;

            var wbs = Cell(WbsElement);
            if (wbs is null)
            {
                issues.Add(Issue.Warning("Wiersz bez WBS element – pominięty", line));
                continue;
            }
            if (!int.TryParse(Cell(Level), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level < 1)
            {
                issues.Add(Issue.Error($"Level '{Cell(Level)}' – oczekiwano liczby całkowitej ≥ 1", line));
                continue;
            }
            if (seen.TryGetValue(wbs, out var first))
            {
                issues.Add(Issue.Error($"Powtórzony WBS element {wbs} (pierwszy raz w wierszu {first})", line));
                continue;
            }
            seen[wbs] = r + 2;
            if (level > previousLevel + 1 && previousLevel > 0)
                issues.Add(Issue.Warning($"Poziom {level} po poziomie {previousLevel} – {wbs} przypięty do najbliższego wyższego elementu", line));
            previousLevel = level;

            while (stack.Count > 0 && stack[^1].Level >= level)
                stack.RemoveAt(stack.Count - 1);
            var node = tree.Add(new PoNode
            {
                Key = tree.NewKey(),
                ParentKey = stack.Count > 0 ? stack[^1].Key : null,
                ProjectDefinition = Cell(ProjectDefinition),
                WbsElement = wbs,
                Name = Cell(Name) ?? wbs,
                PersonResponsible = Cell(PersonResponsible),
                ProfitCenter = Cell(ProfitCenter),
                LegacyWbs = Cell(LegacyWbs),
                PerformanceObligation = Cell(PerformanceObligation),
                SacObjNumber = Cell(SacObjNumber),
                IsStatistical = IsMarked(Cell(Statistical)),
                IsAcctAsstElement = IsMarked(Cell(AcctAsstElement)),
            });
            stack.Add((level, node.Key));
        }
        if (tree.ElementCount == 0 && !issues.Any(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error))
            issues.Add(Issue.Error("Plik nie zawiera elementów (wierszy z WBS element)", fileName));
        return new PoImportResult(fileName, tree, issues);
    }

    /// <summary>Znacznik elementu w eksporcie SAP: „X” (także tak / true / 1).</summary>
    private static bool IsMarked(string? value) =>
        value is not null && (value.Equals("X", StringComparison.OrdinalIgnoreCase) || value is "1" || value.Equals("tak", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static int FindHeader(IReadOnlyList<string> headers, string name)
    {
        static string Norm(string s) => new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();
        var target = Norm(name);
        for (var i = 0; i < headers.Count; i++)
        {
            if (Norm(headers[i]) == target)
                return i;
        }
        return -1;
    }
}
