using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Słownik projektu na ekranie (kreator, krok 3; ekran Projekt): stan, wczytany plik, podgląd różnic i wynik walidacji.
/// Akcje ustawia ekran nadrzędny.
/// </summary>
public sealed class DictionaryPanelViewModel(ProjectDictionaryItem item, string type) : ObservableObject
{
    private ImportPreview? _preview;
    private string _file = "";
    private int _rows;
    private string _lastChange = "";

    public ProjectDictionaryItem Item => item;

    public string Name => item.Name;

    public string Requirement => !item.Stored
        ? "zawartość nieustalona (O37) – nie jest wczytywany"
        : item.IsRequired(type) ? "wymagany – bez niego przebieg jest zablokowany" : "opcjonalny";

    public bool CanLoad => item.Stored;

    public ImportPreview? Preview => _preview;

    public string File { get => _file; private set => SetProperty(ref _file, value); }

    public int Rows { get => _rows; set { if (SetProperty(ref _rows, value)) OnPropertyChanged(nameof(StatusPill)); } }

    public string LastChange { get => _lastChange; set => SetProperty(ref _lastChange, value); }

    public ObservableCollection<Issue> Issues { get; } = [];

    public ObservableCollection<string> Lines { get; } = [];

    public bool HasPreview => _preview is not null;

    public string PreviewSummary => _preview is null ? "" : $"{_preview.Summary} · {_preview.Working.Count} wierszy w pliku";

    public Pill StatusPill => !item.Stored
        ? new Pill("crit", "nieustalony (O37)")
        : _preview is { HasErrors: true } ? new Pill("crit", $"{_preview.Issues.Count(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error)} błędów")
        : _preview is not null ? new Pill("ok", $"{_preview.Working.Count} wierszy z pliku")
        : _rows > 0 ? new Pill("ok", $"{_rows} wierszy")
        : item.IsRequired(type) ? new Pill("warn", "pusty") : new Pill("muted", "pusty");

    public ICommand? Load { get; set; }
    public ICommand? Remove { get; set; }
    public ICommand? Apply { get; set; }

    public void SetPreview(ImportPreview? preview, string file)
    {
        _preview = preview;
        File = file;
        Issues.Clear();
        Lines.Clear();
        if (preview is not null)
        {
            foreach (var issue in preview.Issues.Take(30))
                Issues.Add(issue);
            if (preview.Issues.Count > 30)
                Issues.Add(Issue.Warning($"… i {preview.Issues.Count - 30} kolejnych"));
            foreach (var key in preview.Added.Take(15)) Lines.Add($"+ {key}");
            foreach (var line in preview.Changed.Take(15)) Lines.Add($"~ {line}");
            foreach (var key in preview.Removed.Take(15)) Lines.Add($"− {key}");
        }
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(StatusPill));
    }
}
