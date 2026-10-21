using System.IO;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>Ekran programu – zakładka Struktura: tabela struktury z zapisem wierszy, edycja Performance Objectives, Excel struktury, „Zmiany ACWP”.</summary>
public sealed partial class ProjectDetailViewModel
{
    public StructureViewModel Structure { get; }

    /// <summary>Edytor nakładki Performance Objectives (ten sam co w kreatorze), widoczny w trybie edycji.</summary>
    public PoEditorViewModel Objectives { get; }

    public bool IsEditingObjectives
    {
        get => _isEditingObjectives;
        private set
        {
            if (SetProperty(ref _isEditingObjectives, value))
                OnPropertyChanged(nameof(IsViewingStructure));
        }
    }

    public bool IsViewingStructure => !_isEditingObjectives;

    public string MappingInfo => _mapping.Describe;

    public ICommand AddFromExcel { get; }
    public ICommand EditObjectives { get; }
    public ICommand SaveObjectives { get; }
    public ICommand CancelObjectives { get; }
    public ICommand ExportStructure { get; }

    /// <summary>„Wczytaj strukturę z Excela” – WP, CAM, Cost Category, budżet i daty z arkusza „Struktura” uzupełnionego ręcznie.</summary>
    public ICommand ImportStructure { get; }

    /// <summary>Okno „Zmiany ACWP” (moduł Przebiegi) dla wiersza struktury i rodzaju kosztu kolumny; wiersz bez elementów WBS – cały program.</summary>
    private void ShowAcwpChanges(StructureRowViewModel row, string column)
    {
        if (_service.AcwpChanges is not { } window)
        {
            Status = "Okno „Zmiany ACWP” niedostępne – moduł Przebiegi nie jest uruchomiony.";
            return;
        }
        var kind = column switch
        {
            StructureColumns.AcwpLabor => GlobalDictionaries.LaborKind,
            StructureColumns.AcwpMaterial => GlobalDictionaries.MaterialKind,
            StructureColumns.AcwpOther => GlobalDictionaries.OtherKind,
            _ => null,
        };
        var elements = Structure.WbsElementsOf(row);
        window.Show(new AcwpChangesRequest(Code, kind, elements.Count == 0 ? null : elements, Scope: row.Row.Name));
    }

