using System.IO;
using PzlEv.Modules.MasterData.Data;
using PzlEv.Modules.MasterData.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.MasterData.Services;

/// <summary>
/// Zapis słownika z walidacją (ERROR blokuje, WARNING wymaga potwierdzenia), dziennikiem i wymianą przez Excel
/// (docs/slowniki.md, rozdz. 1, 5).
/// </summary>
public sealed class DictionaryService(IDictionaryStore store, IJournal journal)
{
    private const string Area = "Słowniki";

    public IReadOnlyList<DictRow> Load(DictionarySpec spec, string? project = null) =>
        store.Current(spec.Code, project)
            .Select(r => new DictRow(r.RowId, r.Version, r.Values))
            .ToList();

    /// <summary>Wszystkie wersje wiersza logicznego (historia).</summary>
    public IReadOnlyList<DictionaryEntryRow> History(long rowId) => store.History(rowId);

    /// <summary>
    /// Zapisuje docelowy stan słownika: working – wiersze po edycji (RowId null = nowy), removed – usunięte wiersze.
    /// </summary>
    public SaveOutcome Save(DictionarySpec spec, IReadOnlyList<DictRow> working, IReadOnlyList<DictRow> removed, bool confirmWarnings, string? project = null)
    {
        var issues = new List<Issue>();
        var rows = DictionaryValidator.Normalize(spec, working, issues);
        issues.AddRange(DictionaryValidator.Validate(spec, rows));

        if (issues.Any(i => i.Level == CheckLevel.Error))
            return new SaveOutcome(SaveStatus.Rejected, issues, "Słownik ma błędy (ERROR) – nic nie zapisano.");

        var current = store.Current(spec.Code, project).ToDictionary(r => r.RowId);
        var changes = new List<RowChange>();
        foreach (var row in rows)
        {
            var key = spec.KeyOf(row.Values);
            if (row.RowId is not { } id)
                changes.Add(new RowChange(RowChangeKind.Added, null, null, key, row.Values));
            else if (!current.TryGetValue(id, out var existing) || existing.Version != row.Version || !SameValues(spec, existing.Values, row.Values))
                changes.Add(new RowChange(RowChangeKind.Updated, id, row.Version, key, row.Values));
        }
        changes.AddRange(removed.Where(r => r.RowId is not null)
            .Select(r => new RowChange(RowChangeKind.Removed, r.RowId, r.Version, spec.KeyOf(r.Values), null)));

        if (changes.Count == 0)
            return new SaveOutcome(SaveStatus.NoChanges, issues, "Brak zmian do zapisania.");

        if (issues.Count > 0 && !confirmWarnings)
            return new SaveOutcome(SaveStatus.NeedsConfirmation, issues, "Słownik ma ostrzeżenia (WARNING) – potwierdź zapis.");

        var result = store.Save(spec.Code, project, changes);
        if (!result.Success)
            return new SaveOutcome(SaveStatus.Conflict, issues, result.Conflict!);

        var message = $"{spec.Name}: +{result.Added} ~{result.Updated} −{result.Removed}";
        journal.Add(Area, issues.Count > 0 ? $"{message} (zapis z ostrzeżeniami: {issues.Count})" : message, spec.Code);
        return new SaveOutcome(SaveStatus.Saved, issues, $"Zapisano – {message}.");
    }

    public void Export(DictionarySpec spec, string path, string? project = null)
    {
        var rows = Load(spec, project);
        ExcelTableWriter.Write(path, spec.Name, spec.Columns.Select(c => c.Name).ToList(),
            rows.Select(r => (IReadOnlyList<object?>)spec.Columns.Select(c => ValueFormat.ToExcel(c, r[c.Name])).ToList()));
        journal.Add(Area, $"{spec.Name}: pobrano do Excela ({rows.Count} wierszy)", spec.Code);
    }

    /// <summary>Wczytuje plik i porównuje z bieżącym stanem – bez zapisu.</summary>
    public ImportPreview PreviewImport(DictionarySpec spec, string path, string? project = null)
    {
        var fileName = Path.GetFileName(path);
        var data = TabularFileReader.Read(path);
        var issues = new List<Issue>();

        var columnIndex = new Dictionary<string, int>();
        foreach (var column in spec.Columns)
        {
            var index = FindHeader(data.Headers, column.Name);
            if (index >= 0)
                columnIndex[column.Name] = index;
            else if (column.Key || column.Required)
                issues.Add(Issue.Error($"Brak kolumny '{column.Name}'", fileName));
        }
        if (issues.Count > 0)
            return new ImportPreview(spec.Code, fileName, [], [], [], issues, [], []);

        var current = Load(spec, project);
        var currentByKey = current.GroupBy(r => spec.KeyOf(r.Values)).ToDictionary(g => g.Key, g => g.First());

        var fileRows = data.Rows
            .Select(cells => new DictRow(null, null, spec.Columns.ToDictionary(
                c => c.Name, c => columnIndex.TryGetValue(c.Name, out var i) && i < cells.Length ? cells[i] : null)))
            .ToList();
        var normalized = DictionaryValidator.Normalize(spec, fileRows, issues);

        var working = new List<DictRow>();
        var added = new List<string>();
        var changed = new List<string>();
        var seen = new HashSet<string>();
        foreach (var row in normalized)
        {
            var key = spec.KeyOf(row.Values);
            seen.Add(key);
            if (currentByKey.TryGetValue(key, out var existing))
            {
                working.Add(row with { RowId = existing.RowId, Version = existing.Version });
                var diff = spec.Columns
                    .Where(c => !string.Equals(existing[c.Name], row[c.Name], StringComparison.Ordinal))
                    .Select(c => $"{c.Name}: {ValueFormat.Display(c, existing[c.Name])} → {ValueFormat.Display(c, row[c.Name])}")
                    .ToList();
                if (diff.Count > 0)
                    changed.Add($"{key} – {string.Join("; ", diff)}");
            }
            else
            {
                working.Add(row);
                added.Add(key);
            }
        }
        var removedRows = current.Where(r => !seen.Contains(spec.KeyOf(r.Values))).ToList();

        issues.AddRange(DictionaryValidator.Validate(spec, normalized));
        return new ImportPreview(spec.Code, fileName, added, changed, removedRows.Select(r => spec.KeyOf(r.Values)).ToList(),
            issues, working, removedRows);
    }

    /// <summary>Zapisuje zatwierdzony podgląd (ostrzeżenia potwierdzone przez zatwierdzenie podglądu).</summary>
    public SaveOutcome ApplyImport(DictionarySpec spec, ImportPreview preview, string? project = null)
    {
        if (preview.HasErrors)
            return new SaveOutcome(SaveStatus.Rejected, preview.Issues, "Plik ma błędy (ERROR) – nic nie zapisano.");
        var outcome = Save(spec, preview.Working, preview.RemovedRows, confirmWarnings: true, project);
        if (outcome.Status == SaveStatus.Saved)
            journal.Add(Area, $"{spec.Name}: wczytano z Excela {preview.FileName} ({preview.Summary})", spec.Code);
        return outcome;
    }

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

    private static bool SameValues(DictionarySpec spec, IReadOnlyDictionary<string, string?> a, IReadOnlyDictionary<string, string?> b) =>
        spec.Columns.All(c => string.Equals(a.GetValueOrDefault(c.Name), b.GetValueOrDefault(c.Name), StringComparison.Ordinal));
}
