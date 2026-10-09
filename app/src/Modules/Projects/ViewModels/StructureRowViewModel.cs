using System.ComponentModel;
using System.Windows;
using PzlEv.Modules.Projects.Models;
using PzlEv.Modules.Projects.Services;
using PzlEv.Shared.Utils.Files;

namespace PzlEv.Modules.Projects.ViewModels;

/// <summary>
/// Wiersz tabeli struktury projektu: komórki po kluczu kolumny (StructureEdits), wcięcie i rozwinięcie drzewa,
/// zmiany wpisane w wierszu do chwili zapisu (HasChanges – wiersz niezapisany albo z nieudanym zapisem), problem
/// komórki sprawdzany od razu po wpisaniu (Problems / ProblemText – podświetlenie i podpowiedź). IEditableObject –
/// Esc w tabeli cofa zmiany wiersza.
/// </summary>
public sealed class StructureRowViewModel(StructureRow row) : INotifyPropertyChanged, IEditableObject
{
    private readonly Dictionary<string, string?> _changes = [];
    private readonly Dictionary<string, Shared.Models.Issue> _problems = [];
    private Dictionary<string, string?>? _beforeEdit;
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public StructureRow Row => row;

    public string Id => row.Id;

    public int Depth => row.Depth;

    public bool IsObjective => row.Kind == GridRowKind.Objective;

    public bool IsVirtual => row.IsVirtual;

    /// <summary>Znacznik przy nazwie: węzeł wirtualny nakładki albo element wirtualny P1S (np. Paint).</summary>
    public string VirtualTag => IsObjective ? "węzeł" : "wirtualny";

    public bool IsGreyed => row.IsGreyed;

    public bool HasChildren => row.HasChildren;

    public Thickness Indent => new(row.Depth * 16, 0, 0, 0);

    public string Glyph => !row.HasChildren ? "" : _isExpanded ? "▾" : "▸";

    public string? Note => row.Note;

    public string? Gap => row.Gap;

    public bool HasGap => row.Gap is not null;

    public bool HasChanges => _changes.Count > 0;

    /// <summary>Znacznik WP można zmienić w tym wierszu (wiersz z kodem P1S).</summary>
    public bool CanEditWp => CanEdit(StructureEdits.Wp);

    /// <summary>Poziom problemu komórki według klucza kolumny: crit, warn albo pusty (kolor tła komórki).</summary>
    public CellTexts Problems => new(_problems.ToDictionary(p => p.Key, p => p.Value.Level == Shared.Models.Pipeline.CheckLevel.Error ? "crit" : "warn"));

    /// <summary>Opis problemu komórki według klucza kolumny (podpowiedź); brak problemu – null.</summary>
    public CellTexts ProblemText => new(_problems.ToDictionary(p => p.Key, p => $"{p.Value.LevelText}: {p.Value.Message}"), null);

    /// <summary>Tekst komórki według klucza kolumny (wiązanie „Problems[BacHours]”); brak – wartość domyślna.</summary>
    public sealed class CellTexts(IReadOnlyDictionary<string, string> texts, string? missing = "")
    {
        public string? this[string column] => texts.TryGetValue(column, out var text) ? text : missing;
    }