    /// <summary>Czas zbierania zmian przed zapisem – kilka kliknięć WP albo Enter w kolejnych wierszach idzie jednym zapisem.</summary>
    private static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Zapis zmian wiersza struktury od razu po zatwierdzeniu. Wiersze zatwierdzone w krótkim czasie (kilka WP, Enter
    /// w kolejnych wierszach, wklejenie bloku) czekają w kolejce i są zapisywane razem – jednym zapisem na słownik
    /// (ProjectService.SaveStructureEdits); po serii – jedno odświeżenie danych projektu (także po nieudanym zapisie –
    /// część zmian mogła się zapisać). Nieudany zapis – zmiany zostają w wierszu (żółty), komunikat wyżej.
    /// </summary>
    private async Task CommitRow(StructureRowViewModel row)
    {
        if (!_commits.Contains(row))
            _commits.Enqueue(row);
        if (_committing)
            return;
        _committing = true;
        var saved = new HashSet<string>();
        var messages = new List<string>();
        var failures = new List<string>();
        try
        {
            await Task.Delay(CommitDelay);
            while (_commits.Count > 0)
            {
                var edits = new List<StructureEdit>();
                var names = new Dictionary<string, string>();
                while (_commits.TryDequeue(out var next))
                {
                    if (next.Changes.Count == 0 || !ConfirmWpRemoval(next))
                        continue;
                    edits.Add(new StructureEdit(next.Id, next.Row, next.Changes.ToDictionary(c => c.Key, c => c.Value)));
                    if (next.Changes.ContainsKey(StructureEdits.P1s))
                        _production = null;   // zmiana Legacy WBS / kodu wydzielenia – dane produkcyjne czytane ponownie
                    names[next.Id] = next.Row.Name;
                }
                if (edits.Count == 0)
                    continue;
                var mapping = _mapping;
                await Try(async () =>
                {
                    var results = await Busy.Run(edits.Count > 1 ? $"Zapisywanie zmian ({edits.Count} wierszy)…" : "Zapisywanie zmiany…",
                        () => _service.SaveStructureEdits(Code, mapping, edits));
                    foreach (var result in results)
                    {
                        if (result.Saved)
                        {
                            saved.Add(result.Id);
                            messages.Add(result.Message);
                        }
                        else
                            failures.Add($"{names[result.Id]}: {result.Message}");
                    }
                });
            }
            await Reload(refreshMapping: false, saved: saved);
            Status = failures.Count > 0
                ? $"Nie zapisano ({failures.Count}): {string.Join(" · ", failures.Take(3))}{(failures.Count > 3 ? " …" : "")} Popraw komórki (wiersze żółte) albo „Odrzuć niezapisane”."
                : saved.Count > 1 ? $"Zapisano wierszy: {saved.Count}." : messages.LastOrDefault() ?? Status;
        }
        finally
        {
            _committing = false;
        }
        if (_commits.Count > 0)
            await CommitRow(_commits.Peek());
    }

    /// <summary>Odznaczenie WP z budżetem lub datami usuwa harmonogram WP – wymaga potwierdzenia; „nie” cofa znacznik.</summary>
    private bool ConfirmWpRemoval(StructureRowViewModel row)
    {
        if (!row.Changes.TryGetValue(StructureEdits.Wp, out var flag) || StructureEdits.IsChecked(flag) || row.Row.Wp is not { } wp
            || row.Row.OwnsBudget is false || (row.Row.BacHours == 0 && row.Row.BacMaterial == 0 && row.Row.Bac == 0 && row.Row.Start is null && row.Row.Finish is null))
            return true;
        if (_dialogs.Confirm("Odznaczenie WP",
                $"Odznaczenie WP {wp} („{row.Row.Name}”) usunie jego przypisanie (CAM, Cost Category) oraz harmonogram i budżet WP. Kontynuować?"))
            return true;
        row[StructureEdits.Wp] = "true";
        return row.Changes.Count > 0;
    }

    /// <summary>Przed wyjściem z tabeli struktury: edycja w toku zatwierdzona; zapis w toku albo nieudany – komunikat.</summary>
    private bool StructureSettled(string action)
    {
        Structure.CommitEdits();
        if (!_committing && _commits.Count == 0 && !Structure.HasChanges)
            return true;
        Status = _committing || _commits.Count > 0
            ? $"Trwa zapis zmian struktury – poczekaj przed {action}."
            : $"Struktura ma niezapisane zmiany (wiersze żółte) – popraw je albo „Odrzuć niezapisane” przed {action}.";
        return false;
    }

    private void StartEditObjectives()
    {
        Objectives.Load(_saved.Copy());
        IsEditingObjectives = true;
        Status = "Edycja Performance Objectives – zapis tworzy nowe wersje zmienionych węzłów (historia).";
    }

    private async Task DoSaveObjectives()
    {
        Objectives.CommitEdits();
        var tree = Objectives.Tree.Copy();
        await Try(async () =>
        {
            var (issues, result) = await Busy.Run("Zapisywanie Performance Objectives…", () => _service.SaveObjectives(Code, tree));
            if (result is null)
            {
                Status = "Performance Objectives mają błędy (ERROR) – nic nie zapisano.";
                return;
            }
            if (!result.Success)
            {
                Status = result.Conflict!;
                return;
            }
            IsEditingObjectives = false;
            _production = null;   // nakładka zmienia zakres P1S i wydzielenia – dane produkcyjne czytane ponownie
            await Reload(refreshMapping: false);
            Status = $"Zapisano Performance Objectives: +{result.Added} ~{result.Updated} −{result.Removed}{(issues.Count > 0 ? $" (ostrzeżenia: {issues.Count})" : "")}.";
        });
    }

    /// <summary>Struktura projektu (stan zapisany w bazie, wszystkie kolumny) do Excela; niezapisane zmiany – najpierw zapis.</summary>
    private async Task DoExportStructure()
    {
        if (!StructureSettled("pobraniem struktury do Excela") || _structure is not { } current)
            return;
        var path = _dialogs.SaveExcel("Pobierz strukturę programu", $"{Code}_struktura.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            var rows = await Busy.Run("Zapisywanie struktury programu do Excela…", () => _service.ExportStructure(path, Code, current.Structure, current.Persons, Structure.Mpks.ToList()));
            Status = $"Zapisano {path} – {rows} wierszy struktury.";
        });
    }

