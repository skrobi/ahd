using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.MasterData.ViewModels;

/// <summary>Ekran Słowniki (F03): lista słowników globalnych, edycja w tabeli, zapis z walidacją, historia, Excel.</summary>
public sealed class MasterDataViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "master-data");

    private readonly DictionaryService _service;
    private readonly IFileDialogs _dialogs;
    private readonly List<DictRow> _removed = [];
    private DictionaryItem? _selectedDictionary;
    private DictRowViewModel? _selectedRow;
    private string _filter = "";
    private string _status = "";
    private bool _needsConfirmation;
    private ImportPreview? _preview;

    public MasterDataViewModel(DictionaryService service, IFileDialogs dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        Dictionaries = GlobalDictionaries.All.Select(spec => new DictionaryItem(spec, _service.Load(spec).Count)).ToList();
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => _filter.Length == 0 || o is DictRowViewModel row && row.Matches(_filter);

        AddRow = new RelayCommand(_ => DoAddRow(), _ => Spec is not null && _preview is null);
        RemoveRow = new RelayCommand(_ => DoRemoveRow(), _ => _selectedRow is not null && _preview is null);
        Save = new RelayCommand(_ => DoSave(confirmWarnings: false), _ => Spec is not null && _preview is null);
        SaveWithWarnings = new RelayCommand(_ => DoSave(confirmWarnings: true), _ => _needsConfirmation);
        Discard = new RelayCommand(_ => Reload("Zmiany odrzucone."), _ => Spec is not null && _preview is null);
        Export = new RelayCommand(_ => DoExport(), _ => Spec is not null);
        Import = new RelayCommand(_ => DoImport(), _ => Spec is not null && _preview is null);
        ApplyImport = new RelayCommand(_ => DoApplyImport(), _ => _preview is { HasErrors: false, HasChanges: true });
        CancelImport = new RelayCommand(_ => ClosePreview("Wczytanie anulowane – słownik bez zmian."), _ => _preview is not null);

        SelectedDictionary = Dictionaries.FirstOrDefault();
    }

    /// <summary>Widok przebudowuje kolumny tabeli po zmianie słownika.</summary>
    public event Action? ColumnsChanged;

    public IReadOnlyList<DictionaryItem> Dictionaries { get; }

    public ObservableCollection<DictRowViewModel> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public ObservableCollection<Issue> Issues { get; } = [];

    public ObservableCollection<HistoryItem> History { get; } = [];

    public ObservableCollection<string> PreviewLines { get; } = [];

    public ObservableCollection<Issue> PreviewIssues { get; } = [];

    public DictionarySpec? Spec => _selectedDictionary?.Spec;

    public DictionaryItem? SelectedDictionary
    {
        get => _selectedDictionary;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedDictionary))
                return;
            if (HasPendingChanges)
            {
                Status = "Masz niezapisane zmiany – zapisz albo odrzuć je przed zmianą słownika.";
                OnPropertyChanged();
                return;
            }
            _selectedDictionary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Spec));
            ColumnsChanged?.Invoke();
            ClosePreview(null);
            Reload("");
        }
    }

    public DictRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                LoadHistory();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value.Trim()))
                return;
            if (RowsView is IEditableCollectionView editable)
            {
                if (editable.IsEditingItem)
                    editable.CommitEdit();
                if (editable.IsAddingNew)
                    editable.CommitNew();
            }
            RowsView.Refresh();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool NeedsConfirmation
    {
        get => _needsConfirmation;
        private set => SetProperty(ref _needsConfirmation, value);
    }

    public bool HasIssues => Issues.Count > 0;

    public bool HasHistory => History.Count > 0;

    public bool HasPreview => _preview is not null;

    public string PreviewTitle => _preview is null ? "" : $"Podgląd wczytania: {_preview.FileName} – {_preview.Summary}";

    public bool HasPendingChanges => _removed.Count > 0 || Rows.Any(r => r.IsNew || r.IsModified);

    public ICommand AddRow { get; }
    public ICommand RemoveRow { get; }
    public ICommand Save { get; }
    public ICommand SaveWithWarnings { get; }
    public ICommand Discard { get; }
    public ICommand Export { get; }
    public ICommand Import { get; }
    public ICommand ApplyImport { get; }
    public ICommand CancelImport { get; }

    private void Reload(string status)
    {
        Rows.Clear();
        _removed.Clear();
        SelectedRow = null;
        if (Spec is { } spec)
        {
            foreach (var row in _service.Load(spec))
                Rows.Add(ToViewModel(spec, row));
            _selectedDictionary!.Count = Rows.Count;
        }
        SetIssues([]);
        NeedsConfirmation = false;
        Status = status;
    }

    private static DictRowViewModel ToViewModel(DictionarySpec spec, DictRow row) =>
        new(row.RowId, row.Version, spec.Columns.Select(c => (string?)ValueFormat.Display(c, row[c.Name])).ToArray());

    private void DoAddRow()
    {
        var row = new DictRowViewModel(null, null, new string?[Spec!.Columns.Count]);
        Rows.Add(row);
        SelectedRow = row;
        Status = "Dodano wiersz – uzupełnij wartości i zapisz.";
    }

    private void DoRemoveRow()
    {
        var row = _selectedRow!;
        if (!row.IsNew)
            _removed.Add(row.ToDictRow(Spec!));
        Rows.Remove(row);
        Status = "Wiersz usunięty z tabeli – zapisz, aby zamknąć jego obowiązywanie (historia zostaje).";
    }

    private void DoSave(bool confirmWarnings)
    {
        var spec = Spec!;
        var outcome = _service.Save(spec, Rows.Select(r => r.ToDictRow(spec)).ToList(), _removed, confirmWarnings);
        switch (outcome.Status)
        {
            case SaveStatus.Saved:
            case SaveStatus.NoChanges:
                Reload(outcome.Message);
                SetIssues(outcome.Issues);
                break;
            case SaveStatus.NeedsConfirmation:
                SetIssues(outcome.Issues);
                NeedsConfirmation = true;
                Status = outcome.Message;
                break;
            default:
                SetIssues(outcome.Issues);
                NeedsConfirmation = false;
                Status = outcome.Message;
                break;
        }
        Logger.Information("Zapis słownika {Dictionary}: {Status}", spec.Code, outcome.Status);
    }

    private void DoExport()
    {
        var spec = Spec!;
        var path = _dialogs.SaveExcel("Pobierz słownik do Excela", $"{spec.Name}.xlsx");
        if (path is null)
            return;
        Try(() =>
        {
            _service.Export(spec, path);
            Status = $"Zapisano plik {path}.";
        });
    }

    private void DoImport()
    {
        if (HasPendingChanges)
        {
            Status = "Masz niezapisane zmiany – zapisz albo odrzuć je przed wczytaniem z Excela.";
            return;
        }
        var spec = Spec!;
        var path = _dialogs.OpenExcel($"Wczytaj słownik „{spec.Name}” z Excela");
        if (path is null)
            return;
        Try(() =>
        {
            var preview = _service.PreviewImport(spec, path);
            _preview = preview;
            PreviewLines.Clear();
            foreach (var key in preview.Added) PreviewLines.Add($"+ {key}");
            foreach (var line in preview.Changed) PreviewLines.Add($"~ {line}");
            foreach (var key in preview.Removed) PreviewLines.Add($"− {key}");
            PreviewIssues.Clear();
            foreach (var issue in preview.Issues) PreviewIssues.Add(issue);
            Status = preview.HasErrors
                ? "Plik ma błędy (ERROR) – nie można go wczytać. Popraw plik i wczytaj ponownie."
                : preview.HasChanges ? "Sprawdź różnice i zatwierdź wczytanie." : "Plik nie zawiera zmian względem słownika.";
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(PreviewTitle));
        });
    }

    private void DoApplyImport()
    {
        var spec = Spec!;
        var outcome = _service.ApplyImport(spec, _preview!);
        ClosePreview(null);
        if (outcome.Status == SaveStatus.Saved)
            Reload(outcome.Message);
        else
            Status = outcome.Message;
        SetIssues(outcome.Issues);
    }

    private void ClosePreview(string? status)
    {
        _preview = null;
        PreviewLines.Clear();
        PreviewIssues.Clear();
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewTitle));
        if (status is not null)
            Status = status;
    }

    private void LoadHistory()
    {
        History.Clear();
        if (_selectedRow?.RowId is { } rowId && Spec is { } spec)
        {
            var versions = _service.History(rowId);
            for (var i = versions.Count - 1; i >= 0; i--)
            {
                var v = versions[i];
                var values = string.Join(" · ", spec.Columns.Select(c => $"{c.Name}: {ValueFormat.Display(c, v.Values.GetValueOrDefault(c.Name))}"));
                var note = v.SupersededAt is null
                    ? "bieżąca"
                    : i == versions.Count - 1
                        ? $"usunięta {v.SupersededAt:yyyy-MM-dd HH:mm} – {v.SupersededBy}"
                        : $"zastąpiona {v.SupersededAt:yyyy-MM-dd HH:mm}";
                History.Add(new HistoryItem($"{v.RecordedAt:yyyy-MM-dd HH:mm}", v.RecordedBy, values, note));
            }
        }
        OnPropertyChanged(nameof(HasHistory));
    }

    private void SetIssues(IReadOnlyList<Issue> issues)
    {
        Issues.Clear();
        foreach (var issue in issues)
            Issues.Add(issue);
        OnPropertyChanged(nameof(HasIssues));
    }

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Logger.Error(ex, "Operacja na pliku Excel");
            Status = $"Nie udało się: {ex.Message}";
        }
    }
}