    /// <summary>Zmiany wpisane w wierszu, jeszcze niezapisane (klucz kolumny → wartość).</summary>
    public IReadOnlyDictionary<string, string?> Changes => _changes;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            Notify(nameof(IsExpanded));
            Notify(nameof(Glyph));
        }
    }

    /// <summary>Komórka: wpisana zmiana albo wartość z bazy (liczby w formacie polskim, daty RRRR-MM-DD).</summary>
    public string? this[string column]
    {
        get => _changes.TryGetValue(column, out var changed) ? changed : Value(column);
        set
        {
            if (!CanEdit(column))
                return;
            if ((value ?? "") == (Value(column) ?? ""))
                _changes.Remove(column);
            else
                _changes[column] = value;
            // WP odznaczone z powrotem – budżet i daty wpisane dla nowego WP nie mają już gdzie trafić.
            if (column == StructureEdits.Wp)
            {
                foreach (var key in _changes.Keys.Where(k => StructureEdits.ScheduleColumns.Contains(k) && !CanEdit(k)).ToList())
                {
                    _changes.Remove(key);
                    _problems.Remove(key);
                }
            }
            Check(column);
            Changed();
        }
    }

    /// <summary>Znacznik WP (checkbox): element P1S jest pakietem pracy z kosztami i budżetem.</summary>
    public bool IsWp
    {
        get => StructureEdits.IsChecked(this[StructureEdits.Wp]);
        set => this[StructureEdits.Wp] = value ? "true" : "false";
    }

    /// <summary>
    /// Czy komórkę można zmienić: StructureEdits.CanEdit, a budżet i daty także w wierszu, w którym WP jest właśnie
    /// zaznaczany (StructureEdits.CanEditWithNewWp) – WP z budżetem wklejone za jednym razem.
    /// </summary>
    public bool CanEdit(string column) =>
        StructureEdits.CanEdit(row, column)
        || _changes.TryGetValue(StructureEdits.Wp, out var wp) && StructureEdits.IsChecked(wp) && StructureEdits.CanEditWithNewWp(row, column);

    /// <summary>Odrzuca niezapisane zmiany wiersza.</summary>
    public void Revert()
    {
        _changes.Clear();
        _problems.Clear();
        Changed();
    }

    public void BeginEdit() => _beforeEdit ??= new Dictionary<string, string?>(_changes);

    public void CancelEdit()
    {
        if (_beforeEdit is null)
            return;
        _changes.Clear();
        foreach (var (key, value) in _beforeEdit)
            _changes[key] = value;
        _beforeEdit = null;
        _problems.Clear();
        foreach (var key in _changes.Keys)
            Check(key);
        Changed();
    }

    public void EndEdit() => _beforeEdit = null;

    private string? Value(string column) => column switch
    {
        StructureEdits.Name => row.Name,
        "WbsElement" => row.WbsElement,
        StructureEdits.P1s => row.P1s,
        StructureEdits.Wp => row.Wp is null ? "false" : "true",
        StructureEdits.Cam => row.Cam,
        StructureEdits.CostCategory => row.CostCategory,
        StructureEdits.BacHours => Amount(row.BacHours),
        StructureEdits.BacMaterial => Amount(row.BacMaterial),
        StructureEdits.Bac => Amount(row.Bac),
        StructureEdits.Start => row.Start,
        StructureEdits.Finish => row.Finish,
        "WpCount" => row.Wps.Count == 0 ? null : row.Wps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Acwp" => Amount(row.Acwp),
        StructureColumns.ActualStart => row.ActualStart,
        StructureColumns.ActualFinish => row.ActualFinish,
        StructureColumns.OpsBacHours => Optional(row.OpsBacHours),
        StructureColumns.PvHours => Optional(row.PvHours),
        StructureColumns.EvHours => Optional(row.EvHours),
        StructureColumns.AcHours => Optional(row.AcHours),
        StructureColumns.ActualMaterial => Optional(row.ActualMaterial),
        StructureColumns.PvCost => Optional(row.PvCost),
        StructureColumns.EvCost => Optional(row.EvCost),
        _ => null,
    };

    /// <summary>Sprawdzenie wpisanej wartości jak w tabeli słownika (kolumny „Harmonogram i budżet”: liczba, data; nazwa – wymagana).</summary>
    private void Check(string column)
    {
        var issue = column == StructureEdits.Name
            ? string.IsNullOrWhiteSpace(this[column]) ? Shared.Models.Issue.Error("pole wymagane") : null
            : StructureEdits.ScheduleColumns.Contains(column)
                ? Shared.Utils.Dictionaries.DictionaryCells.Check(ScheduleSpec, ScheduleSpec.Column(StructureEdits.DictionaryColumn(column))!, this[column], null) is { } problem
                  && problem.Message != "pole wymagane" ? problem : null
                : null;
        if (issue is null)
            _problems.Remove(column);
        else
            _problems[column] = issue;
    }

    private static readonly Shared.Models.Dictionaries.DictionarySpec ScheduleSpec = ProjectDictionaries.Base(ProjectDictionaries.ScheduleBudget);

    private void Changed()
    {
        Notify("Item[]");
        Notify(nameof(IsWp));
        Notify(nameof(HasChanges));
        Notify(nameof(Problems));
        Notify(nameof(ProblemText));
    }

    private static string? Amount(decimal value) => value == 0 ? null : PolishNumber.ToDisplay(value);

    /// <summary>Wartość z danych (PZLPROD, wyliczona): brak danych – pusto, zero – „0” (np. brak godzin na elemencie).</summary>
    private static string? Optional(decimal? value) => value is { } v ? PolishNumber.ToDisplay(Math.Round(v, 2)) : null;

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
