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
/// Definicje i reguły przebiegu (ekran Przebiegi): słowniki „Definicje przebiegów”, „Etapy przebiegów”, „Elementy kontrolne
/// etapów”, „Reguły uzgodnień”, „Kontrole przebiegu” – ta sama tabela słownika co na ekranie Słowniki (historia, walidacja przy
/// zapisie, Excel); „Kopia definicji wbudowanej” – wiersze definicji wbudowanej do edycji (nieaktywna, typ „Wszystkie”).
/// </summary>
public sealed class RunDictionaryEditorViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    private readonly DictionaryService _service;
    private readonly IFileDialogs _dialogs;
    private DictionarySpec? _spec;
    private string _status = "";

    public RunDictionaryEditorViewModel(DictionaryService service, IFileDialogs dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        Dictionaries = GlobalDictionaries.RunDictionaries.Select(GlobalDictionaries.Get).ToList();
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
        CopyBuiltIn = new AsyncRelayCommand(DoCopyBuiltIn, () => !Busy.IsBusy && !Table.HasPendingChanges);
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

    /// <summary>Zapis słownika – wywołujący odświeża definicję przebiegu programu.</summary>
    public event Action? Saved;

    public ICommand AddRow { get; }

    public ICommand RemoveRow { get; }

    public ICommand Save { get; }

    public ICommand Discard { get; }

    public ICommand Export { get; }

    public ICommand CopyBuiltIn { get; }

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

    /// <summary>
    /// Kopia definicji wbudowanej w słownikach (nazwa „Standardowy – kopia”, kolejny numer, gdy zajęta): nieaktywna, typ „Wszystkie”,
    /// przebieg „Każdy” – po zmianach wystarczy ją włączyć (kolumna Aktywna) albo zawęzić zakres.
    /// </summary>
    private async Task DoCopyBuiltIn()
    {
        try
        {
            var message = await Busy.Run("Kopiowanie definicji wbudowanej…", () =>
            {
                var headers = GlobalDictionaries.Get(GlobalDictionaries.RunDefinitionHeaders);
                var existing = _service.Load(headers);
                var code = Enumerable.Range(1, 99).Select(i => i == 1 ? "Standardowy – kopia" : $"Standardowy – kopia {i}")
                    .First(c => existing.All(r => !string.Equals(r["Definicja"], c, StringComparison.OrdinalIgnoreCase)));
                var definition = RunDefinitions.BuiltIn;
                static DictRow Row(params (string Column, string? Value)[] values) => new(null, null, values.ToDictionary(v => v.Column, v => v.Value));
                static string YesNo(bool value) => value ? "tak" : "nie";
                var saves = new (DictionarySpec Spec, IReadOnlyList<DictRow> Rows)[]
                {
                    (headers, [Row(("Definicja", code), ("Typ programu", RunCheckRules.AllTypes), ("Program", null), ("Przebieg", RunCheckRules.AnyRun),
                        ("Aktywna", "nie"), ("Opis", "Kopia definicji wbudowanej – dostosuj etapy i elementy, potem włącz (Aktywna) albo zawęź zakres."))]),
                    (GlobalDictionaries.Get(GlobalDictionaries.RunSteps), [.. definition.Steps.Select(s => Row(("Definicja", code),
                        ("Kolejność", s.No.ToString(CultureInfo.InvariantCulture)), ("Etap", s.Name), ("Akcja", s.Action), ("Bramka WARNING", s.WarningGate),
                        ("Zatwierdzenie", YesNo(s.Approval)), ("Opis", s.Description)))]),
                    (GlobalDictionaries.Get(GlobalDictionaries.RunStepElements), [.. definition.Steps.SelectMany(s => s.Elements.Select(e => Row(("Definicja", code),
                        ("Etap", s.Name), ("Element", e.Element), ("Aktywny", YesNo(e.Active)), ("Poziom", null), ("Próg", null), ("Opis", null))))]),
                };
                foreach (var (spec, rows) in saves)
                {
                    var outcome = _service.Save(spec, [.. _service.Load(spec), .. rows], [], confirmWarnings: true);
                    if (outcome.Status is not (SaveStatus.Saved or SaveStatus.NoChanges))
                        return $"Kopia nie zapisana ({spec.Name}): {outcome.Message} {string.Join("; ", outcome.Issues.Where(i => i.Level == CheckLevel.Error).Take(3).Select(i => i.Message))}";
                }
                return $"Zapisano definicję „{code}” (nieaktywna) z {definition.Steps.Count} etapami – zmień etapy i elementy, potem ustaw Aktywna = tak.";
            });
            await Reload(message);
            Saved?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Kopia definicji wbudowanej");
            Status = $"Kopia nieudana: {ex.Message}";
        }
    }
}
