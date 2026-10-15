using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Hr;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Models;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Dictionaries;
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
    private DictionaryItem? _selectedDictionary;
    private string _status = "";
    private ImportPreview? _preview;
    private readonly IHrSource? _hr;
    private readonly ICostElementSource? _costElements;
    private bool _showP1s;
    private IReadOnlyList<HrPerson>? _hrPersons;

    /// <param name="hr">Pracownicy z PZLHRPROD (słownik Osoby); null – brak sekcji PzlHrProd w pzl-ev.json.</param>
    /// <param name="costElements">Cost elementy ostatniego importu ACTUALS – „Uzupełnij z ACTUALS” w Cost Category.</param>
    /// <param name="pzlProd">Struktura P1S (LOG.WBS) do podglądu; null – brak sekcji PzlProd w pzl-ev.json.</param>
    public MasterDataViewModel(DictionaryService service, IFileDialogs dialogs, IHrSource? hr = null, ICostElementSource? costElements = null,
        IPzlProdSource? pzlProd = null)
    {
        P1s = new P1sStructureViewModel(pzlProd);
        OpenP1s = new RelayCommand(_ =>
        {
            ShowP1s = true;
            P1s.EnsureLoaded();
        });
        _service = service;
        _dialogs = dialogs;
        _hr = hr;
        _costElements = costElements;
        Dictionaries = GlobalDictionaries.All.Where(spec => !spec.Hidden).Select(spec => new DictionaryItem(spec, 0)).ToList();
        Table.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DictionaryTableViewModel.SelectedRow))
                _ = LoadHistory();
        };

        AddRow = new RelayCommand(_ => DoAddRow(), _ => Spec is not null && _preview is null && !Busy.IsBusy);
        RemoveRow = new RelayCommand(_ => DoRemoveRow(), _ => Table.SelectedRow is not null && _preview is null && !Busy.IsBusy);
        Save = new AsyncRelayCommand(() => DoSave(confirmWarnings: false), () => Spec is not null && _preview is null && !Busy.IsBusy);
        Table.SaveWithWarnings = new AsyncRelayCommand(() => DoSave(confirmWarnings: true), () => Table.NeedsConfirmation && !Busy.IsBusy);
        Discard = new AsyncRelayCommand(() => Reload("Zmiany odrzucone."), () => Spec is not null && _preview is null && !Busy.IsBusy);
        Export = new AsyncRelayCommand(DoExport, () => Spec is not null && !Busy.IsBusy);
        Import = new AsyncRelayCommand(DoImport, () => Spec is not null && _preview is null && !Busy.IsBusy);
        ApplyImport = new AsyncRelayCommand(DoApplyImport, () => _preview is { HasErrors: false, HasChanges: true } && !Busy.IsBusy);
        ImportFromHr = new AsyncRelayCommand(DoImportFromHr, () => IsPersons && _preview is null && !Busy.IsBusy);
        FillFromActuals = new AsyncRelayCommand(DoFillFromActuals, () => IsCostCategory && _costElements is not null && _preview is null && !Busy.IsBusy);
        EditFormulas = new AsyncRelayCommand(DoEditFormulas, () => IsCasBurden && _preview is null && !Busy.IsBusy);
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

    public IReadOnlyList<DictionaryItem> Dictionaries { get; }

    /// <summary>Wiersze wybranego słownika – edycja w komórkach (wspólna tabela słownika).</summary>
    public DictionaryTableViewModel Table { get; } = new();

    public ObservableCollection<HistoryItem> History { get; } = [];

    public ObservableCollection<string> PreviewLines { get; } = [];

    public ObservableCollection<Issue> PreviewIssues { get; } = [];

    public DictionarySpec? Spec => _selectedDictionary?.Spec;

    public DictionaryItem? SelectedDictionary
    {
        get => _selectedDictionary;
        set
        {
            if (value is not null)
                ShowP1s = false;
            if (value is null || ReferenceEquals(value, _selectedDictionary))
                return;
            Table.CommitEdits();
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
            OnPropertyChanged(nameof(IsCostCategory));
            OnPropertyChanged(nameof(IsCasBurden));
            ClosePreview(null);
            _ = Reload("");
        }
    }

    /// <summary>Struktura P1S z PZLPROD – podgląd tylko do odczytu zamiast tabeli słownika.</summary>
    public P1sStructureViewModel P1s { get; }

    public ICommand OpenP1s { get; }

    public bool ShowP1s
    {
        get => _showP1s;
        private set
        {
            if (SetProperty(ref _showP1s, value))
                OnPropertyChanged(nameof(ShowDictionary));
        }
    }

    public bool ShowDictionary => !ShowP1s;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool HasHistory => History.Count > 0;

    public bool HasPreview => _preview is not null;

    public string PreviewTitle => _preview is null ? "" : $"Podgląd wczytania: {_preview.FileName} – {_preview.Summary}";

    public bool HasPendingChanges => Table.HasPendingChanges;

    public ICommand AddRow { get; }
    public ICommand RemoveRow { get; }
    public ICommand Save { get; }
    public ICommand Discard { get; }
    public ICommand Export { get; }
    public ICommand Import { get; }
    public ICommand ApplyImport { get; }
    public ICommand CancelImport { get; }
    public ICommand ImportFromHr { get; }
    public ICommand FillFromActuals { get; }
    public ICommand EditFormulas { get; }

    /// <summary>Wybrany słownik narzutów CAS (pule, grupy, wskaźniki) – „Formuły narzutów”.</summary>
    public bool IsCasBurden => Spec?.Code is GlobalDictionaries.CasPools or GlobalDictionaries.CasBurdenGroups or GlobalDictionaries.CasBurdenRates;

    /// <summary>Okno macierzy formuł narzutów CAS (true – zapisano). Ustawia moduł.</summary>
    public Func<bool> EditCasFormulas { get; set; } = () => false;

    /// <summary>Wybrany słownik Cost Category – „Uzupełnij z ACTUALS”.</summary>
    public bool IsCostCategory => Spec?.Code == GlobalDictionaries.CostCategory;

    /// <summary>Wybrany słownik Osoby – „Dodaj z HR”.</summary>
    public bool IsPersons => Spec?.Code == GlobalDictionaries.Persons;

    private async Task Reload(string status)
    {
        Table.Load(Spec, Array.Empty<DictRow>());
        if (Spec is { } spec)
        {
            var item = _selectedDictionary!;
            try
            {
                var (rows, lookups) = await Busy.Run($"Wczytywanie słownika „{spec.Name}”…", () => (_service.Load(spec), _service.Lookups(spec)));
                if (!ReferenceEquals(item, _selectedDictionary))
                    return;   // w międzyczasie wybrano inny słownik
                Table.Load(spec, rows.Select(r => (r, false)), lookups);
                item.Count = rows.Count;
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

    private void DoAddRow()
    {
        Table.AddRow();
        Status = "Dodano wiersz – uzupełnij wartości i zapisz.";
    }

    private void DoRemoveRow()
    {
        var (removed, _) = Table.RemoveSelected();
        Status = removed == 0 ? "" : "Wiersze usunięte z tabeli – zapisz, aby zamknąć ich obowiązywanie (historia zostaje).";
    }

    private async Task DoSave(bool confirmWarnings)
    {
        var spec = Spec!;
        Table.CommitEdits();
        var (working, removed) = Table.State();
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
                Table.SetIssues(outcome.Issues);
                break;
            case SaveStatus.NeedsConfirmation:
                Table.SetIssues(outcome.Issues, needsConfirmation: true);
                Status = outcome.Message;
                break;
            default:
                Table.SetIssues(outcome.Issues);
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
        Table.CommitEdits();
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

    /// <summary>
    /// Cost Category uzupełniony o cost elementy całego ostatniego importu ACTUALS, których w słowniku nie ma (opis
    /// z danych; Cost Category i „Rozliczeniowy” określa finansista). Podgląd jak przy wczytaniu z Excela – same nowe
    /// pozycje, nic nie jest zmieniane ani usuwane.
    /// </summary>
    private async Task DoFillFromActuals()
    {
        Table.CommitEdits();
        if (HasPendingChanges)
        {
            Status = "Masz niezapisane zmiany – zapisz albo odrzuć je przed uzupełnieniem z ACTUALS.";
            return;
        }
        var spec = Spec!;
        await Try(async () =>
        {
            var (preview, added, total) = await Busy.Run("Odczyt cost elementów z ostatniego importu ACTUALS…", () =>
            {
                var elements = _costElements!.CostElements();
                var (rows, count) = GlobalDictionaries.CostCategoryWithActuals(_service.Load(spec), elements);
                return (_service.PreviewRows(spec, rows, "ACTUALS – ostatni import"), count, elements.Count);
            });
            ShowPreview(preview, "ACTUALS");
            Status = added == 0
                ? $"Wszystkie cost elementy z ACTUALS ({total}) są już w słowniku."
                : $"Nowe cost elementy z ACTUALS: {added} (z {total}) – zatwierdź wczytanie, potem uzupełnij Cost Category i „Rozliczeniowy”.";
        });
    }

    /// <summary>
    /// „Formuły narzutów” (słowniki narzutów CAS): okno macierzy grupy × pule z edytorem formuły z klocków; zapis w oknie,
    /// po zamknięciu tabela jest odświeżana (nazwy pul i grup – z tabel słowników, formuły wskazują je identyfikatorem).
    /// </summary>
    private async Task DoEditFormulas()
    {
        Table.CommitEdits();
        if (HasPendingChanges)
        {
            Status = "Masz niezapisane zmiany – zapisz albo odrzuć je przed otwarciem formuł narzutów.";
            return;
        }
        if (EditCasFormulas())
            await Reload("Formuły narzutów CAS zapisane.");
    }

    /// <summary>Okno wyboru osób z HR (pracownicy, USRID już w słowniku) → wybrane; null – anulowano. Ustawia moduł.</summary>
    public Func<IReadOnlyList<HrPerson>, IReadOnlySet<string>, IReadOnlyList<HrPerson>?> PickPersons { get; set; } = (_, _) => null;

    /// <summary>
    /// „Dodaj z HR” (słownik Osoby): okno szukania pracowników PZLHRPROD (HR.ORG), wybrane osoby trafiają do podglądu jak
    /// przy wczytaniu z Excela – same nowe osoby, nic nie jest zmieniane ani usuwane.
    /// </summary>
    private async Task DoImportFromHr()
    {
        if (_hr is null)
        {
            Status = "Brak połączenia z PZLHRPROD – dodaj sekcję PzlHrProd w pzl-ev.json (Environments.<Env>.PzlHrProd: Server, Database, Schema).";
            return;
        }
        Table.CommitEdits();
        if (HasPendingChanges)
        {
            Status = "Masz niezapisane zmiany – zapisz albo odrzuć je przed dodaniem osób z HR.";
            return;
        }
        var spec = Spec!;
        await Try(async () =>
        {
            var (persons, current) = await Busy.Run("Wczytywanie pracowników z PZLHRPROD (HR.ORG)…", () =>
                (_hrPersons ??= _hr.Persons(), _service.Load(spec)));
            var existing = current.Select(r => r["USRID"]).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (PickPersons(persons, existing) is not { Count: > 0 } picked)
            {
                Status = "Nie wybrano osób do dodania.";
                return;
            }
            var (rows, added) = GlobalDictionaries.PersonsWithAdded(current, picked);
            var preview = await Busy.Run("Sprawdzanie osób do dodania…", () => _service.PreviewRows(spec, rows, "PZLHRPROD HR.ORG"));
            ShowPreview(preview, "Dane z HR");
            Status = $"Nowe osoby z HR: {added} – sprawdź i zatwierdź wczytanie.";
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
        // Pierwsze problemy (ERROR przed WARNING) – tysiące ostrzeżeń dużego słownika zablokowałyby ekran.
        foreach (var issue in preview.Issues.OrderBy(i => i.Level == Shared.Models.Pipeline.CheckLevel.Error ? 0 : 1).Take(PreviewLimit))
            PreviewIssues.Add(issue);
        if (preview.Issues.Count > PreviewLimit)
            PreviewIssues.Add(Issue.Warning($"… i {preview.Issues.Count - PreviewLimit} kolejnych problemów"));
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
            Table.SetIssues(outcome.Issues);
        });
    }

    /// <summary>Pierwsze linie zmian danego rodzaju – pełne wczytanie dużego słownika to tysiące wierszy.</summary>
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
        if (Table.SelectedRow?.RowId is { } rowId && Spec is { } spec)
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
            if (Table.SelectedRow?.RowId != rowId)
                return;   // w międzyczasie zaznaczono inny wiersz
            for (var i = versions.Count - 1; i >= 0; i--)
            {
                var v = versions[i];
                var values = string.Join(" · ", spec.Columns.Select(c => $"{c.Name}: {(c.Lookup is not null && Table.Options(c) is { } options
                    ? DictionaryCells.Display(v.Values.GetValueOrDefault(c.Name), options)
                    : ValueFormat.Display(c, v.Values.GetValueOrDefault(c.Name)))}"));
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

    private async Task Try(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (FileInUse.Is(ex))
        {
            Logger.Warning("{Context}: plik otwarty w innym programie – {Message}", "Operacja na pliku Excel", ex.Message);
            Status = FileInUse.Describe(ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Operacja na pliku Excel");
            Status = $"Nie udało się: {ex.Message}";
        }
    }
}
