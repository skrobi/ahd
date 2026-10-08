using System.IO;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Shared.Utils.Dictionaries;

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
    /// knownErrors – błędy, które słownik miał już przed zmianą (np. element poza zakresem po odświeżeniu mapowania;
    /// klucz wiersza + komunikat – KnownErrors): nie blokują zapisu zmiany, są ostrzeżeniami.
    /// </summary>
    public SaveOutcome Save(DictionarySpec spec, IReadOnlyList<DictRow> working, IReadOnlyList<DictRow> removed, bool confirmWarnings, string? project = null,
        IReadOnlySet<string>? knownErrors = null)
    {
        var issues = new List<Issue>();
        var rows = DictionaryValidator.Normalize(spec, working, issues);
        issues.AddRange(DictionaryValidator.Validate(spec, rows));
        if (knownErrors is { Count: > 0 })
            issues = issues.Select(i => i.Level == CheckLevel.Error && knownErrors.Contains(Signature(spec, rows, i))
                ? Issue.Warning($"{i.Message} (błąd był już w słowniku – popraw go w zakładce Słowniki projektu)", i.Element)
                : i).ToList();

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

    /// <summary>Błędy (ERROR) bieżącego stanu słownika jako klucz wiersza + komunikat – do Save(knownErrors).</summary>
    public IReadOnlySet<string> KnownErrors(DictionarySpec spec, string? project = null)
    {
        var issues = new List<Issue>();
        var rows = DictionaryValidator.Normalize(spec, Load(spec, project), issues);
        issues.AddRange(DictionaryValidator.Validate(spec, rows));
        return issues.Where(i => i.Level == CheckLevel.Error).Select(i => Signature(spec, rows, i)).ToHashSet();
    }

    /// <summary>Błąd wiersza („wiersz N”) – klucz wiersza i komunikat; inny – sam komunikat.</summary>
    private static string Signature(DictionarySpec spec, IReadOnlyList<DictRow> rows, Issue issue)
    {
        var key = issue.Element is { } element && element.StartsWith("wiersz ", StringComparison.Ordinal)
            && int.TryParse(element.AsSpan(7), out var n) && n >= 1 && n <= rows.Count
            ? spec.KeyOf(rows[n - 1].Values)
            : "";
        return $"{key}\u001f{issue.Message}";
    }

    public void Export(DictionarySpec spec, string path, string? project = null)
    {
        var rows = Load(spec, project);
        ExcelTableWriter.WriteTemplate(path, [(spec.Name, ExcelColumns(spec),
            rows.Select(r => (IReadOnlyList<object?>)spec.Columns.Select(c => ValueFormat.ToExcel(c, r[c.Name])).ToList()))]);
        journal.Add(Area, $"{spec.Name}: pobrano do Excela ({rows.Count} wierszy)", spec.Code);
    }

    /// <summary>
    /// Wczytuje plik (Excel: wskazany arkusz albo pierwszy) i porównuje z bieżącym stanem – bez zapisu. Kolumna
    /// opcjonalna, której nie ma w pliku, zachowuje wartości ze słownika (ostrzeżenie); kolumny spoza słownika są
    /// pomijane (ostrzeżenie); wiersze z samym kluczem – pominięte, gdy słownik tak stanowi (SkipKeyOnlyRows).
    /// Komunikaty wskazują wiersz pliku jak w Excelu. inherited – Cost Category projektu: pozycje globalne.
    /// </summary>
    public ImportPreview PreviewImport(DictionarySpec spec, string path, string? project = null, string? sheet = null, IReadOnlyList<DictRow>? inherited = null)
    {
        var fileName = sheet is null ? Path.GetFileName(path) : $"{Path.GetFileName(path)} [{sheet}]";
        var data = TabularFileReader.Read(path, sheet);
        var issues = new List<Issue>();

        var columnIndex = new Dictionary<string, int>();
        var missing = new HashSet<string>();
        foreach (var column in spec.Columns)
        {
            var index = new[] { column.Name }.Concat(column.Aliases ?? []).Select(n => FindHeader(data.Headers, n)).FirstOrDefault(i => i >= 0, -1);
            if (index >= 0)
                columnIndex[column.Name] = index;
            else if (column.Key || column.Required)
                issues.Add(Issue.Error($"Brak kolumny '{column.Name}'", fileName));
            else
            {
                missing.Add(column.Name);
                issues.Add(Issue.Warning($"Brak kolumny '{column.Name}' w pliku – wartości tej kolumny w słowniku zostają bez zmian", fileName));
            }
        }
        if (issues.Any(i => i.Level == CheckLevel.Error))
            return new ImportPreview(spec.Code, fileName, [], [], [], issues, [], []);
        foreach (var header in data.Headers.Where((h, i) => h.Trim().Length > 0 && !columnIndex.ContainsValue(i)))
            issues.Add(Issue.Warning($"Kolumna '{header}' nie należy do słownika – pominięta", fileName));

        var fileRows = new List<DictRow>();
        var rowNumbers = new List<int>();
        var keyOnly = 0;
        for (var r = 0; r < data.Rows.Count; r++)
        {
            var cells = data.Rows[r];
            var row = new DictRow(null, null, spec.Columns.ToDictionary(
                c => c.Name, c => columnIndex.TryGetValue(c.Name, out var i) && i < cells.Length ? cells[i] : null));
            if (spec.SkipKeyOnlyRows && spec.Columns.Where(c => !c.Key && columnIndex.ContainsKey(c.Name)).All(c => ValueFormat.Clean(row[c.Name]) is null))
            {
                keyOnly++;
                continue;
            }
            fileRows.Add(row);
            rowNumbers.Add(data.RowNumbers is { } numbers ? numbers[r] : r + 2);
        }
        if (keyOnly > 0)
            issues.Add(Issue.Warning($"Pominięto wiersze z samym kluczem (bez pozostałych wartości): {keyOnly}", fileName));
        var preview = PreviewRows(spec, fileRows, fileName, project, missing, rowNumbers, inherited);
        return preview with { Issues = [.. issues, .. preview.Issues] };
    }

    /// <summary>
    /// Podgląd zastąpienia zawartości słownika wierszami z innego źródła (plik, HR): nowe, zmienione, usunięte, walidacja.
    /// Kolumny powiązane (DictColumn.Lookup): opis (np. imię i nazwisko) zamieniany na wartość (USRID). keep – kolumny
    /// bez danych w źródle: wartości z bieżącego słownika. rowNumbers – numery wierszy źródła do komunikatów.
    /// inherited – pozycje dziedziczone (Cost Category globalny): wiersze identyczne z nimi są pomijane. Zapis – ApplyImport.
    /// </summary>
    public ImportPreview PreviewRows(DictionarySpec spec, IReadOnlyList<DictRow> sourceRows, string source, string? project = null,
        IReadOnlySet<string>? keep = null, IReadOnlyList<int>? rowNumbers = null, IReadOnlyList<DictRow>? inherited = null)
    {
        var issues = new List<Issue>();
        var fileName = source;
        var current = Load(spec, project);
        var currentByKey = current.GroupBy(r => spec.KeyOf(r.Values)).ToDictionary(g => g.Key, g => g.First());
        var normalized = DictionaryValidator.Normalize(spec, ResolveLookups(spec, sourceRows, issues, rowNumbers), issues);

        if (keep is { Count: > 0 })
            normalized = normalized.Select(row => currentByKey.TryGetValue(spec.KeyOf(row.Values), out var existing)
                ? row with { Values = row.Values.ToDictionary(p => p.Key, p => keep.Contains(p.Key) ? existing[p.Key] : p.Value) }
                : row).ToList();

        if (inherited is { Count: > 0 })
        {
            var global = inherited.GroupBy(r => spec.KeyOf(r.Values)).ToDictionary(g => g.Key, g => g.First());
            var same = normalized.Select((row, i) => (row, i))
                .Where(x => global.TryGetValue(spec.KeyOf(x.row.Values), out var g) && SameValues(spec, g.Values, x.row.Values))
                .Select(x => x.i).ToHashSet();
            if (same.Count > 0)
            {
                issues.Add(Issue.Warning($"Pominięto wiersze identyczne ze słownikiem globalnym (obowiązują bez zmiany w projekcie): {same.Count}", fileName));
                normalized = normalized.Where((_, i) => !same.Contains(i)).ToList();
                rowNumbers = rowNumbers?.Where((_, i) => !same.Contains(i)).ToList();
            }
        }

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
        if (removedRows.Count > 0 && removedRows.Count * 2 > current.Count)
            issues.Add(Issue.Warning($"Wczytanie usuwa {removedRows.Count} z {current.Count} wierszy słownika – sprawdź, czy to właściwy plik i arkusz", fileName));

        issues.AddRange(DictionaryValidator.Validate(spec, normalized));
        return new ImportPreview(spec.Code, fileName, added, changed, removedRows.Select(r => spec.KeyOf(r.Values)).ToList(),
            Renumber(issues, rowNumbers), working, removedRows);
    }

    /// <summary>
    /// Kolumny arkusza Excel słownika (eksport, szablon): format kolumny według typu (klucze i tekst – tekst), lista
    /// wyboru (Choice), lista osób (kolumna powiązana – arkusz „Listy”), opis w komentarzu nagłówka.
    /// </summary>
    public IReadOnlyList<ExcelTableWriter.ExcelColumn> ExcelColumns(DictionarySpec spec)
    {
        var lookups = Lookups(spec);
        return spec.Columns.Select(c => new ExcelTableWriter.ExcelColumn(
            c.Name,
            c.Key ? ExcelTableWriter.Kind.Text : c.Type switch
            {
                ColumnType.Decimal => ExcelTableWriter.Kind.Number,
                ColumnType.Integer => ExcelTableWriter.Kind.Integer,
                ColumnType.Date => ExcelTableWriter.Kind.Date,
                _ => ExcelTableWriter.Kind.Text,
            },
            c.Choices,
            ColumnNote(spec, c),
            c.Lookup is { } code ? lookups[code].Select(o => (o.Value, o.Label)).ToList() : null)).ToList();
    }

    private static string ColumnNote(DictionarySpec spec, DictColumn column)
    {
        var parts = new List<string>();
        if (column.Key)
            parts.Add(spec.EmptyKeyPartsAllowed ? "część klucza" : "klucz – wymagany, bez powtórzeń");
        else if (column.Required)
            parts.Add("wymagane");
        parts.Add(column.Type switch
        {
            ColumnType.Decimal => "liczba (np. 1 250,5)",
            ColumnType.Integer => "liczba całkowita",
            ColumnType.Date => "data RRRR-MM-DD",
            ColumnType.Boolean => "tak / nie",
            ColumnType.Choice => $"jedna z: {string.Join(", ", column.Choices ?? [])}",
            _ => "tekst",
        });
        if (column.Lookup is { } code)
            parts.Add($"wartość ze słownika {GlobalDictionaries.Get(code).Name} (arkusz „Listy”); imię i nazwisko zamieniane na USRID");
        if (column.PadNumericTo is { } pad)
            parts.Add($"numer uzupełniany zerami do {pad} znaków");
        return string.Join("; ", parts);
    }

    /// <summary>Wartości słowników powiązanych z kolumnami słownika (DictColumn.Lookup) według kodu słownika.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<LookupOption>> Lookups(DictionarySpec spec) =>
        spec.Columns.Select(c => c.Lookup).OfType<string>().Distinct()
            .ToDictionary(code => code, code => GlobalDictionaries.LookupOptions(code, Load(GlobalDictionaries.Get(code))));

    /// <summary>Kolumny powiązane: opis (imię i nazwisko) → wartość (USRID), jak przy wpisaniu w tabeli; niejednoznaczny – ostrzeżenie.</summary>
    private List<DictRow> ResolveLookups(DictionarySpec spec, IReadOnlyList<DictRow> rows, List<Issue> issues, IReadOnlyList<int>? rowNumbers)
    {
        var lookups = Lookups(spec);
        if (lookups.Count == 0)
            return rows.ToList();
        var result = new List<DictRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var values = rows[i].Values.ToDictionary(p => p.Key, p => p.Value);
            foreach (var column in spec.Columns.Where(c => c.Lookup is not null))
            {
                var options = lookups[column.Lookup!];
                var text = values.GetValueOrDefault(column.Name);
                var resolved = DictionaryCells.Resolve(text, options);
                values[column.Name] = resolved;
                if (resolved is not null && options.Count(o => string.Equals(o.Label, resolved, StringComparison.OrdinalIgnoreCase)) > 1)
                    issues.Add(Issue.Warning($"{column.Name}: '{resolved}' – kilka osób o tym opisie, wpisz USRID", DictionaryValidator.RowElement(i)));
            }
            result.Add(rows[i] with { Values = values });
        }
        return result;
    }

    /// <summary>„wiersz N” (pozycja w zestawie) → numer wiersza w pliku, jak w Excelu.</summary>
    private static List<Issue> Renumber(List<Issue> issues, IReadOnlyList<int>? rowNumbers)
    {
        if (rowNumbers is null)
            return issues;
        return issues.Select(issue =>
            issue.Element is { } element && element.StartsWith("wiersz ", StringComparison.Ordinal)
                && int.TryParse(element.AsSpan(7), out var n) && n >= 1 && n <= rowNumbers.Count
                ? issue with { Element = $"wiersz {rowNumbers[n - 1]} w pliku" }
                : issue).ToList();
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
