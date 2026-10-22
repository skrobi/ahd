using System.Windows.Input;
using PzlEv.Modules.Runs.Data;
using PzlEv.Modules.Runs.Services;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Files;
using PzlEv.Shared.Utils.Process;
using PzlEv.Shared.Utils.Ui.Dialogs;
using PzlEv.Shared.Utils.Ui.Mvvm;
using Serilog;

namespace PzlEv.Modules.Runs.ViewModels;

/// <summary>Wybór uruchomienia w oknie: wiersz uruchomienia i opis.</summary>
public sealed record AcwpPinOption(RunPinRow Pin)
{
    public string Label => $"{Pin.Code} #{Pin.PinNo} – {Pin.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}{Pin.ClosingText}";
}

/// <summary>Wybór okresu w wariancie okres do okresu.</summary>
public sealed record AcwpPeriodOption(int Year, int Period)
{
    public string Label => $"{Year}/{Period:00}";
}

/// <summary>
/// Okno „Zmiany ACWP” (docs/funkcjonalnosc.md, F06): porównanie kosztu brutto po rodzaju kosztu – dwa uruchomienia przebiegu
/// (domyślnie: ostatnie uruchomienie poprzedniego okresu i najnowsze) albo okres do okresu w jednym uruchomieniu; kafelki
/// rodzajów, drzewo zmian (AcwpChanges), linie źródłowe wybranego węzła (wszystkie jego komórki) – linia „było” i „jest” w jednym wierszu (AcwpChanges.Lines,
/// stan plików z chwili uruchomienia) i wykres przyrostów (osobne okno). Koszty w walucie przebiegu (CAS – po narzutach); linie – dane
/// źródłowe ACTUALS.
/// </summary>
public sealed class AcwpChangesViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext("Module", "runs");

    /// <summary>Liczba linii źródłowych w tabeli (po porównaniu, od największej zmiany).</summary>
    public const int LineCount = 2000;

    /// <summary>Liczba linii źródłowych jednej strony wczytywanych do porównania (wg |kwoty|).</summary>
    public const int LineFetch = 100_000;

    public const string AllKinds = "Wszystkie";

    private readonly IRunStore _store;
    private readonly Func<IProjectProcessSource?> _processes;
    private readonly IFileDialogs _dialogs;
    private readonly AcwpChangesRequest _request;
    private readonly string _project;
    private readonly IReadOnlySet<string>? _wbsElements;
    private readonly decimal _jumpFactor = AcwpChanges.JumpFactor;
    private AcwpChart _chart = new([], []);
    private readonly Dictionary<long, IReadOnlyList<RunBalanceRow>> _balances = [];
    private RunDictionaryData? _dictionaries;
    private bool _periodMode;
    private AcwpPinOption? _beforePin;
    private AcwpPinOption? _afterPin;
    private AcwpPinOption? _periodPin;
    private IReadOnlyList<AcwpPeriodOption> _periods = [];
    private AcwpPeriodOption? _beforePeriod;
    private AcwpPeriodOption? _afterPeriod;
    private string _kind;
    private bool _onlyChanges = true;
    private bool _showAll;
    private AcwpComparison _comparison = new([], []);
    private AcwpChangeNode? _selected;
    private IReadOnlyList<AcwpLineChange> _lines = [];
    private string _linesTitle = "Wybierz pozycję drzewa – linie źródłowe wszystkich komórek pozycji.";
    private string? _error;
    private string _note = "";
    private int _version;

    public AcwpChangesViewModel(IRunStore store, Func<IProjectProcessSource?> processes, AcwpChangesRequest request, IFileDialogs dialogs)
    {
        _store = store;
        _processes = processes;
        _dialogs = dialogs;
        _request = request;
        _project = request.Project;
        _wbsElements = request.WbsElements is null ? null : new HashSet<string>(request.WbsElements, StringComparer.OrdinalIgnoreCase);
        _kind = request.Kind ?? AllKinds;
        Title = $"Zmiany ACWP – {_project}" + (string.IsNullOrWhiteSpace(request.Scope) ? "" : $" – {request.Scope}");
        Swap = new RelayCommand(_ => (BeforePin, AfterPin) = (AfterPin, BeforePin));
        Export = new AsyncRelayCommand(DoExport, () => !Busy.IsBusy && Pins.Count > 0);
    }

    /// <summary>Drzewo zmian (wszystkie pozycje, bez limitu) i przyrosty do Excela.</summary>
    public ICommand Export { get; }

    /// <summary>
    /// Wykres przyrostów (przycisk „Wykres”): przyrosty miesięczne rodzajów (12 okresów do ostatniego okresu danych „jest”), skoki – próg
    /// reguły „Skok przyrostu kosztu”, skumulowany koszt rodzajów wykresu.
    /// </summary>
    public AcwpChart Chart { get => _chart; private set => SetProperty(ref _chart, value); }

    /// <summary>Uruchomienie danych wykresu (opis okna wykresu).</summary>
    public string ChartSource => (PeriodMode ? PeriodPin : AfterPin)?.Label ?? "";

    public string ChartNote => $"Słupki – koszt zaksięgowany w okresie (uruchomienie „jest”), obramowanie w kolorze ostrzeżenia – skok: przyrost > {_jumpFactor:0.##} × "
                               + $"średni przyrost z {AcwpChanges.IncrementPeriods} poprzednich okresów (jak reguła startowa „Skok przyrostu kosztu”); "
                               + "linia – koszt skumulowany na koniec okresu (prawa oś).";

    public string Title { get; }

    public BusyState Busy { get; } = new();

    public ICommand Swap { get; }

    public IReadOnlyList<string> Kinds { get; } = [AllKinds, .. RunCostKinds.All];

    public IReadOnlyList<AcwpPinOption> Pins { get; private set; } = [];

    /// <summary>Wariant okres do okresu (false – dwa uruchomienia).</summary>
    public bool PeriodMode
    {
        get => _periodMode;
        set
        {
            if (SetProperty(ref _periodMode, value))
            {
                OnPropertyChanged(nameof(RunMode));
                _ = Recompute();
            }
        }
    }

    public bool RunMode { get => !PeriodMode; set => PeriodMode = !value; }

    public AcwpPinOption? BeforePin { get => _beforePin; set { if (SetProperty(ref _beforePin, value)) _ = Recompute(); } }

    public AcwpPinOption? AfterPin { get => _afterPin; set { if (SetProperty(ref _afterPin, value)) _ = Recompute(); } }

    public AcwpPinOption? PeriodPin { get => _periodPin; set { if (SetProperty(ref _periodPin, value)) _ = Recompute(periodsChanged: true); } }

    public IReadOnlyList<AcwpPeriodOption> Periods { get => _periods; private set => SetProperty(ref _periods, value); }

    public AcwpPeriodOption? BeforePeriod { get => _beforePeriod; set { if (SetProperty(ref _beforePeriod, value)) _ = Recompute(); } }

    public AcwpPeriodOption? AfterPeriod { get => _afterPeriod; set { if (SetProperty(ref _afterPeriod, value)) _ = Recompute(); } }

    public string Kind { get => _kind; set { if (SetProperty(ref _kind, value)) _ = Recompute(); } }

    public bool OnlyChanges { get => _onlyChanges; set { if (SetProperty(ref _onlyChanges, value)) _ = Recompute(); } }

    /// <summary>Wszystkie pozycje na poziomie drzewa (false – pierwsze AcwpChanges.DefaultTop i „pozostałe”).</summary>
    public bool ShowAll { get => _showAll; set { if (SetProperty(ref _showAll, value)) _ = Recompute(); } }

    public IReadOnlyList<AcwpKindTotal> Totals => _comparison.Totals;

    public IReadOnlyList<AcwpChangeNode> Tree => _comparison.Tree;

    public bool IsEmpty => _comparison.Tree.Count == 0;

    public AcwpChangeNode? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                _ = LoadLines(value);
        }
    }

    public IReadOnlyList<AcwpLineChange> Lines { get => _lines; private set => SetProperty(ref _lines, value); }

    public string LinesTitle { get => _linesTitle; private set => SetProperty(ref _linesTitle, value); }

    /// <summary>Opis porównania: zakres, waluta, ograniczenia.</summary>
    public string Note { get => _note; private set => SetProperty(ref _note, value); }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;

    /// <summary>Uruchomienia programu i domyślny wybór: najnowsze i ostatnie uruchomienie wcześniejszego okresu.</summary>
    public async Task Load()
    {
        await Try(async () =>
        {
            var (pins, dictionaries) = await Busy.Run("Wczytywanie uruchomień przebiegu…", () =>
                (_store.Pins(_project, 60).Where(p => p.Rows > 0 && p.Status is not (RunStatuses.Running or RunStatuses.Failed or RunStatuses.Abandoned)).ToList(),
                    _store.Dictionaries(_project)));
            _dictionaries = dictionaries;
            Pins = pins.Select(p => new AcwpPinOption(p)).ToList();
            OnPropertyChanged(nameof(Pins));
            if (Pins.Count == 0)
            {
                Error = "Program nie ma zakończonych uruchomień przebiegu z danymi – „Uruchom przebieg” na ekranie programu.";
                return;
            }
            // Wynik kontroli: uruchomienie wyniku i poprzednie uruchomienie wcześniejszego tygodnia (jak porównanie w przebiegu); inaczej
            // najnowsze i ostatnie uruchomienie wcześniejszego okresu.
            var latest = Pins.FirstOrDefault(p => p.Pin.PinId == _request.PinId) ?? Pins[0];
            var older = Pins.SkipWhile(p => p != latest).Skip(1).ToList();
            var previousPeriod = (_request.PinId is not null
                    ? older.FirstOrDefault(p => p.Pin.Code != latest.Pin.Code)
                    : older.FirstOrDefault(p => string.CompareOrdinal(Period(p.Pin.Code), Period(latest.Pin.Code)) < 0))
                ?? older.FirstOrDefault() ?? latest;
            _afterPin = latest;
            _beforePin = previousPeriod;
            _periodPin = latest;
            OnPropertyChanged(nameof(AfterPin));
            OnPropertyChanged(nameof(BeforePin));
            OnPropertyChanged(nameof(PeriodPin));
            await Recompute(periodsChanged: true);
        });
    }

    private async Task Recompute(bool periodsChanged = false)
    {
        if (Pins.Count == 0)
            return;
        var version = ++_version;
        await Try(async () =>
        {
            var pins = PeriodMode ? [PeriodPin] : new[] { BeforePin, AfterPin };
            if (pins.Any(p => p is null))
                return;
            var missing = pins.Select(p => p!.Pin).Where(p => !_balances.ContainsKey(p.PinId)).DistinctBy(p => p.PinId).ToList();
            if (missing.Count > 0)
            {
                var loaded = await Busy.Run("Wczytywanie danych uruchomień…", () => missing.Select(p => (p.PinId, Rows: Costs(_store.Balance(p.PinId)))).ToList());
                foreach (var (id, rows) in loaded)
                    _balances[id] = rows;
            }
            if (version != _version)
                return;
            if (PeriodMode && (periodsChanged || Periods.Count == 0) && PeriodPin is { } pin)
            {
                Periods = AcwpChanges.PeriodsOf(_balances[pin.Pin.PinId]).Select(p => new AcwpPeriodOption(p.Year, p.Period)).ToList();
                _afterPeriod = Periods.FirstOrDefault();
                _beforePeriod = Periods.ElementAtOrDefault(1) ?? _afterPeriod;
                OnPropertyChanged(nameof(AfterPeriod));
                OnPropertyChanged(nameof(BeforePeriod));
            }
            var kind = Kind == AllKinds ? null : Kind;
            var top = ShowAll ? int.MaxValue : AcwpChanges.DefaultTop;
            if (PeriodMode)
            {
                if (BeforePeriod is null || AfterPeriod is null)
                    return;
                Compare(AcwpChanges.Periods(_balances[PeriodPin!.Pin.PinId], (BeforePeriod.Year, BeforePeriod.Period), (AfterPeriod.Year, AfterPeriod.Period),
                    _dictionaries?.CostKinds, _dictionaries?.CostElementNames, kind, _wbsElements, OnlyChanges, top, _request.CostElement),
                    $"Koszt zaksięgowany w okresie {BeforePeriod.Label} (było) i {AfterPeriod.Label} (jest) – {PeriodPin.Label}.");
            }
            else
                Compare(AcwpChanges.Runs(_balances[BeforePin!.Pin.PinId], _balances[AfterPin!.Pin.PinId], _dictionaries?.CostKinds, _dictionaries?.CostElementNames,
                    kind, _wbsElements, OnlyChanges, top, _request.CostElement), $"Stan narastająco: było – {BeforePin.Label}, jest – {AfterPin.Label}.");
            Chart = AcwpChanges.Chart(_balances[(PeriodMode ? PeriodPin! : AfterPin!).Pin.PinId], _dictionaries?.CostKinds, kind, _wbsElements,
                _request.CostElement, _jumpFactor);
            OnPropertyChanged(nameof(ChartSource));
        });
    }

    private void Compare(AcwpComparison comparison, string description)
    {
        _comparison = comparison;
        OnPropertyChanged(nameof(Totals));
        OnPropertyChanged(nameof(Tree));
        OnPropertyChanged(nameof(IsEmpty));
        Note = description + " Koszt brutto bez rozliczenia wychodzącego w walucie kosztów przebiegu (CAS – po narzutach CAS); rodzaj – Cost Category"
            + (_wbsElements is null ? "" : $"; elementy WBS wiersza struktury: {_wbsElements.Count}")
            + ". Dane przypięte w uruchomieniach – bez słownika „Wykluczenia” programu, więc ACWP struktury (ostatni import, po wykluczeniach) może się różnić.";
        Selected = null;
    }

    /// <summary>Koszty uruchomienia procesem programu (CAS – po narzutach CAS, jak w przebiegu); błąd – dane źródłowe.</summary>
    private IReadOnlyList<RunBalanceRow> Costs(IReadOnlyList<RunBalanceRow> rows)
    {
        var process = _processes()?.For(_project) ?? ProjectProcess.Default;
        if (!process.TransformsCosts)
            return rows;
        var (transformed, error) = RunService.Transformed(process, rows);
        if (error is null)
            return transformed;
        Logger.Warning("Zmiany ACWP {Project}: {Error} – dane źródłowe", _project, error);
        return rows;
    }

    private async Task LoadLines(AcwpChangeNode? node)
    {
        Lines = [];
        if (node is null || node.Cells.Count == 0)
            return;
        await Try(async () =>
        {
            var sides = PeriodMode
                ? new[] { ("było", PeriodPin!.Pin, AcwpChanges.LineScope(node.Cells, before: true, foldClosing: true)),
                    ("jest", PeriodPin!.Pin, AcwpChanges.LineScope(node.Cells, before: false, foldClosing: true)) }
                : [("było", BeforePin!.Pin, AcwpChanges.LineScope(node.Cells, before: true, foldClosing: false)),
                    ("jest", AfterPin!.Pin, AcwpChanges.LineScope(node.Cells, before: false, foldClosing: false))];
            var onlyChanges = OnlyChanges;
            var (result, lines) = await Busy.Run($"Linie źródłowe {node.Level.ToLowerInvariant()} {node.Label}…", () =>
            {
                var loaded = sides.Select(s =>
                {
                    if (s.Item3 is not { } scope)
                        return (Side: s.Item1, Rows: (IReadOnlyList<RunActualsLine>)[], Truncated: false);
                    var query = _store.ActualsLines(new RunActualsScope(s.Item2.PinId, null, scope.CostElements, scope.Year, scope.Period, scope.WbsElements),
                        s.Item2.FinishedAt ?? s.Item2.StartedAt, s.Item2.AmountField ?? ProjectSettings.DefaultActualsAmount, LineFetch);
                    return (Side: s.Item1, Rows: AcwpChanges.InScope(query.Rows, scope), Truncated: query.Count > query.Rows.Count);
                }).ToList();
                return (loaded, AcwpChanges.Lines(loaded[0].Rows, loaded[1].Rows, onlyChanges));
            });
            if (!ReferenceEquals(node, _selected))
                return;
            Lines = lines.Take(LineCount).ToList();
            var states = lines.GroupBy(l => l.State).ToDictionary(g => g.Key, g => g.Count());
            LinesTitle = $"Linie źródłowe – {node.Level.ToLowerInvariant()} {node.Label}, komórek: {node.Cells.Count} (dane ACTUALS, pole kwoty uruchomienia): "
                + string.Join("; ", result.Select(r => $"{r.Side} – {r.Rows.Count} linii, suma {r.Rows.Sum(l => l.Amount ?? 0):#,0.00}"
                                                       + (r.Truncated ? $" (porównano {LineFetch} największych linii zapytania)" : "")))
                + $". Nowe: {states.GetValueOrDefault(AcwpLineStates.New)}, usunięte: {states.GetValueOrDefault(AcwpLineStates.Removed)}, "
                + $"zmienione: {states.GetValueOrDefault(AcwpLineStates.Changed)}"
                + (onlyChanges ? "" : $", bez zmian: {states.GetValueOrDefault(AcwpLineStates.Unchanged)}")
                + (lines.Count > LineCount ? $"; pokazano {LineCount} z największą zmianą" : "") + ".";
        });
    }

    private async Task DoExport()
    {
        if (Pins.Count == 0 || (PeriodMode ? PeriodPin : AfterPin) is null)
            return;
        var path = _dialogs.SaveExcel("Zmiany ACWP do Excela", $"{_project}_zmiany_ACWP.xlsx");
        if (path is null)
            return;
        await Try(async () =>
        {
            var kind = Kind == AllKinds ? null : Kind;
            var full = PeriodMode
                ? AcwpChanges.Periods(_balances[PeriodPin!.Pin.PinId], (BeforePeriod!.Year, BeforePeriod.Period), (AfterPeriod!.Year, AfterPeriod.Period),
                    _dictionaries?.CostKinds, _dictionaries?.CostElementNames, kind, _wbsElements, OnlyChanges, int.MaxValue, _request.CostElement)
                : AcwpChanges.Runs(_balances[BeforePin!.Pin.PinId], _balances[AfterPin!.Pin.PinId], _dictionaries?.CostKinds, _dictionaries?.CostElementNames,
                    kind, _wbsElements, OnlyChanges, int.MaxValue, _request.CostElement);
            var increments = AcwpChanges.Increments(_balances[(PeriodMode ? PeriodPin! : AfterPin!).Pin.PinId], _dictionaries?.CostKinds, kind, _wbsElements,
                _request.CostElement, _jumpFactor);
            await Busy.Run("Zapis zmian ACWP do Excela…", () =>
            {
                ExcelTableWriter.WriteSheets(path,
                [
                    ("Zmiany", ["Rodzaj", "Cost element", "Element WBS", "Okres", "Było", "Jest", "Zmiana"],
                        AcwpChanges.Flatten(full.Tree).Select(r => (IReadOnlyList<object?>)[r.Kind, r.CostElement, r.WbsElement, r.Period, r.Before, r.After, r.Delta])),
                    ("Rodzaje", ["Rodzaj", "Było", "Jest", "Zmiana", "Zmiana %"],
                        full.Totals.Select(t => (IReadOnlyList<object?>)[t.Kind, t.Before, t.After, t.Delta, t.Percent])),
                    ("Przyrosty", ["Rodzaj", "Okres", "Przyrost", "Średni przyrost", "Skok"],
                        increments.SelectMany(s => s.Increments.Select(i => (IReadOnlyList<object?>)[s.Kind, i.Label, i.Amount, i.Average, i.IsJump ? "tak" : ""]))),
                    ("Opis", ["Opis"], [[Title], [Note]]),
                ]);
                return true;
            });
        });
    }

    // „R-PAF-2026-10-T41” → „2026-10”.
    private static string Period(string code) => code.Split('-') is { Length: >= 5 } parts ? $"{parts[^3]}-{parts[^2]}" : code;

    private async Task Try(Func<Task> action)
    {
        try
        {
            Error = null;
            await action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Logger.Error(ex, "Zmiany ACWP {Project}", _project);
            Error = $"Nie udało się wczytać zmian ACWP: {ex.Message}";
        }
    }
}
