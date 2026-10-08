using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Data;
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

    private const int PreviewLimit = 200;

    private readonly DictionaryService _service;
    private readonly IFileDialogs _dialogs;
    private readonly List<DictRow> _removed = [];
    private DictionaryItem? _selectedDictionary;
    private DictRowViewModel? _selectedRow;
    private string _filter = "";
    private string _status = "";
    private bool _needsConfirmation;
    private ImportPreview? _preview;
    private readonly IHrSource? _hr;

    /// <param name="hr">Pracownicy z PZLHRPROD (słownik Osoby); null – brak sekcji PzlHrProd w pzl-ev.json.</param>
    public MasterDataViewModel(DictionaryService service, IFileDialogs dialogs, IHrSource? hr = null)
    {
        _service = service;
        _dialogs = dialogs;
        _hr = hr;
        Dictionaries = GlobalDictionaries.All.Select(spec => new DictionaryItem(spec, 0)).ToList();
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => _filter.Length == 0 || o is DictRowViewModel row && row.Matches(_filter);

        AddRow = new RelayCommand(_ => DoAddRow(), _ => Spec is not null && _preview is null && !Busy.IsBusy);
        RemoveRow = new RelayCommand(_ => DoRemoveRow(), _ => _selectedRow is not null && _preview is null && !Busy.IsBusy);
        Save = new AsyncRelayCommand(() => DoSave(confirmWarnings: false), () => Spec is not null && _preview is null && !Busy.IsBusy);
        SaveWithWarnings = new AsyncRelayCommand(() => DoSave(confirmWarnings: true), () => _needsConfirmation && !Busy.IsBusy);
        Discard = new AsyncRelayCommand(() => Reload("Zmiany odrzucone."), () => Spec is not null && _preview is null && !Busy.IsBusy);
        Export = new AsyncRelayCommand(DoExport, () => Spec is not null && !Busy.IsBusy);
        Import = new AsyncRelayCommand(DoImport, () => Spec is not null && _preview is null && !Busy.IsBusy);
        ApplyImport = new AsyncRelayCommand(DoApplyImport, () => _preview is { HasErrors: false, HasChanges: true } && !Busy.IsBusy);
        ImportFromHr = new AsyncRelayCommand(DoImportFromHr, () => IsPersons && _preview is null && !Busy.IsBusy);
        CancelImport = new RelayCommand(_ => ClosePreview("Wczytanie anulowane – słownik bez zmian."), _ => _preview is not null && !Busy.IsBusy);

        SelectedDictionary = Dictionaries.FirstOrDefault();
        _ = LoadCounts();
    }

    /// <summary>Operacja w tle (pasek „Trwa: …”): odczyt i zapis w bazie, pliki Excel.</summary>
    public BusyState Busy { get; } = new();

    private async Task LoadCounts()
    {
        try
        {
            var counts = await Busy.Run("Wczytywanie liczby wierszy słowników…", () => Dictionaries.Select(d => _service.Load(d.Spec).Count).ToList());
            for (var i = 0; i < counts.Count; i++)
                Dictionaries[i].Count = counts[i];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Liczba wierszy słowników");
        }
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
            if (HasPendingChanges || Busy.IsBusy)
            {
                Status = Busy.IsBusy ? "Poczekaj na zakończenie bieżącej operacji." : "Masz niezapisane zmiany – zapisz albo odrzuć je przed zmianą słownika.";
                OnPropertyChanged();
                return;
            }
            _selectedDictionary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Spec));
            OnPropertyChanged(nameof(IsPersons));
            ColumnsChanged?.Invoke();
            ClosePreview(null);
            _ = Reload("");
        }
    }

    public DictRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                _ = LoadHistory();
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
    public ICommand ImportFromHr { get; }

    /// <summary>Wybrany słownik Osoby – „Wczytaj z HR”.</summary>
    public bool IsPersons => Spec?.Code == GlobalDictionaries.Persons;

    private async Task Reload(string status)
    {
        Rows.Clear();
        _removed.Clear();
        SelectedRow = null;
        SetIssues([]);
        NeedsConfirmation = false;
        if (Spec is { } spec)
        {
            var item = _selectedDictionary!;
            try
            {
                var rows = await Busy.Run($"Wczytywanie słownika „{spec.Name}”…", () => _service.Load(spec));
                if (!ReferenceEquals(item, _selectedDictionary))
                    return;   // w międzyczasie wybrano inny słownik
                foreach (var row in rows)
                    Rows.Add(ToViewModel(spec, row));
                item.Count = Rows.Count;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.Error(ex, "Odczyt słownika {Dictionary}", spec.Code);
                Status = $"Nie udało się wczytać słownika: {ex.Message}";
                return;
            }
        }
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

    private async Task DoSave(bool confirmWarnings)
    {
        var spec = Spec!;
        var (working, removed) = (Rows.Select(r => r.ToDictRow(spec)).ToList(), _removed.ToList());
        SaveOutcome outcome;
        try
        {
            outcome = await Busy.Run($"Zapisywanie słownika „{spec.Name}”…", () => _service.Save(spec, working, removed, confirmWarnings));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Zapis słownika {Dictionary}", spec.Code);
            Status = $"Zapis nieudany: {ex.Message}";
            return;
        }
        switch (outcome.Status)
        {
            case SaveStatus.Saved:
            case SaveStatus.NoChanges:
                await Reload(outcome.Message);
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

    private async Task DoExport()
    {
        var spec = Spec!;
        var path = _dialogs.SaveExcel("Pobierz słownik do Excela", $"{spec.Name}.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            await Busy.Run($"Zapisywanie słownika „{spec.Name}” do Excela…", () => _service.Export(spec, path));
            Status = $"Zapisano plik {path}.";
        });
    }

    private async Task DoImport()
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
        await Try(async () =>
        {
            var preview = await Busy.Run("Wczytywanie i sprawdzanie pliku Excel…", () => _service.PreviewImport(spec, path));
            ShowPreview(preview, "Plik");
        });
    }

    /// <summary>Słownik Osoby z PZLHRPROD (HR.ORG): podgląd różnic jak przy wczytaniu z Excela – zawartość zastępowana.</summary>
    private async Task DoImportFromHr()
    {
        if (_hr is null)
        {
            Status = "Brak połączenia z PZLHRPROD – dodaj sekcję PzlHrProd w pzl-ev.json (Environments.<Env>.PzlHrProd: Server, Database, Schema).";
            return;
        }
        if (HasPendingChanges)
        {
            Status = "Masz niezapisane zmiany – zapisz albo odrzuć je przed wczytaniem z HR.";
            return;
        }
        var spec = Spec!;
        await Try(async () =>
        {
            var preview = await Busy.Run("Wczytywanie pracowników z PZLHRPROD (HR.ORG)…", () =>
                _service.PreviewRows(spec, GlobalDictionaries.PersonRows(_hr.Persons()), "PZLHRPROD HR.ORG"));
            ShowPreview(preview, "Dane z HR");
        });
    }

    private void ShowPreview(ImportPreview preview, string source)
    {
        _preview = preview;
        PreviewLines.Clear();
        AddPreviewLines("+", preview.Added);
        AddPreviewLines("~", preview.Changed);
        AddPreviewLines("−", preview.Removed);
        PreviewIssues.Clear();
        foreach (var issue in preview.Issues) PreviewIssues.Add(issue);
        Status = preview.HasErrors
            ? $"{source} ma błędy (ERROR) – nie można wczytać."
            : preview.HasChanges ? "Sprawdź różnice i zatwierdź wczytanie." : $"{source} nie zawiera zmian względem słownika.";
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewTitle));
    }

    private async Task DoApplyImport()
    {
        var (spec, preview) = (Spec!, _preview!);
        await Try(async () =>
        {
            var outcome = await Busy.Run($"Zapisywanie słownika „{spec.Name}” z Excela…", () => _service.ApplyImport(spec, preview));
            ClosePreview(null);
            if (outcome.Status == SaveStatus.Saved)
                await Reload(outcome.Message);
            else
                Status = outcome.Message;
            SetIssues(outcome.Issues);
        });
    }

    /// <summary>Pierwsze linie zmian danego rodzaju – pełne wczytanie dużego słownika (raport mapowań) to tysiące wierszy.</summary>
    private void AddPreviewLines(string sign, IReadOnlyList<string> lines)
    {
        foreach (var line in lines.Take(PreviewLimit))
            PreviewLines.Add($"{sign} {line}");
        if (lines.Count > PreviewLimit)
            PreviewLines.Add($"{sign} … i {lines.Count - PreviewLimit} więcej");
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

    private async Task LoadHistory()
    {
        History.Clear();
        OnPropertyChanged(nameof(HasHistory));
        if (_selectedRow?.RowId is { } rowId && Spec is { } spec)
        {
            IReadOnlyList<Shared.Models.Db.DictionaryEntryRow> versions;
            try
            {
                versions = await Task.Run(() => _service.History(rowId));   // krótki odczyt – bez paska, nie blokuje okna
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.Error(ex, "Historia wiersza {RowId}", rowId);
                return;
            }
            if (_selectedRow?.RowId != rowId)
                return;   // w międzyczasie zaznaczono inny wiersz
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

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Operacja na pliku Excel");
            Status = $"Nie udało się: {ex.Message}";
        }
    }
}