    /// <summary>
    /// Wczytanie arkusza „Struktura” uzupełnionego w Excelu (StructureImport): podsumowanie zmian i problemów pliku do
    /// potwierdzenia, potem zapis jak edycja w tabeli struktury (SaveStructureEdits) i odświeżenie projektu.
    /// </summary>
    private async Task DoImportStructure()
    {
        if (!StructureSettled("wczytaniem struktury z Excela") || _structure is not { } current)
            return;
        var path = _dialogs.OpenExcel($"Wczytaj strukturę programu {Code} z Excela (arkusz „Struktura” pobrany z aplikacji)");
        if (path is null)
            return;
        await Try(async () =>
        {
            var file = await Busy.Run("Odczyt pliku struktury…", () => _service.ReadStructureFile(path, Code, current.Structure));
            var problems = file.Issues.OrderBy(i => i.Level == CheckLevel.Error ? 0 : 1)
                .Select(i => $"• {(i.Level == CheckLevel.Error ? "BŁĄD" : "UWAGA")} {i.Element}: {i.Message}").ToList();
            var shown = string.Join("\n", problems.Take(10)) + (problems.Count > 10 ? $"\n… (+{problems.Count - 10})" : "");
            if (file.Edits.Count == 0)
            {
                Status = problems.Count == 0 ? $"{Path.GetFileName(path)}: brak zmian względem programu." : $"{Path.GetFileName(path)}: brak zmian do zapisu – {problems[0]}";
                return;
            }
            var budget = file.Edits.Count(e => e.Changes.Keys.Any(StructureEdits.ScheduleColumns.Contains));
            var message = $"Plik {Path.GetFileName(path)} ({file.FileRows} wierszy) – zmiany w {file.Edits.Count} wierszach struktury:\n"
                          + $"• nowe WP: {file.Count(StructureEdits.Wp)}\n• CAM: {file.Count(StructureEdits.Cam)}\n• Cost Category: {file.Count(StructureEdits.CostCategory)}\n"
                          + $"• budżet i daty WP: {budget}\n\nPuste komórki – bez zmiany."
                          + (problems.Count > 0 ? $"\n\nPominięte ({problems.Count}):\n{shown}" : "")
                          + "\n\nZapisać zmiany?";
            if (!_dialogs.Confirm("Wczytaj strukturę z Excela", message))
            {
                Status = "Wczytanie struktury anulowane – bez zmian.";
                return;
            }
            var mapping = _mapping;
            var results = await Busy.Run($"Zapisywanie zmian z pliku ({file.Edits.Count} wierszy)…", () => _service.SaveStructureEdits(Code, mapping, file.Edits));
            var failures = results.Where(r => !r.Saved).Select(r => $"{r.Id}: {r.Message}").ToList();
            _production = null;
            await Reload(refreshMapping: false);
            Status = failures.Count == 0
                ? $"Wczytano strukturę z {Path.GetFileName(path)}: zapisano {results.Count} wierszy{(problems.Count > 0 ? $", pominięto {problems.Count}" : "")}."
                : $"Zapisano {results.Count - failures.Count} z {results.Count} wierszy; nie zapisano: {string.Join(" · ", failures.Take(3))}{(failures.Count > 3 ? " …" : "")}";
        });
    }

    /// <summary>Dołożenie elementów z kolejnego eksportu SAP (np. nowe Project definition) – istniejąca nakładka bez zmian.</summary>
    private async Task DoAddFromExcel()
    {
        var path = _dialogs.OpenExcel($"Dołóż elementy do programu {Code} – eksport struktury WBS z SAP");
        if (path is null)
            return;
        await Try(async () =>
        {
            var (message, changed) = await Busy.Run($"Wczytywanie {Path.GetFileName(path)} i dokładanie nowych elementów…", () =>
            {
                var read = _service.ReadObjectives(path);
                if (read.HasErrors)
                    return ($"Plik {read.FileName} ma błędy (ERROR) – nic nie dołożono: {string.Join("; ", read.Issues.Where(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error).Take(3).Select(i => i.Message))}", false);
                var (tree, added, projects) = ProjectService.AddNew(_service.Objectives(Code), read.Tree);
                if (added == 0)
                    return ($"Plik {read.FileName} nie zawiera nowych elementów – struktura bez zmian.", false);
                var (issues, result) = _service.SaveObjectives(Code, tree);
                if (result is null)
                    return ($"Nie dołożono – nakładka miałaby błędy: {string.Join("; ", issues.Where(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error).Take(3).Select(i => i.Message))}", false);
                if (!result.Success)
                    return (result.Conflict!, false);
                return ($"Dołożono {added} elementów{(projects.Count > 0 ? $" (Project definition: {string.Join(", ", projects)})" : "")}.", true);
            });
            if (changed)
            {
                _production = null;
                await Reload(refreshMapping: false);
            }
            Status = message;
        });
    }
}
