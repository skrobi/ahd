using System.Globalization;
using System.Windows.Input;
using PzlEv.Shared.Models;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Models.Pipeline;
using PzlEv.Shared.Utils.Dictionaries;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Dictionaries;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>
/// Słowniki przebiegu w oknie Kontrole (ekran Przebiegi): „Reguły uzgodnień”, „Kontrole przebiegu” (ustawienia dla typów
/// programów) – ta sama tabela słownika co na ekranie Słowniki (historia, walidacja przy zapisie, Excel).
/// </summary>
public sealed class RunDictionaryEditorViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly DictionaryService _service;
    private readonly IFileDialogs _dialogs;
    private DictionarySpec? _spec;
    private string _status = "";

    public RunDictionaryEditorViewModel(DictionaryService service, IFileDialogs dialogs, IReadOnlyList<string> codes)
    {
        _service = service;
        _dialogs = dialogs;
        Dictionaries = codes.Select(GlobalDictionaries.Get).ToList();
        AddRow = new RelayCommand(_ => { Table.AddRow(); Status = "Dodano wiersz – uzupełnij wartości i zapisz."; }, _ => _spec is not null && !Busy.IsBusy);
        RemoveRow = new RelayCommand(_ =>
        {
            var (removed, _) = Table.RemoveSelected();
            Status = removed == 0 ? "" : "Wiersze usunięte z tabeli – zapisz, aby zamknąć ich obowiązywanie (historia zostaje).";
        }, _ => Table.SelectedRow is not null && !Busy.IsBusy);
        Save = new AsyncRelayCommand(() => DoSave(confirmWarnings: false), () => _spec is not null && !Busy.IsBusy);
        Table.SaveWithWarnings = new AsyncRelayCommand(() => DoSave(confirmWarnings: true), () => Table.NeedsConfirmation && !Busy.IsBusy);
        Discard = new AsyncRelayCommand(() => Reload("Zmiany odrzucone."), () => _spec is not null && !Busy.IsBusy);
        Export = new AsyncRelayCommand(DoExport, () => _spec is not null && !Busy.IsBusy);
        Spec = Dictionaries[0];
    }

    public BusyState Busy { get; } = new();

    public IReadOnlyList<DictionarySpec> Dictionaries { get; }

    public DictionaryTableViewModel Table { get; } = new();

    public DictionarySpec? Spec
    {
        get => _spec;
        set
        {
            if (value is null || ReferenceEquals(value, _spec))
                return;
            Table.CommitEdits();
            if (Table.HasPendingChanges || Busy.IsBusy)
            {
                Status = Busy.IsBusy ? "Poczekaj na zakończenie bieżącej operacji." : "Masz niezapisane zmiany – zapisz albo odrzuć je przed zmianą słownika.";
                OnPropertyChanged();
                return;
            }
            _spec = value;
            OnPropertyChanged();
            _ = Reload("");
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>Zapis słownika – ekran Przebiegi odświeża szablony.</summary>
    public event Action? Saved;

    public ICommand AddRow { get; }

    public ICommand RemoveRow { get; }

    public ICommand Save { get; }

    public ICommand Discard { get; }

    public ICommand Export { get; }

    private async Task Reload(string status)
    {
        Table.Load(_spec, Array.Empty<DictRow>());
        if (_spec is not { } spec)
            return;
        try
        {
            var (rows, lookups) = await Busy.Run($"Wczytywanie słownika „{spec.Name}”…", () => (_service.Load(spec), _service.Lookups(spec)));
            if (!ReferenceEquals(spec, _spec))
                return;
            Table.Load(spec, rows.Select(r => (r, false)), lookups);
            Status = status;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Odczyt słownika {Dictionary}", spec.Code);
            Status = $"Nie udało się wczytać słownika: {ex.Message}";
        }
    }

    private async Task DoSave(bool confirmWarnings)
    {
        var spec = _spec!;
        Table.CommitEdits();
        var (working, removed) = Table.State();
        try
        {
            var outcome = await Busy.Run($"Zapisywanie słownika „{spec.Name}”…", () => _service.Save(spec, working, removed, confirmWarnings));
            switch (outcome.Status)
            {
                case SaveStatus.Saved:
                case SaveStatus.NoChanges:
                    await Reload(outcome.Message);
                    Table.SetIssues(outcome.Issues);
                    if (outcome.Status == SaveStatus.Saved)
                        Saved?.Invoke();
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
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Zapis słownika {Dictionary}", spec.Code);
            Status = $"Zapis nieudany: {ex.Message}";
        }
    }

    private async Task DoExport()
    {
        var spec = _spec!;
        var path = _dialogs.SaveExcel("Pobierz słownik do Excela", $"{spec.Name}.xlsx");
        if (path is null)
            return;
        try
        {
            await Busy.Run($"Zapisywanie słownika „{spec.Name}” do Excela…", () => { _service.Export(spec, path); return true; });
            Status = $"Zapisano plik {path}.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Eksport słownika {Dictionary}", spec.Code);
            Status = $"Nie udało się zapisać pliku: {ex.Message}";
        }
    }
}
